using PptPngExporter.Core.Converters;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Parsing;
using PptPngExporter.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace PptPngExporter.Tests;

/// <summary>
/// 真的呼叫 LibreOffice 把 .pptx 轉成 PNG 的端到端測試。
///
/// 這些測試需要機器上有 LibreOffice；沒有的話會直接略過而不是失敗，
/// 這樣一般開發機與 CI 都能執行完整測試套件。
/// </summary>
public class LibreOfficeIntegrationTests : IDisposable
{
    private readonly string _workDir;
    private readonly ITestOutputHelper _output;
    private readonly string? _soffice;

    public LibreOfficeIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _soffice = LibreOfficeLocator.Find();
        _workDir = Path.Combine(Path.GetTempPath(), "PptPngExporterIT", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Assets", "測試簡報.pptx");

    private bool Skip(string reason)
    {
        _output.WriteLine("略過：" + reason);
        return true;
    }

    private bool ShouldSkip()
    {
        if (_soffice is null) return Skip("這台機器沒有安裝 LibreOffice。");
        if (!File.Exists(SamplePath)) return Skip("找不到測試用簡報 " + SamplePath);
        _output.WriteLine("使用 LibreOffice：" + _soffice);
        return false;
    }

    [Fact]
    public void 六頁簡報全部轉出且尺寸正確()
    {
        if (ShouldSkip()) return;

        var outputDir = Path.Combine(_workDir, "全部");
        var converter = new LibreOfficeConverter();

        var files = converter.Convert(new ConversionRequest
        {
            SourcePath = SamplePath,
            OutputDirectory = outputDir,
            Pages = PageRangeSpec.All,
            ImageWidth = 1920,
            FileNamePrefix = "投影片_",
            Numbering = FileNumbering.Sequential,
            NumberDigits = 3
        }, null, CancellationToken.None);

        Assert.Equal(6, files.Count);
        Assert.All(files, f => Assert.True(File.Exists(f), f + " 不存在"));

        // 預設補三位數
        Assert.Equal(
            new[] { "投影片_001.png", "投影片_002.png", "投影片_003.png", "投影片_004.png", "投影片_005.png", "投影片_006.png" },
            files.Select(Path.GetFileName).ToArray());

        var (width, height) = ReadPngSize(files[0]);
        _output.WriteLine($"輸出尺寸：{width}x{height}");

        Assert.Equal(1920, width);
        // 16:9 → 1080，允許算繪的 ±2 像素誤差
        Assert.InRange(height, 1078, 1082);
    }

    [Fact]
    public void 只轉指定的頁碼範圍()
    {
        if (ShouldSkip()) return;

        var outputDir = Path.Combine(_workDir, "範圍");

        var files = new LibreOfficeConverter().Convert(new ConversionRequest
        {
            SourcePath = SamplePath,
            OutputDirectory = outputDir,
            Pages = PageRangeParser.Parse("2-3,6"),
            ImageWidth = 1280,
            FileNamePrefix = string.Empty,
            // 明確指定沿用原始頁碼，檔名才會對應到來源頁碼
            Numbering = FileNumbering.OriginalPage,
            NumberDigits = 2
        }, null, CancellationToken.None);

        Assert.Equal(3, files.Count);
        Assert.Equal(new[] { "02.png", "03.png", "06.png" }, files.Select(Path.GetFileName).ToArray());
        Assert.Equal(1280, ReadPngSize(files[0]).Width);
    }

    [Fact]
    public void 自訂寬度會被套用()
    {
        if (ShouldSkip()) return;

        var files = new LibreOfficeConverter().Convert(new ConversionRequest
        {
            SourcePath = SamplePath,
            OutputDirectory = Path.Combine(_workDir, "4K"),
            Pages = PageRangeParser.Parse("1"),
            ImageWidth = 3840,
            FileNamePrefix = "頁_"
        }, null, CancellationToken.None);

        var (width, height) = ReadPngSize(files.Single());
        Assert.Equal(3840, width);
        Assert.InRange(height, 2158, 2162);
    }

    [Fact]
    public void 進度會逐頁回報()
    {
        if (ShouldSkip()) return;

        var reports = new List<SlideProgress>();
        var progress = new ImmediateProgress<SlideProgress>(reports.Add);

        new LibreOfficeConverter().Convert(new ConversionRequest
        {
            SourcePath = SamplePath,
            OutputDirectory = Path.Combine(_workDir, "進度"),
            Pages = PageRangeSpec.All,
            ImageWidth = 960,
            FileNamePrefix = "p"
        }, progress, CancellationToken.None);

        Assert.Equal(0, reports[0].Completed);
        Assert.Equal(6, reports[^1].Completed);
        Assert.All(reports, r => Assert.Equal(6, r.Total));
    }

    [Fact]
    public void 超出範圍的頁碼會給出說明實際頁數的錯誤()
    {
        if (ShouldSkip()) return;

        var ex = Assert.Throws<ConversionException>(() => new LibreOfficeConverter().Convert(new ConversionRequest
        {
            SourcePath = SamplePath,
            OutputDirectory = Path.Combine(_workDir, "超出"),
            Pages = PageRangeParser.Parse("50-60"),
            ImageWidth = 1920,
            FileNamePrefix = string.Empty
        }, null, CancellationToken.None));

        Assert.Contains("6", ex.Message);
    }

    [Fact]
    public void 損毀的檔案會失敗但不會擲出未預期的例外()
    {
        if (ShouldSkip()) return;

        // 真正毀損的 OOXML：保留 ZIP 檔頭但把內容截半，LibreOffice 無法開啟。
        // （純文字檔改副檔名成 .pptx 不算毀損 — LibreOffice 會把它當文字文件正常開啟。）
        var original = File.ReadAllBytes(SamplePath);
        var broken = Path.Combine(_workDir, "損毀.pptx");
        File.WriteAllBytes(broken, original.Take(original.Length / 2).ToArray());

        var ex = Assert.Throws<ConversionException>(() => new LibreOfficeConverter().Convert(new ConversionRequest
        {
            SourcePath = broken,
            OutputDirectory = Path.Combine(_workDir, "損毀輸出"),
            Pages = PageRangeSpec.All,
            ImageWidth = 1920,
            FileNamePrefix = string.Empty
        }, null, CancellationToken.None));

        _output.WriteLine("錯誤訊息：" + ex.Message);
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
        // 訊息必須是使用者看得懂的中文，不能是原始的技術堆疊
        Assert.Contains("LibreOffice", ex.Message);
    }

    [Fact]
    public void 損毀的檔案在批次中只影響自己()
    {
        if (ShouldSkip()) return;

        var original = File.ReadAllBytes(SamplePath);

        var good = Path.Combine(_workDir, "正常.pptx");
        File.Copy(SamplePath, good);

        var bad = Path.Combine(_workDir, "壞掉.pptx");
        File.WriteAllBytes(bad, original.Take(original.Length / 2).ToArray());

        var good2 = Path.Combine(_workDir, "正常二.pptx");
        File.Copy(SamplePath, good2);

        var outputRoot = Path.Combine(_workDir, "混合輸出");

        var report = new BatchExportService(new ISlideConverter[] { new LibreOfficeConverter() })
            .Run(new[] { good, bad, good2 }, new ExportOptions
            {
                OutputRoot = outputRoot,
                Pages = PageRangeParser.Parse("1"),
                ImageWidth = 800,
                FileNamePrefix = "投影片_",
                Engine = EnginePreference.LibreOfficeOnly
            });

        Assert.Equal(ExportStatus.Success, report.Results[0].Status);
        Assert.Equal(ExportStatus.Failed, report.Results[1].Status);
        Assert.Equal(ExportStatus.Success, report.Results[2].Status);
        Assert.Equal(2, report.SuccessCount);

        // 失敗的檔案不留下空資料夾
        Assert.False(Directory.Exists(Path.Combine(outputRoot, "壞掉")));
    }

    [Fact]
    public void 中文與含空白的路徑可以正常轉換()
    {
        if (ShouldSkip()) return;

        var sourceDir = Path.Combine(_workDir, "我的 簡報 資料夾");
        Directory.CreateDirectory(sourceDir);
        var source = Path.Combine(sourceDir, "２０２５年 度 報告書.pptx");
        File.Copy(SamplePath, source);

        var outputDir = Path.Combine(_workDir, "輸出 資料夾", "２０２５年 度 報告書");

        var files = new LibreOfficeConverter().Convert(new ConversionRequest
        {
            SourcePath = source,
            OutputDirectory = outputDir,
            Pages = PageRangeParser.Parse("1-2"),
            ImageWidth = 1920,
            FileNamePrefix = "投影片_"
        }, null, CancellationToken.None);

        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.True(File.Exists(f)));
    }

    [Fact]
    public void 轉檔後不會留下soffice背景程序()
    {
        if (ShouldSkip()) return;

        var before = System.Diagnostics.Process.GetProcessesByName("soffice").Select(p => p.Id).ToHashSet();
        var beforeBin = System.Diagnostics.Process.GetProcessesByName("soffice.bin").Select(p => p.Id).ToHashSet();

        new LibreOfficeConverter().Convert(new ConversionRequest
        {
            SourcePath = SamplePath,
            OutputDirectory = Path.Combine(_workDir, "程序檢查"),
            Pages = PageRangeParser.Parse("1"),
            ImageWidth = 800,
            FileNamePrefix = string.Empty
        }, null, CancellationToken.None);

        Thread.Sleep(1500);

        var leftover = System.Diagnostics.Process.GetProcessesByName("soffice").Select(p => p.Id).Except(before)
            .Concat(System.Diagnostics.Process.GetProcessesByName("soffice.bin").Select(p => p.Id).Except(beforeBin))
            .ToArray();

        _output.WriteLine("殘留程序：" + (leftover.Length == 0 ? "無" : string.Join(",", leftover)));
        Assert.Empty(leftover);
    }

    [Fact]
    public void 批次流程可以真的把兩份簡報轉完()
    {
        if (ShouldSkip()) return;

        var a = Path.Combine(_workDir, "第一份.pptx");
        var b = Path.Combine(_workDir, "第二份.pptx");
        File.Copy(SamplePath, a);
        File.Copy(SamplePath, b);

        var outputRoot = Path.Combine(_workDir, "批次輸出");

        var report = new BatchExportService(new ISlideConverter[] { new LibreOfficeConverter() })
            .Run(new[] { a, b }, new ExportOptions
            {
                OutputRoot = outputRoot,
                Pages = PageRangeParser.Parse("1-2"),
                ImageWidth = 1280,
                FileNamePrefix = "投影片_",
                Engine = EnginePreference.LibreOfficeOnly
            });

        Assert.Equal(2, report.SuccessCount);
        Assert.Equal(0, report.FailedCount);
        Assert.Equal(4, report.TotalImages);
        Assert.Equal(Path.Combine(outputRoot, "第一份"), report.Results[0].OutputDirectory);
        Assert.Equal(Path.Combine(outputRoot, "第二份"), report.Results[1].OutputDirectory);
        Assert.Equal(2, Directory.GetFiles(report.Results[0].OutputDirectory!).Length);
    }

    [Fact]
    public void 重複執行批次不會覆蓋前一次的圖片()
    {
        if (ShouldSkip()) return;

        var source = Path.Combine(_workDir, "重複.pptx");
        File.Copy(SamplePath, source);
        var outputRoot = Path.Combine(_workDir, "重複輸出");

        var options = new ExportOptions
        {
            OutputRoot = outputRoot,
            Pages = PageRangeParser.Parse("1"),
            ImageWidth = 800,
            FileNamePrefix = "投影片_",
            Engine = EnginePreference.LibreOfficeOnly
        };

        var service = new BatchExportService(new ISlideConverter[] { new LibreOfficeConverter() });
        var first = service.Run(new[] { source }, options);
        var firstFile = Directory.GetFiles(first.Results[0].OutputDirectory!).Single();
        var firstBytes = File.ReadAllBytes(firstFile);

        var second = service.Run(new[] { source }, options);

        Assert.Equal(Path.Combine(outputRoot, "重複"), first.Results[0].OutputDirectory);
        Assert.Equal(Path.Combine(outputRoot, "重複 (2)"), second.Results[0].OutputDirectory);
        Assert.Equal(firstBytes, File.ReadAllBytes(firstFile));
    }

    /// <summary>直接讀 PNG 檔頭取得寬高，不需額外的影像函式庫。</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        var read = stream.Read(header);

        Assert.Equal(24, read);
        // PNG 簽章
        Assert.Equal(0x89, header[0]);
        Assert.Equal((byte)'P', header[1]);
        Assert.Equal((byte)'N', header[2]);
        Assert.Equal((byte)'G', header[3]);

        var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
        var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
        return (width, height);
    }

    private sealed class ImmediateProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public ImmediateProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
