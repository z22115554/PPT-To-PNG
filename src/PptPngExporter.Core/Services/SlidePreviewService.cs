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
    public SlidePreview GetPreview(string sourcePath, EnginePreference preference, int thumbnailWidth, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(sourcePath);

        if (!File.Exists(LongPath.Extended(fullPath)))
            throw new ConversionException("找不到這個檔案，可能已被移動、改名或刪除。");

        var cacheDir = Path.Combine(CacheRoot, BuildCacheKey(fullPath, thumbnailWidth));

        if (TryReadCache(cacheDir) is { Count: > 0 } cached)
        {
            _logger.Info($"使用既有的縮圖快取：{Path.GetFileName(fullPath)}（{cached.Count} 張）");
            return new SlidePreview { SourcePath = fullPath, ThumbnailPaths = cached };
        }

        // 先產生到暫存位置，全部成功後才搬到快取，避免中途取消留下半套縮圖
        var staging = cacheDir + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
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
                    var produced = converter.Convert(request, null, cancellationToken);
                    if (produced.Count == 0) throw new ConversionException("這份簡報沒有任何投影片。");

                    Directory.CreateDirectory(CacheRoot);
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

    private IReadOnlyList<ISlideConverter> Order(EnginePreference preference) => preference switch
    {
        EnginePreference.PowerPointOnly => _converters.Where(c => c.Engine == ConversionEngine.PowerPoint).ToList(),
        EnginePreference.LibreOfficeOnly => _converters.Where(c => c.Engine == ConversionEngine.LibreOffice).ToList(),
        _ => _converters
            .OrderBy(c => c.Engine == ConversionEngine.PowerPoint ? 0 : c.Engine == ConversionEngine.LibreOffice ? 1 : 2)
            .ToList()
    };

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

    private static string BuildCacheKey(string fullPath, int width)
    {
        var info = new FileInfo(fullPath);
        var raw = $"{fullPath.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}|{width}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..24];
    }

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
