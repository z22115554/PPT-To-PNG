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

        var ordered = OrderConverters(options.Engine);
        if (ordered.Count == 0)
        {
            var reason = DescribeNoEngine(options.Engine);
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
                CleanUpEmptyOutput(result);
            }
            catch (Exception ex)
            {
                // 保險層：任何未預期的例外都只影響這一個檔案
                _logger.Error($"處理 {result.SourceName} 時發生未預期的錯誤。", ex);
                result.Status = ExportStatus.Failed;
                result.ErrorMessage = "發生未預期的錯誤：" + ex.Message;
                CleanUpEmptyOutput(result);
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
        var outputDirectory = UniquePathResolver.ResolveDirectory(options.OutputRoot, folderName);
        result.OutputDirectory = outputDirectory;

        var request = new ConversionRequest
        {
            SourcePath = result.SourcePath,
            OutputDirectory = outputDirectory,
            // 這份簡報若有單獨挑選的頁面就用它，否則沿用整批的設定
            Pages = job.Pages ?? options.Pages,
            ImageWidth = width,
            FileNamePrefix = prefix,
            Numbering = options.Numbering,
            NumberDigits = options.NumberDigits
        };

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

            try
            {
                _logger.Info($"使用 {converter.DisplayName} 轉換 {result.SourceName}。");
                var files = converter.Convert(request, slideProgress, cancellationToken);

                result.Status = ExportStatus.Success;
                result.EngineUsed = converter.Engine;
                result.ImageCount = files.Count;
                result.OutputDirectory = outputDirectory;
                result.ErrorMessage = null;
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Warn($"{converter.DisplayName} 轉換 {result.SourceName} 失敗：{ex.Message}");
                failures.Add($"{converter.DisplayName}：{ex.Message}");

                // 這個資料夾是本次新建且專屬於這個檔案的，整個丟棄是安全的。
                // 不這麼做的話，下一個引擎會把圖片寫進已有半成品的資料夾裡，造成頁面重複或混雜。
                DiscardDirectory(outputDirectory);
            }
        }

        result.Status = ExportStatus.Failed;
        result.ErrorMessage = failures.Count > 0
            ? string.Join("；", failures)
            : "沒有可用的轉換方式。";
        result.OutputDirectory = null;
        DiscardDirectory(outputDirectory);
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

    /// <summary>失敗或取消時，移除還沒有任何圖片的空資料夾，避免留下垃圾。</summary>
    private void CleanUpEmptyOutput(ExportResult result)
    {
        var dir = result.OutputDirectory;
        if (string.IsNullOrEmpty(dir)) return;

        try
        {
            var extended = LongPath.Extended(dir);
            if (!Directory.Exists(extended)) return;

            if (!Directory.EnumerateFileSystemEntries(extended).Any())
            {
                Directory.Delete(extended);
                if (result.Status != ExportStatus.Success) result.OutputDirectory = null;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"清理輸出資料夾時發生問題：{ex.Message}");
        }
    }

    private IReadOnlyList<ISlideConverter> OrderConverters(EnginePreference preference) => preference switch
    {
        EnginePreference.PowerPointOnly => _converters.Where(c => c.Engine == ConversionEngine.PowerPoint).ToList(),
        EnginePreference.LibreOfficeOnly => _converters.Where(c => c.Engine == ConversionEngine.LibreOffice).ToList(),
        _ => _converters
            .OrderBy(c => c.Engine == ConversionEngine.PowerPoint ? 0 : c.Engine == ConversionEngine.LibreOffice ? 1 : 2)
            .ToList()
    };

    private static string DescribeNoEngine(EnginePreference preference) => preference switch
    {
        EnginePreference.PowerPointOnly => "目前設定為只用 PowerPoint 轉換，但這台電腦沒有偵測到 PowerPoint。",
        EnginePreference.LibreOfficeOnly => "目前設定為只用 LibreOffice 轉換，但這台電腦沒有偵測到 LibreOffice。",
        _ => "找不到可用的轉換方式，請安裝 Microsoft PowerPoint 或 LibreOffice。"
    };
}
