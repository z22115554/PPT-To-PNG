using PptPngExporter.Core.Converters;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Parsing;
using PptPngExporter.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace PptPngExporter.Tests;

public class ImageNameBuilderTests
{
    [Fact]
    public void 挑選第1和5和7張時連續編號()
    {
        var pages = new[] { 1, 5, 7 };
        var naming = new ImageNameBuilder("投影片_", FileNumbering.Sequential, 3, pages);

        var names = pages.Select((page, i) => naming.Build(i + 1, page)).ToArray();

        Assert.Equal(new[] { "投影片_001", "投影片_002", "投影片_003" }, names);
    }

    [Fact]
    public void 挑選第1和5和7張時也可以保留原始頁碼()
    {
        var pages = new[] { 1, 5, 7 };
        var naming = new ImageNameBuilder("投影片_", FileNumbering.OriginalPage, 3, pages);

        var names = pages.Select((page, i) => naming.Build(i + 1, page)).ToArray();

        Assert.Equal(new[] { "投影片_001", "投影片_005", "投影片_007" }, names);
    }

    [Fact]
    public void 沒有前綴時只有數字()
    {
        var naming = new ImageNameBuilder(string.Empty, FileNumbering.Sequential, 3, new[] { 2, 4 });

        Assert.Equal("001", naming.Build(1, 2));
        Assert.Equal("002", naming.Build(2, 4));
    }

    [Theory]
    [InlineData(2, "投影片_01")]
    [InlineData(3, "投影片_001")]
    [InlineData(4, "投影片_0001")]
    [InlineData(5, "投影片_00001")]
    public void 位數可以指定(int digits, string expected)
    {
        var naming = new ImageNameBuilder("投影片_", FileNumbering.Sequential, digits, new[] { 1, 2, 3 });

        Assert.Equal(expected, naming.Build(1, 1));
        Assert.Equal(digits, naming.Digits);
    }

    [Fact]
    public void 位數設為自動時依連續編號的總張數決定()
    {
        var pages = Enumerable.Range(1, 150).ToArray();
        var naming = new ImageNameBuilder("p", FileNumbering.Sequential, 0, pages);

        Assert.Equal(3, naming.Digits);
        Assert.Equal("p001", naming.Build(1, 1));
        Assert.Equal("p150", naming.Build(150, 150));
    }

    [Fact]
    public void 位數設為自動且使用原始頁碼時依最大頁碼決定()
    {
        // 只挑兩張，但頁碼到 120，位數必須容得下 120
        var pages = new[] { 3, 120 };
        var naming = new ImageNameBuilder("p", FileNumbering.OriginalPage, 0, pages);

        Assert.Equal(3, naming.Digits);
        Assert.Equal("p003", naming.Build(1, 3));
        Assert.Equal("p120", naming.Build(2, 120));
    }

    [Fact]
    public void 自動位數至少兩位()
    {
        var naming = new ImageNameBuilder(string.Empty, FileNumbering.Sequential, 0, new[] { 1, 2 });

        Assert.Equal(2, naming.Digits);
        Assert.Equal("01", naming.Build(1, 1));
    }

    [Fact]
    public void 指定位數不足時不會截斷數字()
    {
        var naming = new ImageNameBuilder("p", FileNumbering.Sequential, 2, Enumerable.Range(1, 1500).ToArray());

        Assert.Equal("p1500", naming.Build(1500, 1500));
    }

    [Fact]
    public void 沒有任何頁面時不會擲出例外()
    {
        var naming = new ImageNameBuilder("p", FileNumbering.Sequential, 0, Array.Empty<int>());

        Assert.Equal("p01", naming.Build(1, 1));
    }
}

public class PageRangeFromPagesTests
{
    [Fact]
    public void 由勾選的頁碼建立()
    {
        var spec = PageRangeSpec.FromPages(new[] { 1, 5, 7 });

        Assert.False(spec.IsAll);
        Assert.Equal(new[] { 1, 5, 7 }, spec.Resolve(10));
    }

    [Fact]
    public void 會排序並去除重複()
    {
        var spec = PageRangeSpec.FromPages(new[] { 7, 1, 5, 1, 7 });

        Assert.Equal(new[] { 1, 5, 7 }, spec.Resolve(10));
    }

    [Fact]
    public void 連續的頁碼會合併成區間()
    {
        var spec = PageRangeSpec.FromPages(new[] { 1, 2, 3, 8 });

        Assert.Equal(new[] { 1, 2, 3, 8 }, spec.Resolve(10));
        Assert.Equal(2, spec.Intervals.Count);
    }

    [Fact]
    public void 忽略無效頁碼()
    {
        var spec = PageRangeSpec.FromPages(new[] { 0, -3, 2 });

        Assert.Equal(new[] { 2 }, spec.Resolve(10));
    }

    [Fact]
    public void 空選取展開後為空()
        => Assert.Empty(PageRangeSpec.FromPages(Array.Empty<int>()).Resolve(10));

    [Fact]
    public void 超出總頁數的部分會被裁切()
        => Assert.Equal(new[] { 2, 4 }, PageRangeSpec.FromPages(new[] { 2, 4, 99 }).Resolve(5));
}

public class PerJobPageSelectionTests : IDisposable
{
    private readonly string _root;
    private readonly string _outputRoot;

    public PerJobPageSelectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        _outputRoot = Path.Combine(_root, "輸出");
        Directory.CreateDirectory(_outputRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string CreateSource(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "假的簡報內容");
        return path;
    }

    [Fact]
    public void 每份簡報可以有各自的頁面選擇()
    {
        var a = CreateSource("甲.pptx");
        var b = CreateSource("乙.pptx");

        var seen = new Dictionary<string, IReadOnlyList<int>>();
        var converter = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            seen[Path.GetFileName(request.SourcePath)] = request.Pages.Resolve(10);
            LongPath.EnsureDirectory(request.OutputDirectory);
            return Array.Empty<string>();
        });

        new BatchExportService(new ISlideConverter[] { converter }).Run(
            new[]
            {
                new ExportJob { SourcePath = a, Pages = PageRangeSpec.FromPages(new[] { 1, 5, 7 }) },
                new ExportJob { SourcePath = b, Pages = PageRangeSpec.FromPages(new[] { 2, 3 }) }
            },
            new ExportOptions { OutputRoot = _outputRoot, Pages = PageRangeSpec.All });

        Assert.Equal(new[] { 1, 5, 7 }, seen["甲.pptx"]);
        Assert.Equal(new[] { 2, 3 }, seen["乙.pptx"]);
    }

    [Fact]
    public void 沒有單獨選擇時沿用整批設定()
    {
        var a = CreateSource("甲.pptx");
        IReadOnlyList<int>? seen = null;

        var converter = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            seen = request.Pages.Resolve(10);
            LongPath.EnsureDirectory(request.OutputDirectory);
            return Array.Empty<string>();
        });

        new BatchExportService(new ISlideConverter[] { converter }).Run(
            new[] { ExportJob.For(a) },
            new ExportOptions { OutputRoot = _outputRoot, Pages = PageRangeParser.Parse("4-6") });

        Assert.Equal(new[] { 4, 5, 6 }, seen);
    }

    [Fact]
    public void 編號設定會傳遞給轉換引擎()
    {
        var a = CreateSource("甲.pptx");
        FileNumbering? numbering = null;
        var digits = 0;

        var converter = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            numbering = request.Numbering;
            digits = request.NumberDigits;
            LongPath.EnsureDirectory(request.OutputDirectory);
            return Array.Empty<string>();
        });

        new BatchExportService(new ISlideConverter[] { converter }).Run(
            new[] { ExportJob.For(a) },
            new ExportOptions
            {
                OutputRoot = _outputRoot,
                Pages = PageRangeSpec.All,
                Numbering = FileNumbering.OriginalPage,
                NumberDigits = 4
            });

        Assert.Equal(FileNumbering.OriginalPage, numbering);
        Assert.Equal(4, digits);
    }
}

/// <summary>
/// 直接驗證使用者提出的情境：挑第 1、5、7 張，輸出必須是 001/002/003，
/// 而且內容真的是原簡報的第 1、5、7 張。
/// </summary>
public class PickedPagesIntegrationTests : IDisposable
{
    private readonly string _workDir;
    private readonly ITestOutputHelper _output;
    private readonly bool _hasLibreOffice;

    public PickedPagesIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _hasLibreOffice = LibreOfficeLocator.Find() is not null;
        _workDir = Path.Combine(Path.GetTempPath(), "PptPngExporterIT", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static string DeckPath => Path.Combine(AppContext.BaseDirectory, "Assets", "測試簡報-10頁.pptx");

    private bool ShouldSkip()
    {
        if (!_hasLibreOffice) { _output.WriteLine("略過：沒有安裝 LibreOffice。"); return true; }
        if (!File.Exists(DeckPath)) { _output.WriteLine("略過：找不到 " + DeckPath); return true; }
        return false;
    }

    private static IReadOnlyList<string> Export(string outputDir, int[] pages, FileNumbering numbering, int digits)
        => new LibreOfficeConverter().Convert(new ConversionRequest
        {
            SourcePath = DeckPath,
            OutputDirectory = outputDir,
            Pages = PageRangeSpec.FromPages(pages),
            ImageWidth = 640,
            FileNamePrefix = "投影片_",
            Numbering = numbering,
            NumberDigits = digits
        }, null, CancellationToken.None);

    [Fact]
    public void 挑選第1和5和7張輸出為001和002和003()
    {
        if (ShouldSkip()) return;

        var files = Export(Path.Combine(_workDir, "連續編號"), new[] { 1, 5, 7 }, FileNumbering.Sequential, 3);

        Assert.Equal(
            new[] { "投影片_001.png", "投影片_002.png", "投影片_003.png" },
            files.Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void 連續編號的圖片內容確實是第1和5和7張()
    {
        if (ShouldSkip()) return;

        var picked = Export(Path.Combine(_workDir, "挑選"), new[] { 1, 5, 7 }, FileNumbering.Sequential, 3);

        // 分別單獨輸出第 1、5、7 張作為對照組，逐一比對位元組
        var expected = new[] { 1, 5, 7 }
            .Select(p => Export(Path.Combine(_workDir, "對照" + p), new[] { p }, FileNumbering.Sequential, 3).Single())
            .ToArray();

        for (var i = 0; i < 3; i++)
        {
            _output.WriteLine($"{Path.GetFileName(picked[i])} ↔ 原簡報第 {new[] { 1, 5, 7 }[i]} 張");
            Assert.Equal(File.ReadAllBytes(expected[i]), File.ReadAllBytes(picked[i]));
        }
    }

    [Fact]
    public void 改用原始頁碼會得到001和005和007()
    {
        if (ShouldSkip()) return;

        var files = Export(Path.Combine(_workDir, "原始頁碼"), new[] { 1, 5, 7 }, FileNumbering.OriginalPage, 3);

        Assert.Equal(
            new[] { "投影片_001.png", "投影片_005.png", "投影片_007.png" },
            files.Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void 縮圖服務會產生整份簡報的預覽並可重複使用快取()
    {
        if (ShouldSkip()) return;

        var service = new SlidePreviewService(new ISlideConverter[] { new LibreOfficeConverter() });

        var first = service.GetPreview(DeckPath, EnginePreference.LibreOfficeOnly, 240, CancellationToken.None);

        Assert.Equal(10, first.SlideCount);
        Assert.All(first.ThumbnailPaths, p => Assert.True(File.Exists(p)));

        // 第二次應直接命中快取，回傳同一批檔案
        var second = service.GetPreview(DeckPath, EnginePreference.LibreOfficeOnly, 240, CancellationToken.None);
        Assert.Equal(first.ThumbnailPaths, second.ThumbnailPaths);
    }

    [Fact]
    public void 縮圖依頁序排列()
    {
        if (ShouldSkip()) return;

        var preview = new SlidePreviewService(new ISlideConverter[] { new LibreOfficeConverter() })
            .GetPreview(DeckPath, EnginePreference.LibreOfficeOnly, 200, CancellationToken.None);

        var names = preview.ThumbnailPaths.Select(Path.GetFileNameWithoutExtension).ToArray();

        Assert.Equal(Enumerable.Range(1, 10).Select(i => i.ToString("D4")).ToArray(), names);
    }
}
