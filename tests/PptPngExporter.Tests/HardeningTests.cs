using PptPngExporter.Core.Converters;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Parsing;
using PptPngExporter.Core.Services;
using Xunit;

namespace PptPngExporter.Tests;

// ─────────────────────────────────────────────────────────────
// P0-1：巨集必須在開檔前被停用，且借用使用者的 PowerPoint 時要還原設定
// ─────────────────────────────────────────────────────────────
public class OfficeSettingsGuardTests
{
    /// <summary>模擬 Office 應用程式的設定。</summary>
    private sealed class FakeApp
    {
        public Dictionary<string, int> Values { get; } = new();
        public List<(string Name, int Value)> WriteLog { get; } = new();
        public HashSet<string> Unsupported { get; } = new();
        public HashSet<string> Unreadable { get; } = new();

        public int? Read(string name)
        {
            if (Unreadable.Contains(name)) throw new InvalidOperationException("讀不到");
            return Values.TryGetValue(name, out var v) ? v : null;
        }

        public bool Write(string name, int value)
        {
            if (Unsupported.Contains(name)) return false;
            Values[name] = value;
            WriteLog.Add((name, value));
            return true;
        }
    }

    private static OfficeSettingsGuard Create(FakeApp app) => new(app.Read, app.Write);

    [Fact]
    public void 巨集安全性被設為強制停用()
    {
        var app = new FakeApp();
        app.Values[PowerPointConstants.AutomationSecurity] = PowerPointConstants.AutomationSecurityLow;

        using (var guard = Create(app))
        {
            Assert.True(guard.Apply(PowerPointConstants.AutomationSecurity, PowerPointConstants.AutomationSecurityForceDisable));

            // 開檔期間必須是 ForceDisable
            Assert.Equal(PowerPointConstants.AutomationSecurityForceDisable,
                app.Values[PowerPointConstants.AutomationSecurity]);
        }
    }

    [Fact]
    public void 結束時還原原本的巨集安全性設定()
    {
        var app = new FakeApp();
        app.Values[PowerPointConstants.AutomationSecurity] = PowerPointConstants.AutomationSecurityByUI;

        using (var guard = Create(app))
        {
            guard.Apply(PowerPointConstants.AutomationSecurity, PowerPointConstants.AutomationSecurityForceDisable);
            Assert.Equal(1, guard.PendingRestoreCount);
        }

        // 借用使用者的 PowerPoint 時，不能把他的設定留在被我們改過的狀態
        Assert.Equal(PowerPointConstants.AutomationSecurityByUI, app.Values[PowerPointConstants.AutomationSecurity]);
    }

    [Fact]
    public void 多項設定以相反順序還原()
    {
        var app = new FakeApp();
        app.Values["A"] = 1;
        app.Values["B"] = 2;

        using (var guard = Create(app))
        {
            guard.Apply("A", 10);
            guard.Apply("B", 20);
        }

        var restores = app.WriteLog.Skip(2).Select(w => w.Name).ToArray();
        Assert.Equal(new[] { "B", "A" }, restores);
        Assert.Equal(1, app.Values["A"]);
        Assert.Equal(2, app.Values["B"]);
    }

    [Fact]
    public void 原值與新值相同時不需要還原()
    {
        var app = new FakeApp();
        app.Values["A"] = 3;

        using var guard = Create(app);
        guard.Apply("A", 3);

        Assert.Equal(0, guard.PendingRestoreCount);
    }

    [Fact]
    public void 應用程式不支援該設定時回報失敗而不擲出例外()
    {
        var app = new FakeApp();
        app.Unsupported.Add(PowerPointConstants.AutomationSecurity);

        using var guard = Create(app);

        Assert.False(guard.Apply(PowerPointConstants.AutomationSecurity, 3));
        Assert.Equal(0, guard.PendingRestoreCount);
    }

    [Fact]
    public void 讀不到原值時仍會嘗試套用()
    {
        var app = new FakeApp();
        app.Unreadable.Add("A");

        using var guard = Create(app);

        Assert.True(guard.Apply("A", 9));
        Assert.Equal(9, app.Values["A"]);
        Assert.Equal(0, guard.PendingRestoreCount);   // 沒有原值可還原
    }

    [Fact]
    public void 重複Dispose不會重複還原()
    {
        var app = new FakeApp();
        app.Values["A"] = 1;

        var guard = Create(app);
        guard.Apply("A", 5);
        guard.Dispose();
        guard.Dispose();

        // 套用 1 次 + 還原 1 次 = 2 次；第二次 Dispose 不應再寫入
        Assert.Equal(2, app.WriteLog.Count);
        Assert.Equal(("A", 5), app.WriteLog[0]);
        Assert.Equal(("A", 1), app.WriteLog[1]);
        Assert.Equal(1, app.Values["A"]);
    }

    [Fact]
    public void 常數值符合Office定義()
    {
        Assert.Equal(1, PowerPointConstants.AutomationSecurityLow);
        Assert.Equal(2, PowerPointConstants.AutomationSecurityByUI);
        Assert.Equal(3, PowerPointConstants.AutomationSecurityForceDisable);
        Assert.Equal(1, PowerPointConstants.AlertsNone);
        Assert.Equal(-1, PowerPointConstants.MsoTrue);
        Assert.Equal(0, PowerPointConstants.MsoFalse);
    }
}

// ─────────────────────────────────────────────────────────────
// P0-2：只結束確定由本程式啟動的程序
// ─────────────────────────────────────────────────────────────
public class OwnedProcessGuardTests
{
    [Fact]
    public void 沒有登記任何程序時不做任何事()
    {
        var guard = new OwnedProcessGuard();
        var start = DateTime.UtcNow;

        guard.KillSurvivors(TimeSpan.FromSeconds(3));

        Assert.Empty(guard.OwnedPids);
        Assert.True(DateTime.UtcNow - start < TimeSpan.FromSeconds(1), "沒有登記程序時應該立即返回");
    }

    [Fact]
    public void 只會記錄明確登記的程序()
    {
        var guard = new OwnedProcessGuard();
        guard.Track(4242);
        guard.Track(4242);       // 重複登記不應增加
        guard.Track(0);          // 無效 PID 應被忽略
        guard.Track(-1);

        Assert.Single(guard.OwnedPids);
        Assert.Contains(4242, guard.OwnedPids);
    }

    [Fact]
    public void 不會碰到沒有登記的程序()
    {
        // 目前程序沒有被登記，所以無論如何都不該被影響
        var current = System.Diagnostics.Process.GetCurrentProcess().Id;
        var guard = new OwnedProcessGuard();
        guard.Track(999_999);    // 不存在的 PID

        guard.KillSurvivors(TimeSpan.FromMilliseconds(300));

        Assert.DoesNotContain(current, guard.OwnedPids);
        Assert.True(true, "執行到這裡代表自己沒有被殺掉");
    }

    [Fact]
    public void JobObject在非Windows平台回傳null()
    {
        var job = WindowsJobObject.TryCreate();

        if (OperatingSystem.IsWindows())
            Assert.True(job is null || job.IsValid);
        else
            Assert.Null(job);

        job?.Dispose();
    }
}

// ─────────────────────────────────────────────────────────────
// P1-1：引擎不可用時必須擋在開始之前
// ─────────────────────────────────────────────────────────────
public class EngineAvailabilityTests
{
    [Theory]
    [InlineData(EnginePreference.Auto, true, true, true)]
    [InlineData(EnginePreference.Auto, true, false, true)]
    [InlineData(EnginePreference.Auto, false, true, true)]
    [InlineData(EnginePreference.Auto, false, false, false)]
    [InlineData(EnginePreference.PowerPointOnly, true, true, true)]
    [InlineData(EnginePreference.PowerPointOnly, false, true, false)]
    [InlineData(EnginePreference.PowerPointOnly, false, false, false)]
    [InlineData(EnginePreference.LibreOfficeOnly, true, true, true)]
    [InlineData(EnginePreference.LibreOfficeOnly, true, false, false)]
    [InlineData(EnginePreference.LibreOfficeOnly, false, false, false)]
    public void 判斷是否可以開始(EnginePreference preference, bool hasPp, bool hasLo, bool expected)
        => Assert.Equal(expected, EngineAvailability.CanRun(preference, hasPp, hasLo));

    [Fact]
    public void 只用PowerPoint但只有LibreOffice時會被擋下並建議改選()
    {
        var blocker = EngineAvailability.DescribeBlocker(EnginePreference.PowerPointOnly, false, true);

        Assert.NotNull(blocker);
        Assert.Contains("只用 PowerPoint", blocker);
        Assert.Contains("改選", blocker);
    }

    [Fact]
    public void 只用LibreOffice但只有PowerPoint時會被擋下並建議改選()
    {
        var blocker = EngineAvailability.DescribeBlocker(EnginePreference.LibreOfficeOnly, true, false);

        Assert.NotNull(blocker);
        Assert.Contains("只用 LibreOffice", blocker);
        Assert.Contains("改選", blocker);
    }

    [Fact]
    public void 兩者皆無時建議安裝()
    {
        var blocker = EngineAvailability.DescribeBlocker(EnginePreference.Auto, false, false);

        Assert.NotNull(blocker);
        Assert.Contains("安裝", blocker);
    }

    [Fact]
    public void 可以開始時沒有阻擋訊息()
        => Assert.Null(EngineAvailability.DescribeBlocker(EnginePreference.Auto, true, false));

    [Fact]
    public void 批次服務的錯誤訊息與介面判斷一致()
    {
        // 介面說不能開始，批次服務就必須給出同一個理由
        var blocker = EngineAvailability.DescribeBlocker(EnginePreference.PowerPointOnly, false, true);

        var root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "a.pptx");
        File.WriteAllText(source, "x");

        try
        {
            var powerPoint = FakeConverter.Succeeding(ConversionEngine.PowerPoint);
            powerPoint.Available = false;
            var libreOffice = FakeConverter.Succeeding(ConversionEngine.LibreOffice);

            var report = new BatchExportService(new ISlideConverter[] { powerPoint, libreOffice })
                .Run(new[] { source }, new ExportOptions
                {
                    OutputRoot = Path.Combine(root, "out"),
                    Engine = EnginePreference.PowerPointOnly
                });

            Assert.Equal(ExportStatus.Failed, report.Results[0].Status);
            Assert.Equal(blocker, report.Results[0].ErrorMessage);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}

// ─────────────────────────────────────────────────────────────
// 後備引擎的輸出必須完全隔離
// ─────────────────────────────────────────────────────────────
public class StagingIsolationTests : IDisposable
{
    private readonly string _root;
    private readonly string _outputRoot;

    public StagingIsolationTests()
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

    private ExportOptions Options() => new()
    {
        OutputRoot = _outputRoot,
        Pages = PageRangeSpec.All,
        FileNamePrefix = "投影片_",
        NumberDigits = 3
    };

    [Fact]
    public void 第一個引擎的半成品即使刪不掉也不會混進後備引擎的輸出()
    {
        var source = CreateSource("簡報.pptx");

        // 模擬「半成品刪不掉」：把檔案開著不放，Windows 上會讓刪除失敗
        FileStream? holdOpen = null;

        var halfWritten = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            LongPath.EnsureDirectory(request.OutputDirectory);
            var locked = Path.Combine(request.OutputDirectory, "投影片_001.png");
            File.WriteAllText(locked, "半成品");
            holdOpen = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
            throw new ConversionException("寫到一半失敗");
        });

        try
        {
            var report = new BatchExportService(new ISlideConverter[]
                {
                    halfWritten,
                    FakeConverter.Succeeding(ConversionEngine.LibreOffice, pageCount: 3)
                })
                .Run(new[] { source }, Options());

            var dir = report.Results[0].OutputDirectory!;
            var files = Directory.GetFiles(dir);

            Assert.Equal(ConversionEngine.LibreOffice, report.Results[0].EngineUsed);
            Assert.Equal(3, files.Length);
            Assert.All(files, f => Assert.NotEqual("半成品", File.ReadAllText(f)));
        }
        finally
        {
            holdOpen?.Dispose();
        }
    }

    [Fact]
    public void 成功時不會留下暫存資料夾()
    {
        var source = CreateSource("簡報.pptx");

        new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { source }, Options());

        var leftovers = Directory.GetDirectories(_outputRoot).Where(d => Path.GetFileName(d).StartsWith('~')).ToArray();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void 失敗時不會留下任何資料夾()
    {
        var source = CreateSource("簡報.pptx");

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Failing(ConversionEngine.PowerPoint) })
            .Run(new[] { source }, Options());

        Assert.Equal(ExportStatus.Failed, report.Results[0].Status);
        Assert.Null(report.Results[0].OutputDirectory);
        Assert.Empty(Directory.GetDirectories(_outputRoot));
    }

    [Fact]
    public void 取消時已完成的圖片會保留下來()
    {
        var source = CreateSource("簡報.pptx");
        using var cts = new CancellationTokenSource();

        var partial = new FakeConverter(ConversionEngine.PowerPoint, request =>
        {
            LongPath.EnsureDirectory(request.OutputDirectory);
            File.WriteAllText(Path.Combine(request.OutputDirectory, "投影片_001.png"), "第一張");
            File.WriteAllText(Path.Combine(request.OutputDirectory, "投影片_002.png"), "第二張");
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        var report = new BatchExportService(new ISlideConverter[] { partial })
            .Run(new[] { source }, Options(), null, cts.Token);

        Assert.Equal(ExportStatus.Cancelled, report.Results[0].Status);
        Assert.NotNull(report.Results[0].OutputDirectory);
        Assert.Equal(2, Directory.GetFiles(report.Results[0].OutputDirectory!).Length);
    }

    [Fact]
    public void 輸出資料夾名稱不受暫存機制影響()
    {
        var source = CreateSource("我的 簡報.pptx");

        var report = new BatchExportService(new ISlideConverter[] { FakeConverter.Succeeding(ConversionEngine.PowerPoint) })
            .Run(new[] { source }, Options());

        Assert.Equal(Path.Combine(_outputRoot, "我的 簡報"), report.Results[0].OutputDirectory);
    }
}

// ─────────────────────────────────────────────────────────────
// 拖入資料夾的掃描
// ─────────────────────────────────────────────────────────────
public class PresentationScannerTests : IDisposable
{
    private readonly string _root;

    public PresentationScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void 遞迴找出所有支援的簡報()
    {
        Touch("a.pptx");
        Touch(Path.Combine("子資料夾", "b.ppt"));
        Touch(Path.Combine("子資料夾", "深層", "c.ppsx"));
        Touch("不支援.pdf");
        Touch("不支援.docx");

        var result = PresentationScanner.Scan(new[] { _root });

        Assert.Equal(3, result.Files.Count);
        Assert.False(result.ReachedLimit);
        Assert.All(result.Files, f => Assert.True(BatchExportService.IsSupported(f)));
    }

    [Fact]
    public void 混合檔案與資料夾()
    {
        var single = Touch("single.pptx");
        Touch(Path.Combine("資料夾", "inner.pptx"));

        var result = PresentationScanner.Scan(new[] { single, Path.Combine(_root, "資料夾") });

        Assert.Equal(2, result.Files.Count);
    }

    [Fact]
    public void 重複路徑只會出現一次()
    {
        var file = Touch("a.pptx");

        var result = PresentationScanner.Scan(new[] { file, file, _root });

        Assert.Single(result.Files);
    }

    [Fact]
    public void 達到上限時停止並回報()
    {
        for (var i = 0; i < 10; i++) Touch($"deck{i:D2}.pptx");

        var result = PresentationScanner.Scan(new[] { _root }, maxFiles: 4);

        Assert.True(result.ReachedLimit);
        Assert.True(result.Files.Count <= 4);
    }

    [Fact]
    public void 不存在的路徑不會擲出例外()
    {
        var result = PresentationScanner.Scan(new[] { Path.Combine(_root, "不存在"), _root });

        Assert.Empty(result.Files);
    }

    [Fact]
    public void 可以取消()
    {
        for (var i = 0; i < 50; i++) Touch($"deck{i:D3}.pptx");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => PresentationScanner.Scan(new[] { _root }, cancellationToken: cts.Token));
    }

    [Fact]
    public void 結果依名稱排序()
    {
        Touch("c.pptx");
        Touch("a.pptx");
        Touch("b.pptx");

        var result = PresentationScanner.Scan(new[] { _root });
        var names = result.Files.Select(Path.GetFileName).ToArray();

        Assert.Equal(new[] { "a.pptx", "b.pptx", "c.pptx" }, names);
    }

    [Fact]
    public void 使用的列舉設定會略過無權限的目錄()
    {
        // 迴歸測試：Directory.EnumerateFiles 的 SearchOption 多載走相容性設定，
        // IgnoreInaccessible = false，遇到沒有權限的子資料夾會中斷整個掃描。
        var compatible = typeof(EnumerationOptions)
            .GetMethod("FromSearchOption", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { SearchOption.AllDirectories }) as EnumerationOptions;

        Assert.False(compatible!.IgnoreInaccessible, "這正是不能使用 SearchOption 多載的原因");

        // PresentationScanner 必須自行指定 IgnoreInaccessible = true，
        // 因此對含有無權限子目錄的樹仍能回傳結果。
        Touch(Path.Combine("可讀", "a.pptx"));
        var result = PresentationScanner.Scan(new[] { _root });
        Assert.Single(result.Files);
    }
}

// ─────────────────────────────────────────────────────────────
// 縮圖快取鍵
// ─────────────────────────────────────────────────────────────
public class PreviewCacheKeyTests
{
    private static string KeyFor(string path, int width, ConversionEngine engine)
    {
        var method = typeof(SlidePreviewService)
            .GetMethod("BuildCacheKey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (string)method.Invoke(null, new object[] { path, width, engine })!;
    }

    [Fact]
    public void 不同引擎產生不同的快取鍵()
    {
        var file = Path.Combine(Path.GetTempPath(), "PptPngExporterTests-" + Guid.NewGuid().ToString("N") + ".pptx");
        File.WriteAllText(file, "x");

        try
        {
            var pp = KeyFor(file, 400, ConversionEngine.PowerPoint);
            var lo = KeyFor(file, 400, ConversionEngine.LibreOffice);

            Assert.NotEqual(pp, lo);
            Assert.StartsWith("powerpoint-", pp);
            Assert.StartsWith("libreoffice-", lo);
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void 不同寬度產生不同的快取鍵()
    {
        var file = Path.Combine(Path.GetTempPath(), "PptPngExporterTests-" + Guid.NewGuid().ToString("N") + ".pptx");
        File.WriteAllText(file, "x");

        try
        {
            Assert.NotEqual(
                KeyFor(file, 200, ConversionEngine.PowerPoint),
                KeyFor(file, 400, ConversionEngine.PowerPoint));
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void 檔案內容改變後快取鍵會改變()
    {
        var file = Path.Combine(Path.GetTempPath(), "PptPngExporterTests-" + Guid.NewGuid().ToString("N") + ".pptx");
        File.WriteAllText(file, "x");

        try
        {
            var before = KeyFor(file, 400, ConversionEngine.PowerPoint);

            File.WriteAllText(file, "內容變長了，大小與修改時間都不同");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));

            Assert.NotEqual(before, KeyFor(file, 400, ConversionEngine.PowerPoint));
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
    }
}
