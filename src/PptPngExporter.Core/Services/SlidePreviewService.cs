using System.Security.Cryptography;
using System.Text;
using PptPngExporter.Core.Converters;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Parsing;

namespace PptPngExporter.Core.Services;

/// <summary>一份簡報的縮圖結果。</summary>
public sealed class SlidePreview
{
    public required string SourcePath { get; init; }
    public required IReadOnlyList<string> ThumbnailPaths { get; init; }
    public int SlideCount => ThumbnailPaths.Count;
}

/// <summary>
/// 產生投影片縮圖，供「從縮圖挑選頁面」使用。
///
/// 直接沿用既有的轉換引擎，只是把寬度調小、輸出到暫存資料夾，
/// 因此不需要為預覽另外維護一套算繪程式碼。
///
/// 結果會依「檔案路徑 + 最後修改時間 + 檔案大小 + 縮圖寬度」快取，
/// 所以重新開啟挑選視窗時是即時的；簡報一旦被修改，快取自然失效。
/// </summary>
public sealed class SlidePreviewService
{
    public const int DefaultThumbnailWidth = 400;

    private readonly IReadOnlyList<ISlideConverter> _converters;
    private readonly IAppLogger _logger;

    public SlidePreviewService(IReadOnlyList<ISlideConverter> converters, IAppLogger? logger = null)
    {
        _converters = converters ?? throw new ArgumentNullException(nameof(converters));
        _logger = logger ?? NullLogger.Instance;
    }

    public static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PptPngExporter", "preview");

    /// <summary>
    /// 取得（必要時產生）指定簡報的所有投影片縮圖，依頁碼排序。
    /// </summary>
    /// <param name="progress">
    /// 逐頁進度。第一次預覽一份 300 頁的簡報要跑好幾分鐘，沒有這個的話進度條會整段停在 0%。
    /// 命中快取時不會回報（因為是瞬間完成的）。
    /// </param>
    public SlidePreview GetPreview(
        string sourcePath,
        EnginePreference preference,
        int thumbnailWidth,
        CancellationToken cancellationToken,
        IProgress<SlideProgress>? progress = null)
    {
        var fullPath = Path.GetFullPath(sourcePath);

        if (!File.Exists(LongPath.Extended(fullPath)))
            throw new ConversionException("找不到這個檔案，可能已被移動、改名或刪除。");

        var candidates = Order(preference).Where(c => c.IsAvailable()).ToList();

        // 快取鍵包含產生縮圖的引擎：PowerPoint 與 LibreOffice 的算繪結果不同，
        // 不能讓 LibreOffice 產生的縮圖被 PowerPoint 模式沿用，否則預覽會與正式輸出不一致。
        foreach (var converter in candidates)
        {
            var dir = Path.Combine(CacheRoot, BuildCacheKey(fullPath, thumbnailWidth, converter.Engine));
            if (TryReadCache(dir) is { Count: > 0 } cached)
            {
                _logger.Info($"使用既有的縮圖快取：{Path.GetFileName(fullPath)}（{cached.Count} 張，{converter.DisplayName}）");
                return new SlidePreview { SourcePath = fullPath, ThumbnailPaths = cached };
            }
        }

        // 先產生到暫存位置，全部成功後才搬到快取，避免中途取消留下半套縮圖
        Directory.CreateDirectory(CacheRoot);
        var staging = Path.Combine(CacheRoot, "tmp-" + Guid.NewGuid().ToString("N")[..10]);
        TryDelete(staging);
        Directory.CreateDirectory(staging);

        try
        {
            var request = new ConversionRequest
            {
                SourcePath = fullPath,
                OutputDirectory = staging,
                Pages = PageRangeSpec.All,
                ImageWidth = thumbnailWidth,
                FileNamePrefix = string.Empty,
                Numbering = FileNumbering.OriginalPage,
                NumberDigits = 4
            };

            var failures = new List<string>();

            foreach (var converter in Order(preference))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!converter.IsAvailable())
                {
                    if (converter.UnavailableReason is { Length: > 0 } reason)
                        failures.Add($"{converter.DisplayName}：{reason}");
                    continue;
                }

                try
                {
                    var produced = converter.Convert(request, progress, cancellationToken);
                    if (produced.Count == 0) throw new ConversionException("這份簡報沒有任何投影片。");

                    // 用「實際成功的引擎」當作快取鍵，後備轉換的結果才不會被誤認成主引擎的
                    var cacheDir = Path.Combine(CacheRoot, BuildCacheKey(fullPath, thumbnailWidth, converter.Engine));
                    TryDelete(cacheDir);
                    Directory.Move(staging, cacheDir);

                    var files = TryReadCache(cacheDir) ?? Array.Empty<string>();
                    return new SlidePreview { SourcePath = fullPath, ThumbnailPaths = files };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"{converter.DisplayName} 產生 {Path.GetFileName(fullPath)} 的縮圖失敗：{ex.Message}");
                    failures.Add($"{converter.DisplayName}：{ex.Message}");
                    TryDelete(staging);
                    Directory.CreateDirectory(staging);
                }
            }

            throw new ConversionException(failures.Count > 0
                ? string.Join("；", failures)
                : "沒有可用的轉換方式，無法產生預覽。");
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>超過這個時間沒被用到的快取會在啟動時清掉。</summary>
    public static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(14);

    /// <summary>快取總量上限。超過時從最久沒用到的開始刪。</summary>
    public const long CacheBudgetBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// 清掉過期或超量的縮圖快取。程式啟動時呼叫。回傳釋放的位元組數。
    ///
    /// 快取鍵包含簡報的最後修改時間與程式版本，所以每改一次簡報、每更新一次程式，
    /// 就會多出一整套快取，舊的永遠不會被用到。沒有這個清理機制的話，
    /// 一份常改的 300 頁簡報可以在幾個月內累積數 GB，而使用者不會知道要去按「清除快取」。
    /// </summary>
    public static long SweepCache(IAppLogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;

        try
        {
            if (!Directory.Exists(CacheRoot)) return 0;

            var entries = new List<(string Path, long Size, DateTime LastUsed)>();

            foreach (var dir in Directory.EnumerateDirectories(CacheRoot))
            {
                try
                {
                    var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                    var size = 0L;
                    var lastUsed = Directory.GetCreationTimeUtc(dir);

                    foreach (var file in files)
                    {
                        var info = new FileInfo(file);
                        size += info.Length;
                        if (info.LastWriteTimeUtc > lastUsed) lastUsed = info.LastWriteTimeUtc;
                    }

                    entries.Add((dir, size, lastUsed));
                }
                catch
                {
                    // 讀不到的資料夾就跳過，不要因此中斷整個清理
                }
            }

            var freed = 0L;
            var now = DateTime.UtcNow;

            // 先刪過期的
            var survivors = new List<(string Path, long Size, DateTime LastUsed)>();
            foreach (var entry in entries)
            {
                if (now - entry.LastUsed > CacheMaxAge)
                {
                    if (TryDeleteReporting(entry.Path)) freed += entry.Size;
                }
                else
                {
                    survivors.Add(entry);
                }
            }

            // 還是超過預算的話，從最久沒用到的開始刪
            var total = survivors.Sum(e => e.Size);
            if (total > CacheBudgetBytes)
            {
                foreach (var entry in survivors.OrderBy(e => e.LastUsed))
                {
                    if (total <= CacheBudgetBytes) break;
                    if (!TryDeleteReporting(entry.Path)) continue;

                    total -= entry.Size;
                    freed += entry.Size;
                }
            }

            if (freed > 0) log.Info($"已清理縮圖快取 {freed / 1024d / 1024d:0.#} MB。");
            return freed;
        }
        catch (Exception ex)
        {
            log.Warn("清理縮圖快取時發生問題：" + ex.Message);
            return 0;
        }
    }

    private static bool TryDeleteReporting(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>清除所有縮圖快取。</summary>
    public static long ClearCache()
    {
        try
        {
            if (!Directory.Exists(CacheRoot)) return 0;

            var size = Directory.EnumerateFiles(CacheRoot, "*", SearchOption.AllDirectories)
                .Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });

            Directory.Delete(CacheRoot, recursive: true);
            return size;
        }
        catch
        {
            return 0;
        }
    }

    private IReadOnlyList<ISlideConverter> Order(EnginePreference preference)
        => EngineAvailability.Order(_converters, preference);

    private static IReadOnlyList<string>? TryReadCache(string cacheDir)
    {
        try
        {
            if (!Directory.Exists(cacheDir)) return null;
            var files = Directory.GetFiles(cacheDir, "*.png");
            if (files.Length == 0) return null;

            // 檔名是補到四位的原始頁碼，字典序即為頁序
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 快取鍵。除了檔案本身與寬度，還包含<b>產生縮圖的引擎</b>與<b>程式版本</b>：
    /// 換引擎或改版導致算繪結果不同時，舊快取必須自動失效。
    /// </summary>
    private static string BuildCacheKey(string fullPath, int width, ConversionEngine engine)
    {
        var info = new FileInfo(fullPath);
        var raw = string.Join('|',
            fullPath.ToLowerInvariant(),
            info.LastWriteTimeUtc.Ticks,
            info.Length,
            width,
            engine,
            AppVersion);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"{engine.ToString().ToLowerInvariant()}-{Convert.ToHexString(hash)[..20]}";
    }

    private static string AppVersion { get; } =
        typeof(SlidePreviewService).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // 清理失敗不影響主流程
        }
    }
}
