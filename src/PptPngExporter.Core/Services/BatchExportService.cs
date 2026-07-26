using System.Diagnostics;
using PptPngExporter.Core.Converters;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Models;

namespace PptPngExporter.Core.Services;

/// <summary>
/// 依序處理多份簡報。設計重點：
/// 1. 單一檔案失敗只會標記該檔失敗，不會中斷整批工作。
/// 2. 引擎依偏好排序，前一個引擎失敗時自動改用下一個。
/// 3. 取消時停止排入新工作，尚未處理的檔案標記為「已取消」。
/// </summary>
public sealed class BatchExportService
{
    /// <summary>支援的簡報副檔名。</summary>
    public static readonly IReadOnlyList<string> SupportedExtensions = new[] { ".ppt", ".pptx", ".pps", ".ppsx" };

    /// <summary>暫存資料夾的名稱前綴。</summary>
    private const string StagingPrefix = "~pptpng-tmp-";

    private readonly IReadOnlyList<ISlideConverter> _converters;
    private readonly IAppLogger _logger;

    public BatchExportService(IReadOnlyList<ISlideConverter> converters, IAppLogger? logger = null)
    {
        _converters = converters ?? throw new ArgumentNullException(nameof(converters));
        _logger = logger ?? NullLogger.Instance;
    }

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext)
               && SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>所有簡報套用同一組頁面設定的簡便版本。</summary>
    public BatchExportReport Run(
        IReadOnlyList<string> sourceFiles,
        ExportOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
        => Run(sourceFiles.Select(ExportJob.For).ToList(), options, progress, cancellationToken);

    /// <summary>
    /// 每份簡報可帶自己的頁面選擇（縮圖勾選的情況）。
    /// </summary>
    public BatchExportReport Run(
        IReadOnlyList<ExportJob> jobs,
        ExportOptions options,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(options);

        var results = jobs.Select(j => new ExportResult { SourcePath = j.SourcePath }).ToList();
        var total = results.Count;
        var cancelled = false;

        LongPath.EnsureDirectory(options.OutputRoot);
        SweepStaleStaging(options.OutputRoot);

        var hasPowerPoint = _converters.Any(c => c.Engine == ConversionEngine.PowerPoint && c.IsAvailable());
        var hasLibreOffice = _converters.Any(c => c.Engine == ConversionEngine.LibreOffice && c.IsAvailable());
        var blocker = EngineAvailability.DescribeBlocker(options.Engine, hasPowerPoint, hasLibreOffice);

        var ordered = EngineAvailability.Order(_converters, options.Engine);
        if (blocker is not null || ordered.Count == 0)
        {
            var reason = blocker ?? EngineAvailability.DescribeBlocker(options.Engine, false, false)!;
            foreach (var r in results)
            {
                r.Status = ExportStatus.Failed;
                r.ErrorMessage = reason;
            }
            return new BatchExportReport { Results = results, OutputRoot = options.OutputRoot, WasCancelled = false };
        }

        var prefix = FileNameSanitizer.SanitizePrefix(options.FileNamePrefix);
        var width = ExportOptions.ClampWidth(options.ImageWidth);

        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];

            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                result.Status = ExportStatus.Cancelled;
                continue;
            }

            result.Status = ExportStatus.Running;
            progress?.Report(new ProgressReport
            {
                FilesCompleted = index,
                FilesTotal = total,
                CurrentFileName = result.SourceName,
                Message = $"正在處理：{result.SourceName}"
            });

            var stopwatch = Stopwatch.StartNew();
            try
            {
                ProcessSingle(result, jobs[index], ordered, options, prefix, width, index, total, progress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                result.Status = ExportStatus.Cancelled;
            }
            catch (Exception ex)
            {
                // 保險層：任何未預期的例外都只影響這一個檔案
                _logger.Error($"處理 {result.SourceName} 時發生未預期的錯誤。", ex);
                result.Status = ExportStatus.Failed;
                result.ErrorMessage = "發生未預期的錯誤：" + ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                result.Duration = stopwatch.Elapsed;
            }

            progress?.Report(new ProgressReport
            {
                FilesCompleted = index + 1,
                FilesTotal = total,
                CurrentFileName = result.SourceName,
                Message = result.Status == ExportStatus.Success
                    ? $"{result.SourceName} 已完成（{result.ImageCount} 張）"
                    : $"{result.SourceName}：{result.ErrorMessage ?? result.StatusText}"
            });
        }

        if (cancellationToken.IsCancellationRequested) cancelled = true;

        return new BatchExportReport
        {
            Results = results,
            OutputRoot = options.OutputRoot,
            WasCancelled = cancelled
        };
    }

    private void ProcessSingle(
        ExportResult result,
        ExportJob job,
        IReadOnlyList<ISlideConverter> converters,
        ExportOptions options,
        string prefix,
        int width,
        int index,
        int total,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(LongPath.Extended(Path.GetFullPath(result.SourcePath))))
        {
            result.Status = ExportStatus.Failed;
            result.ErrorMessage = "找不到這個檔案，可能已被移動、改名或刪除。";
            return;
        }

        var folderName = FileNameSanitizer.Sanitize(Path.GetFileNameWithoutExtension(result.SourcePath), "簡報");

        var slideProgress = new Progress<SlideProgress>(sp => progress?.Report(new ProgressReport
        {
            FilesCompleted = index,
            FilesTotal = total,
            CurrentFileName = result.SourceName,
            SlidesCompleted = sp.Completed,
            SlidesTotal = sp.Total,
            Message = $"{result.SourceName}：第 {Math.Min(sp.Completed + 1, sp.Total)} / {sp.Total} 頁"
        }));

        var failures = new List<string>();

        foreach (var converter in converters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!converter.IsAvailable())
            {
                if (converter.UnavailableReason is { Length: > 0 } reason) failures.Add($"{converter.DisplayName}：{reason}");
                continue;
            }

            // 每次嘗試都用全新的暫存資料夾。
            // 這樣即使前一個引擎留下半成品且刪除失敗（檔案被防毒或索引服務鎖住），
            // 下一個引擎也絕對不會寫進同一個資料夾。
            var staging = CreateStagingDirectory(options.OutputRoot);

            var request = new ConversionRequest
            {
                SourcePath = result.SourcePath,
                OutputDirectory = staging,
                Pages = job.Pages ?? options.Pages,
                ImageWidth = width,
                FileNamePrefix = prefix,
                Numbering = options.Numbering,
                NumberDigits = options.NumberDigits
            };

            try
            {
                _logger.Info($"使用 {converter.DisplayName} 轉換 {result.SourceName}。");
                var files = converter.Convert(request, slideProgress, cancellationToken);

                var finalDirectory = PublishStaging(staging, options.OutputRoot, folderName);

                result.Status = ExportStatus.Success;
                result.EngineUsed = converter.Engine;
                result.ImageCount = files.Count;
                result.OutputDirectory = finalDirectory;
                result.ErrorMessage = null;
                return;
            }
            catch (OperationCanceledException)
            {
                // 已經產生的圖片仍然保留給使用者，只是狀態標記為「已取消」
                if (HasFiles(staging))
                {
                    result.OutputDirectory = PublishStaging(staging, options.OutputRoot, folderName);
                    result.ImageCount = CountFiles(result.OutputDirectory);
                    result.EngineUsed = converter.Engine;
                }
                else
                {
                    DiscardDirectory(staging);
                }
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn($"{converter.DisplayName} 轉換 {result.SourceName} 失敗：{ex.Message}");
                failures.Add($"{converter.DisplayName}：{ex.Message}");
                DiscardDirectory(staging);
            }
        }

        result.Status = ExportStatus.Failed;
        result.ErrorMessage = failures.Count > 0 ? string.Join("；", failures) : "沒有可用的轉換方式。";
        result.OutputDirectory = null;
    }

    /// <summary>暫存資料夾建立在輸出根目錄底下，確保與正式位置同一個磁碟區，搬移才會是瞬間完成。</summary>
    private static string CreateStagingDirectory(string outputRoot)
    {
        var path = Path.Combine(outputRoot, StagingPrefix + Guid.NewGuid().ToString("N")[..10]);
        LongPath.EnsureDirectory(path);
        return path;
    }

    /// <summary>把暫存資料夾搬到最終位置。名稱在搬移前才決定，避免與期間新建的資料夾撞名。</summary>
    private string PublishStaging(string staging, string outputRoot, string folderName)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var target = UniquePathResolver.ResolveDirectory(outputRoot, folderName);
            try
            {
                Directory.Move(LongPath.Extended(staging), LongPath.Extended(target));
                return target;
            }
            catch (IOException ex) when (attempt < 5)
            {
                _logger.Warn($"搬移輸出資料夾失敗（第 {attempt} 次）：{ex.Message}");
                Thread.Sleep(150);
            }
        }

        // 搬不動就退而求其次，逐檔複製過去
        var fallback = UniquePathResolver.ResolveDirectory(outputRoot, folderName);
        LongPath.EnsureDirectory(fallback);
        foreach (var file in Directory.EnumerateFiles(LongPath.Extended(staging)))
        {
            var name = Path.GetFileName(file);
            File.Copy(file, LongPath.Extended(Path.Combine(fallback, name)), overwrite: false);
        }
        DiscardDirectory(staging);
        return fallback;
    }

    private static bool HasFiles(string directory)
    {
        try { return Directory.Exists(LongPath.Extended(directory)) && Directory.EnumerateFiles(LongPath.Extended(directory)).Any(); }
        catch { return false; }
    }

    private static int CountFiles(string? directory)
    {
        try { return directory is null ? 0 : Directory.GetFiles(LongPath.Extended(directory)).Length; }
        catch { return 0; }
    }

    /// <summary>清掉上次執行留下的暫存資料夾（例如程式被強制結束）。只動超過一天的，避免影響同時執行的另一個執行個體。</summary>
    private void SweepStaleStaging(string outputRoot)
    {
        try
        {
            if (!Directory.Exists(LongPath.Extended(outputRoot))) return;

            foreach (var dir in Directory.EnumerateDirectories(LongPath.Extended(outputRoot), StagingPrefix + "*"))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddDays(-1))
                        Directory.Delete(dir, recursive: true);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("清理舊暫存資料夾時發生問題：" + ex.Message);
        }
    }

    /// <summary>刪除本次為單一檔案建立的輸出資料夾（含其中的半成品）。</summary>
    private void DiscardDirectory(string directory)
    {
        try
        {
            var extended = LongPath.Extended(directory);
            if (Directory.Exists(extended)) Directory.Delete(extended, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.Warn($"清理輸出資料夾時發生問題：{ex.Message}");
        }
    }

}
