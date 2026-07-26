using PptPngExporter.Core.Converters;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Parsing;
using PptPngExporter.Core.Services;
using Xunit;

namespace PptPngExporter.Tests;

/// <summary>
/// 針對實際踩到的問題所補上的迴歸測試，以及中文／空白／長路徑的實地驗證。
/// </summary>
public class OutputIntegrityTests : IDisposable
{
    private readonly string _root;

    public OutputIntegrityTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private ExportOptions Options(string outputRoot, string? pages = null) => new()
    {
        OutputRoot = outputRoot,
        Pages = PageRangeParser.Parse(pages),
        ImageWidth = 1920,
        FileNamePrefix = "投影片_",
        Engine = EnginePreference.Auto
    };

    private string CreateSource(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "假的簡報內容");
        return path;
    }

    [Fact]
    public void 後備引擎成功時輸出資料夾必須被記錄下來()
    {
        // 迴歸測試：先前第一個引擎失敗後會把 OutputDirectory 清成 null，
        // 導致後備引擎成功後介面上找不到「開啟資料夾」。
        var outputRoot = Path.Combine(_root, "輸出");
        var source = CreateSource("簡報.pptx");

        var report = new BatchExportService(new ISlideConverter[]
            {
                FakeConverter.Failing(ConversionEngine.PowerPoint),
                FakeConverter.Succeeding(ConversionEngine.LibreOffice, pageCount: 2)
            })
            .Run(new[] { source }, Options(outputRoot));

        Assert.Equal(ExportStatus.Success, report.Results[0].Status);
        Assert.False(string.IsNullOrEmpty(report.Results[0].OutputDirectory));
        Assert.True(Directory.Exists(report.Results[0].OutputDirectory!));
        Assert.Equal(2, Directory.GetFiles(report.Results[0].OutputDirectory!).Length);
    }

    [Fact]
    public void 前一個引擎的半成品不會混進後備引擎的輸出()
    {
        // 迴歸測試：PowerPoint 寫到一半才失敗時，殘留的圖片必須先清掉，
        // 否則 LibreOffice 的輸出會與半成品混在同一個資料夾。
        var outputRoot = Path.Combine(_root, "輸出");
        var source = CreateSource("簡報.pptx");

        var halfWritten = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            LongPath.EnsureDirectory(request.OutputDirectory);
            File.WriteAllText(Path.Combine(request.OutputDirectory, "投影片_01.png"), "半成品");
            File.WriteAllText(Path.Combine(request.OutputDirectory, "投影片_02.png"), "半成品");
            throw new ConversionException("寫到一半失敗了");
        });

        var report = new BatchExportService(new ISlideConverter[]
            {
                halfWritten,
                FakeConverter.Succeeding(ConversionEngine.LibreOffice, pageCount: 3)
            })
            .Run(new[] { source }, Options(outputRoot));

        var dir = report.Results[0].OutputDirectory!;
        var files = Directory.GetFiles(dir);

        Assert.Equal(ConversionEngine.LibreOffice, report.Results[0].EngineUsed);
        Assert.Equal(3, files.Length);
        Assert.All(files, f => Assert.NotEqual("半成品", File.ReadAllText(f)));
    }

    [Fact]
    public void 全部引擎失敗時半成品也會被清掉()
    {
        var outputRoot = Path.Combine(_root, "輸出");
        var source = CreateSource("簡報.pptx");

        var halfWritten = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            LongPath.EnsureDirectory(request.OutputDirectory);
            File.WriteAllText(Path.Combine(request.OutputDirectory, "投影片_01.png"), "半成品");
            throw new ConversionException("失敗");
        });

        var report = new BatchExportService(new ISlideConverter[] { halfWritten })
            .Run(new[] { source }, Options(outputRoot));

        Assert.Equal(ExportStatus.Failed, report.Results[0].Status);
        Assert.Null(report.Results[0].OutputDirectory);
        Assert.False(Directory.Exists(Path.Combine(outputRoot, "簡報")));
    }

    [Theory]
    [InlineData("我的簡報.pptx")]
    [InlineData("2025 年度 檢討 報告.pptx")]
    [InlineData("スライド 資料.pptx")]
    [InlineData("報告（最終版）.pptx")]
    public void 中文與含空白的檔名可以正常輸出(string fileName)
    {
        var outputRoot = Path.Combine(_root, "我的 輸出 資料夾");
        var source = CreateSource(Path.Combine("來源 資料夾", fileName));

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { source }, Options(outputRoot));

        var expected = Path.Combine(outputRoot, Path.GetFileNameWithoutExtension(fileName));

        Assert.Equal(ExportStatus.Success, report.Results[0].Status);
        Assert.Equal(expected, report.Results[0].OutputDirectory);
        Assert.Equal(3, Directory.GetFiles(expected).Length);
    }

    [Fact]
    public void 極長的檔名會被截短但仍能成功輸出()
    {
        // 長度刻意超過 FileNameSanitizer 的上限以觸發截斷；
        // 同時控制位元組數，讓這個測試在 Linux（檔名上限 255 位元組）也能執行。
        var longName = new string('報', 30) + new string('A', 120) + ".pptx";
        var source = CreateSource(longName);
        var outputRoot = Path.Combine(_root, "輸出");

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { source }, Options(outputRoot));

        Assert.Equal(ExportStatus.Success, report.Results[0].Status);
        var folderName = Path.GetFileName(report.Results[0].OutputDirectory!);
        Assert.True(folderName.Length <= FileNameSanitizer.DefaultMaxLength);
        Assert.Equal(3, Directory.GetFiles(report.Results[0].OutputDirectory!).Length);
    }

    [Fact]
    public void 深層巢狀的輸出路徑仍可建立()
    {
        // 逐層堆疊出接近傳統 260 字元上限的路徑
        var deep = _root;
        for (var i = 0; i < 6; i++) deep = Path.Combine(deep, new string('層', 30));

        var source = CreateSource("簡報.pptx");

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { source }, Options(deep));

        Assert.Equal(ExportStatus.Success, report.Results[0].Status);
        Assert.True(report.Results[0].OutputDirectory!.Length > 200);
    }

    [Fact]
    public void 長路徑輔助方法只在需要時加上前綴()
    {
        Assert.False(LongPath.IsLong("C:/短/路徑"));
        Assert.True(LongPath.IsLong("C:/" + new string('a', 300)));

        // 非 Windows 平台原樣回傳，Windows 平台才加上 \\?\
        var extended = LongPath.Extended(Path.Combine(_root, "x"));
        Assert.Contains("x", extended);
    }
}


/// <summary>
/// PowerPoint 是單一執行個體的 COM 伺服器：使用者已開著它時，我們拿到的是使用者那一個。
/// 這組測試把「什麼可以動、什麼不能動」的判斷固定下來。
/// </summary>
public class PowerPointSessionPolicyTests
{
    [Fact]
    public void 自己啟動的執行個體可以結束()
    {
        var policy = new PowerPointSessionPolicy(powerPointWasAlreadyRunning: false);

        Assert.False(policy.AttachedToExistingInstance);
        Assert.True(policy.MayQuitApplication);
        Assert.True(policy.MayKillLeftoverProcesses);
        Assert.False(policy.MustRestoreApplicationSettings);
    }

    [Fact]
    public void 使用者已開啟時絕對不可以結束PowerPoint()
    {
        var policy = new PowerPointSessionPolicy(powerPointWasAlreadyRunning: true);

        Assert.True(policy.AttachedToExistingInstance);
        Assert.False(policy.MayQuitApplication);
        Assert.False(policy.MayKillLeftoverProcesses);
        Assert.True(policy.MustRestoreApplicationSettings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 只關閉我們自己開啟的簡報(bool attached)
    {
        var policy = new PowerPointSessionPolicy(attached);

        Assert.True(policy.MayClosePresentation(weOpenedIt: true));
        Assert.False(policy.MayClosePresentation(weOpenedIt: false));
    }

    [Fact]
    public void 說明文字會反映實際行為()
    {
        Assert.Contains("不會關閉", new PowerPointSessionPolicy(true).Describe());
        Assert.Contains("一併關閉", new PowerPointSessionPolicy(false).Describe());
    }
}

/// <summary>轉換引擎的偵測快取必須可以清除，否則使用者中途安裝軟體後要重開程式才生效。</summary>
public class EngineDetectionResetTests
{
    [Fact]
    public void LibreOffice偵測快取可以重設()
    {
        var converter = new LibreOfficeConverter();

        var first = converter.IsAvailable();
        converter.ResetAvailability();
        var second = converter.IsAvailable();

        Assert.Equal(first, second);
        Assert.Null(Record.Exception(() => converter.ResetAvailability()));
    }

    [Fact]
    public void 重設之後不可用原因會一併清除()
    {
        var converter = new LibreOfficeConverter();
        converter.IsAvailable();

        converter.ResetAvailability();

        // 尚未重新偵測前不應殘留上一次的訊息
        Assert.Null(converter.UnavailableReason);
    }
}
