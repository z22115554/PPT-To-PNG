using PptPngExporter.Core.Converters;

namespace PptPngExporter.Core.Services;

public sealed class ScanResult
{
    public required IReadOnlyList<string> Files { get; init; }

    /// <summary>因為達到上限而停止掃描。</summary>
    public bool ReachedLimit { get; init; }

    /// <summary>掃描過程中被略過的資料夾數（多半是沒有權限）。</summary>
    public int SkippedDirectories { get; init; }
}

/// <summary>
/// 從檔案與資料夾路徑中找出支援的簡報。
///
/// 兩個必須注意的地方：
/// 1. 這個方法可能耗時很久（網路磁碟機、深層目錄），呼叫端必須在背景執行緒上跑。
/// 2. <c>Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories)</c> 走的是
///    相容性設定，<c>IgnoreInaccessible = false</c>，遇到沒有權限的子資料夾會直接擲出
///    <see cref="UnauthorizedAccessException"/> 並中斷整個掃描 —— 使用者會什麼都掃不到。
///    因此這裡明確指定 <see cref="EnumerationOptions"/>。
/// </summary>
public static class PresentationScanner
{
    public const int DefaultMaxFiles = 2000;

    public static ScanResult Scan(
        IEnumerable<string> paths,
        int maxFiles = DefaultMaxFiles,
        CancellationToken cancellationToken = default,
        IAppLogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        var reachedLimit = false;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // 關鍵：沒有權限的資料夾要略過，而不是讓整個掃描失敗
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
            MatchType = MatchType.Simple,
            ReturnSpecialDirectories = false
        };

        foreach (var path in paths)
        {
            if (reachedLimit) break;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (File.Exists(path))
                {
                    if (BatchExportService.IsSupported(path)) Add(path);
                    continue;
                }

                if (!Directory.Exists(path)) continue;

                foreach (var file in Directory.EnumerateFiles(path, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!BatchExportService.IsSupported(file)) continue;
                    if (!Add(file)) { reachedLimit = true; break; }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                skipped++;
            }
            catch (Exception ex)
            {
                skipped++;
                log.Warn($"掃描 {path} 時發生問題：{ex.Message}");
            }
        }

        found.Sort(StringComparer.CurrentCulture);

        return new ScanResult
        {
            Files = found,
            ReachedLimit = reachedLimit,
            SkippedDirectories = skipped
        };

        bool Add(string file)
        {
            string full;
            try { full = Path.GetFullPath(file); }
            catch { return true; }

            if (seen.Add(full)) found.Add(full);
            return found.Count < maxFiles;
        }
    }
}
