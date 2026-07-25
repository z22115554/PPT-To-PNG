using PptPngExporter.Core.Converters;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Parsing;
using PptPngExporter.Core.Services;
using Xunit;

namespace PptPngExporter.Tests;

/// <summary>
/// 假的轉換引擎，讓我們可以在沒有 PowerPoint / LibreOffice 的環境下
/// 驗證批次流程、引擎切換與錯誤隔離。
/// </summary>
internal sealed class FakeConverter : ISlideConverter
{
    private readonly Func<ConversionRequest, IReadOnlyList<string>> _behaviour;

    public FakeConverter(ConversionEngine engine, Func<ConversionRequest, IReadOnlyList<string>> behaviour, bool available = true)
    {
        Engine = engine;
        _behaviour = behaviour;
        Available = available;
    }

    public ConversionEngine Engine { get; }
    public string DisplayName => Engine.ToString();
    public bool Available { get; set; }
    public string? UnavailableReason => Available ? null : $"{DisplayName} 未安裝（測試用）。";
    public int CallCount { get; private set; }
    public List<string> SeenSources { get; } = new();

    public bool IsAvailable() => Available;

    public int ResetCount { get; private set; }
    public void ResetAvailability() => ResetCount++;

    public IReadOnlyList<string> Convert(ConversionRequest request, IProgress<SlideProgress>? progress, CancellationToken cancellationToken)
    {
        CallCount++;
        SeenSources.Add(request.SourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        return _behaviour(request);
    }

    /// <summary>成功路徑：真的把 PNG 佔位檔寫進輸出資料夾，讓防覆蓋邏輯可以一併驗證。</summary>
    public static FakeConverter Succeeding(ConversionEngine engine, int pageCount = 3) =>
        new(engine, request =>
        {
            LongPath.EnsureDirectory(request.OutputDirectory);
            var written = new List<string>();
            var pages = request.Pages.Resolve(pageCount);
            foreach (var page in pages)
            {
                var name = FileNameSanitizer.BuildImageName(request.FileNamePrefix, page, pages[^1]);
                var path = UniquePathResolver.ResolveFile(request.OutputDirectory, name, ".png");
                File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                written.Add(path);
            }
            return written;
        });

    public static FakeConverter Failing(ConversionEngine engine, string message = "測試用失敗") =>
        new(engine, _ => throw new ConversionException(message));
}

public class BatchExportServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _outputRoot;

    public BatchExportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        _outputRoot = Path.Combine(_root, "輸出");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outputRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string CreateSource(string fileName)
    {
        var path = Path.Combine(_root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "假的簡報內容");
        return path;
    }

    private ExportOptions Options(EnginePreference preference = EnginePreference.Auto, string? pages = null, string prefix = "投影片_")
        => new()
        {
            OutputRoot = _outputRoot,
            Pages = PageRangeParser.Parse(pages),
            ImageWidth = 1920,
            FileNamePrefix = prefix,
            Engine = preference
        };

    [Fact]
    public void 支援的副檔名判斷()
    {
        Assert.True(BatchExportService.IsSupported("a.ppt"));
        Assert.True(BatchExportService.IsSupported("a.PPTX"));
        Assert.True(BatchExportService.IsSupported("a.pps"));
        Assert.True(BatchExportService.IsSupported("a.ppsx"));
        Assert.False(BatchExportService.IsSupported("a.pdf"));
        Assert.False(BatchExportService.IsSupported("a"));
    }

    [Fact]
    public void 單一檔案失敗不會中斷整批工作()
    {
        var good1 = CreateSource("正常一.pptx");
        var bad = CreateSource("壞掉.pptx");
        var good2 = CreateSource("正常二.pptx");

        var converter = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            if (request.SourcePath == bad) throw new ConversionException("這個檔案損毀了");
            LongPath.EnsureDirectory(request.OutputDirectory);
            File.WriteAllText(Path.Combine(request.OutputDirectory, "01.png"), "x");
            return new[] { Path.Combine(request.OutputDirectory, "01.png") };
        });

        var service = new BatchExportService(new ISlideConverter[] { converter });
        var report = service.Run(new[] { good1, bad, good2 }, Options());

        Assert.Equal(3, report.Results.Count);
        Assert.Equal(ExportStatus.Success, report.Results[0].Status);
        Assert.Equal(ExportStatus.Failed, report.Results[1].Status);
        Assert.Equal(ExportStatus.Success, report.Results[2].Status);
        Assert.Equal(2, report.SuccessCount);
        Assert.Equal(1, report.FailedCount);
        Assert.Contains("損毀", report.Results[1].ErrorMessage);
    }

    [Fact]
    public void 未預期的例外也只影響單一檔案()
    {
        var a = CreateSource("a.pptx");
        var b = CreateSource("b.pptx");

        var converter = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            if (request.SourcePath == a) throw new InvalidOperationException("非預期崩潰");
            LongPath.EnsureDirectory(request.OutputDirectory);
            return Array.Empty<string>();
        });

        var report = new BatchExportService(new ISlideConverter[] { converter })
            .Run(new[] { a, b }, Options());

        Assert.Equal(ExportStatus.Failed, report.Results[0].Status);
        Assert.Contains("非預期崩潰", report.Results[0].ErrorMessage);
        Assert.Equal(ExportStatus.Success, report.Results[1].Status);
    }

    [Fact]
    public void PowerPoint失敗時自動改用LibreOffice()
    {
        var source = CreateSource("簡報.pptx");
        var powerPoint = FakeConverter.Failing(ConversionEngine.PowerPoint, "PowerPoint 忙碌中");
        var libreOffice = FakeConverter.Succeeding(ConversionEngine.LibreOffice, pageCount: 4);

        var report = new BatchExportService(new ISlideConverter[] { powerPoint, libreOffice })
            .Run(new[] { source }, Options());

        Assert.Equal(ExportStatus.Success, report.Results[0].Status);
        Assert.Equal(ConversionEngine.LibreOffice, report.Results[0].EngineUsed);
        Assert.Equal(4, report.Results[0].ImageCount);
        Assert.Equal(1, powerPoint.CallCount);
        Assert.Equal(1, libreOffice.CallCount);
    }

    [Fact]
    public void PowerPoint成功時不會呼叫LibreOffice()
    {
        var source = CreateSource("簡報.pptx");
        var powerPoint = FakeConverter.Succeeding(ConversionEngine.PowerPoint);
        var libreOffice = FakeConverter.Succeeding(ConversionEngine.LibreOffice);

        var report = new BatchExportService(new ISlideConverter[] { libreOffice, powerPoint })
            .Run(new[] { source }, Options());

        Assert.Equal(ConversionEngine.PowerPoint, report.Results[0].EngineUsed);
        Assert.Equal(0, libreOffice.CallCount);
    }

    [Fact]
    public void 沒有安裝PowerPoint時直接使用LibreOffice()
    {
        var source = CreateSource("簡報.pptx");
        var powerPoint = FakeConverter.Succeeding(ConversionEngine.PowerPoint);
        powerPoint.Available = false;
        var libreOffice = FakeConverter.Succeeding(ConversionEngine.LibreOffice);

        var report = new BatchExportService(new ISlideConverter[] { powerPoint, libreOffice })
            .Run(new[] { source }, Options());

        Assert.Equal(ConversionEngine.LibreOffice, report.Results[0].EngineUsed);
        Assert.Equal(0, powerPoint.CallCount);
    }

    [Fact]
    public void 兩種引擎都失敗時錯誤訊息會同時列出()
    {
        var source = CreateSource("簡報.pptx");
        var report = new BatchExportService(new ISlideConverter[]
            {
                FakeConverter.Failing(ConversionEngine.PowerPoint, "PowerPoint 開不起來"),
                FakeConverter.Failing(ConversionEngine.LibreOffice, "LibreOffice 讀不到")
            })
            .Run(new[] { source }, Options());

        Assert.Equal(ExportStatus.Failed, report.Results[0].Status);
        Assert.Contains("PowerPoint 開不起來", report.Results[0].ErrorMessage);
        Assert.Contains("LibreOffice 讀不到", report.Results[0].ErrorMessage);
    }

    [Fact]
    public void 沒有任何可用引擎時每個檔案都會有清楚說明()
    {
        var source = CreateSource("簡報.pptx");
        var report = new BatchExportService(Array.Empty<ISlideConverter>())
            .Run(new[] { source }, Options(EnginePreference.PowerPointOnly));

        Assert.Equal(ExportStatus.Failed, report.Results[0].Status);
        Assert.Contains("PowerPoint", report.Results[0].ErrorMessage);
    }

    [Fact]
    public void 只用LibreOffice時不會呼叫PowerPoint()
    {
        var source = CreateSource("簡報.pptx");
        var powerPoint = FakeConverter.Succeeding(ConversionEngine.PowerPoint);
        var libreOffice = FakeConverter.Succeeding(ConversionEngine.LibreOffice);

        new BatchExportService(new ISlideConverter[] { powerPoint, libreOffice })
            .Run(new[] { source }, Options(EnginePreference.LibreOfficeOnly));

        Assert.Equal(0, powerPoint.CallCount);
        Assert.Equal(1, libreOffice.CallCount);
    }

    [Fact]
    public void 失敗的檔案不會留下空資料夾()
    {
        var source = CreateSource("壞掉.pptx");

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Failing(ConversionEngine.PowerPoint) })
            .Run(new[] { source }, Options());

        Assert.Equal(ExportStatus.Failed, report.Results[0].Status);
        Assert.False(Directory.Exists(Path.Combine(_outputRoot, "壞掉")));
    }

    [Fact]
    public void 每份簡報輸出到自己的資料夾()
    {
        var a = CreateSource("第一份.pptx");
        var b = CreateSource("第二份.pptx");

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { a, b }, Options());

        Assert.Equal(Path.Combine(_outputRoot, "第一份"), report.Results[0].OutputDirectory);
        Assert.Equal(Path.Combine(_outputRoot, "第二份"), report.Results[1].OutputDirectory);
        Assert.Equal(3, Directory.GetFiles(report.Results[0].OutputDirectory!).Length);
    }

    [Fact]
    public void 重複執行不會覆蓋既有輸出()
    {
        var source = CreateSource("簡報.pptx");
        var service = new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) });

        var first = service.Run(new[] { source }, Options());
        var second = service.Run(new[] { source }, Options());

        Assert.Equal(Path.Combine(_outputRoot, "簡報"), first.Results[0].OutputDirectory);
        Assert.Equal(Path.Combine(_outputRoot, "簡報 (2)"), second.Results[0].OutputDirectory);
        Assert.Equal(3, Directory.GetFiles(first.Results[0].OutputDirectory!).Length);
        Assert.Equal(3, Directory.GetFiles(second.Results[0].OutputDirectory!).Length);
    }

    [Fact]
    public void 同名的不同來源檔案會各自分開輸出()
    {
        var a = CreateSource(Path.Combine("資料夾A", "年報.pptx"));
        var b = CreateSource(Path.Combine("資料夾B", "年報.pptx"));

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { a, b }, Options());

        Assert.NotEqual(report.Results[0].OutputDirectory, report.Results[1].OutputDirectory);
        Assert.Equal(2, report.SuccessCount);
    }

    [Fact]
    public void 檔案不存在時給出友善訊息()
    {
        var missing = Path.Combine(_root, "不存在.pptx");

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { missing }, Options());

        Assert.Equal(ExportStatus.Failed, report.Results[0].Status);
        Assert.Contains("找不到", report.Results[0].ErrorMessage);
    }

    [Fact]
    public void 取消後尚未處理的檔案標記為已取消()
    {
        var files = Enumerable.Range(1, 5).Select(i => CreateSource($"簡報{i}.pptx")).ToArray();
        using var cts = new CancellationTokenSource();

        var converter = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            LongPath.EnsureDirectory(request.OutputDirectory);
            if (request.SourcePath.EndsWith("簡報2.pptx", StringComparison.Ordinal)) cts.Cancel();
            File.WriteAllText(Path.Combine(request.OutputDirectory, "01.png"), "x");
            return new[] { Path.Combine(request.OutputDirectory, "01.png") };
        });

        var report = new BatchExportService(new ISlideConverter[] { converter }).Run(files, Options(), null, cts.Token);

        Assert.True(report.WasCancelled);
        Assert.Equal(ExportStatus.Success, report.Results[0].Status);
        Assert.Equal(ExportStatus.Success, report.Results[1].Status);
        Assert.All(report.Results.Skip(2), r => Assert.Equal(ExportStatus.Cancelled, r.Status));
        Assert.Equal(2, converter.CallCount);
    }

    [Fact]
    public void 轉檔中途取消會標記為已取消而非失敗()
    {
        var source = CreateSource("簡報.pptx");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var converter = new FakeConverter(ConversionEngine.PowerPoint, _ => Array.Empty<string>());
        var report = new BatchExportService(new ISlideConverter[] { converter }).Run(new[] { source }, Options(), null, cts.Token);

        Assert.True(report.WasCancelled);
        Assert.Equal(ExportStatus.Cancelled, report.Results[0].Status);
    }

    [Fact]
    public void 頁碼範圍會傳遞給轉換引擎()
    {
        var source = CreateSource("簡報.pptx");
        PageRangeSpec? seen = null;

        var converter = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            seen = request.Pages;
            LongPath.EnsureDirectory(request.OutputDirectory);
            return Array.Empty<string>();
        });

        new BatchExportService(new ISlideConverter[] { converter }).Run(new[] { source }, Options(pages: "2-3"));

        Assert.NotNull(seen);
        Assert.Equal(new[] { 2, 3 }, seen!.Resolve(10));
    }

    [Fact]
    public void 前綴中的非法字元在傳給引擎前就被清理()
    {
        var source = CreateSource("簡報.pptx");
        string? seenPrefix = null;

        var converter = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            seenPrefix = request.FileNamePrefix;
            LongPath.EnsureDirectory(request.OutputDirectory);
            return Array.Empty<string>();
        });

        new BatchExportService(new ISlideConverter[] { converter }).Run(new[] { source }, Options(prefix: "a/b:c"));

        Assert.NotNull(seenPrefix);
        Assert.DoesNotContain('/', seenPrefix!);
        Assert.DoesNotContain(':', seenPrefix!);
    }

    [Fact]
    public void 進度回報涵蓋每個檔案()
    {
        var files = new[] { CreateSource("a.pptx"), CreateSource("b.pptx") };
        var reports = new List<ProgressReport>();
        var progress = new Progress<ProgressReport>(reports.Add);

        // Progress<T> 走同步內容時仍是非同步排入；改用直接實作以便測試斷言
        var sync = new SynchronousProgress(reports.Add);

        new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(files, Options(), sync);

        Assert.NotEmpty(reports);
        Assert.Contains(reports, r => r.CurrentFileName == "a.pptx");
        Assert.Contains(reports, r => r.CurrentFileName == "b.pptx");
        Assert.Equal(2, reports[^1].FilesCompleted);
        Assert.Equal(100, reports[^1].OverallPercent);
        _ = progress;
    }

    [Fact]
    public void 輸出根目錄不存在時會自動建立()
    {
        var source = CreateSource("簡報.pptx");
        var nested = Path.Combine(_root, "新的", "深層", "輸出");

        var options = new ExportOptions
        {
            OutputRoot = nested,
            Pages = PageRangeSpec.All,
            ImageWidth = 1920,
            FileNamePrefix = string.Empty,
            Engine = EnginePreference.Auto
        };

        new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { source }, options);

        Assert.True(Directory.Exists(nested));
    }

    [Theory]
    [InlineData(10, ExportOptions.MinWidth)]
    [InlineData(1920, 1920)]
    [InlineData(999999, ExportOptions.MaxWidth)]
    public void 圖片寬度會被限制在安全範圍(int input, int expected)
        => Assert.Equal(expected, ExportOptions.ClampWidth(input));

    private sealed class SynchronousProgress : IProgress<ProgressReport>
    {
        private readonly Action<ProgressReport> _handler;
        public SynchronousProgress(Action<ProgressReport> handler) => _handler = handler;
        public void Report(ProgressReport value) => _handler(value);
    }
}
