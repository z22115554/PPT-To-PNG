using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using PptPngExporter.Core.Updates;
using Xunit;
using Xunit.Abstractions;

namespace PptPngExporter.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.0", 1, 2, 0)]
    [InlineData("v1.2.0", 1, 2, 0)]
    [InlineData("V1.2.0", 1, 2, 0)]
    [InlineData("1.2.0.0", 1, 2, 0)]
    [InlineData("1.2", 1, 2, 0)]
    [InlineData("2", 2, 0, 0)]
    [InlineData("  v10.20.30  ", 10, 20, 30)]
    public void 解析各種版本號寫法(string text, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var v));
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
    }

    [Fact]
    public void 解析預發行版本()
    {
        Assert.True(ReleaseVersion.TryParse("1.3.0-beta.1", out var v));
        Assert.Equal("beta.1", v.PreRelease);
        Assert.True(v.IsPreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("v")]
    [InlineData("1.x.0")]
    public void 拒絕不合法的版本號(string? text)
        => Assert.False(ReleaseVersion.TryParse(text, out _));

    [Theory]
    [InlineData("1.2.0", "1.1.0", 1)]
    [InlineData("1.1.0", "1.2.0", -1)]
    [InlineData("1.2.0", "1.2.0", 0)]
    [InlineData("2.0.0", "1.99.99", 1)]
    [InlineData("1.2.1", "1.2.0", 1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    public void 版本比較(string a, string b, int expected)
        => Assert.Equal(expected, Math.Sign(ReleaseVersion.Parse(a).CompareTo(ReleaseVersion.Parse(b))));

    [Fact]
    public void 正式版大於同號的預發行版()
    {
        Assert.True(ReleaseVersion.Parse("1.3.0") > ReleaseVersion.Parse("1.3.0-beta.1"));
        Assert.True(ReleaseVersion.Parse("1.3.0-beta.2") > ReleaseVersion.Parse("1.3.0-beta.1"));
    }

    [Fact]
    public void 運算子行為正確()
    {
        var a = ReleaseVersion.Parse("1.2.0");
        var b = ReleaseVersion.Parse("1.3.0");

        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a <= ReleaseVersion.Parse("1.2.0"));
        Assert.True(a >= ReleaseVersion.Parse("1.2.0"));
        Assert.True(a == ReleaseVersion.Parse("1.2.0"));
        Assert.True(a != b);
    }

    [Fact]
    public void 目前版本可以取得且不是零()
        => Assert.True(ReleaseVersion.Current > new ReleaseVersion(0, 0, 0), $"讀到 {ReleaseVersion.Current}");

    [Fact]
    public void 字串輸出()
    {
        Assert.Equal("1.2.0", ReleaseVersion.Parse("v1.2.0").ToString());
        Assert.Equal("1.3.0-beta.1", ReleaseVersion.Parse("1.3.0-beta.1").ToString());
    }
}

public class UpdatePolicyTests
{
    private static UpdateManifest Manifest(
        string version,
        bool manual = false,
        string? minimum = null,
        bool withPortable = true,
        bool withInstaller = true) => new()
    {
        Version = version,
        RequiresManualDownload = manual,
        MinimumInAppUpdateFrom = minimum,
        Portable = withPortable ? new UpdateAsset { FileName = "portable.zip", Sha256 = "abc" } : null,
        Installer = withInstaller ? new UpdateAsset { FileName = "setup.exe", Sha256 = "def" } : null
    };

    [Fact]
    public void 版本相同時視為最新()
    {
        var result = UpdatePolicy.Evaluate(ReleaseVersion.Parse("1.2.0"), Manifest("1.2.0"), InstallationKind.Portable);

        Assert.Equal(UpdateAvailability.UpToDate, result.Availability);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void 遠端版本較舊時也視為最新()
    {
        var result = UpdatePolicy.Evaluate(ReleaseVersion.Parse("1.3.0"), Manifest("1.2.0"), InstallationKind.Portable);

        Assert.Equal(UpdateAvailability.UpToDate, result.Availability);
    }

    [Fact]
    public void 有新版時免安裝版可以程式內更新()
    {
        var result = UpdatePolicy.Evaluate(ReleaseVersion.Parse("1.2.0"), Manifest("1.3.0"), InstallationKind.Portable);

        Assert.Equal(UpdateAvailability.CanUpdateInApp, result.Availability);
        Assert.Equal("portable.zip", result.Asset!.FileName);
    }

    [Fact]
    public void 安裝版會拿到安裝程式而不是免安裝包()
    {
        var result = UpdatePolicy.Evaluate(ReleaseVersion.Parse("1.2.0"), Manifest("1.3.0"), InstallationKind.Installed);

        Assert.Equal(UpdateAvailability.CanUpdateInApp, result.Availability);
        Assert.Equal("setup.exe", result.Asset!.FileName);
    }

    [Fact]
    public void 標記為重大變更時要求手動下載()
    {
        var result = UpdatePolicy.Evaluate(ReleaseVersion.Parse("1.2.0"), Manifest("2.0.0", manual: true), InstallationKind.Portable);

        Assert.Equal(UpdateAvailability.ManualDownloadRequired, result.Availability);
        Assert.Contains("手動", result.Message);
        Assert.True(result.HasUpdate);
    }

    [Fact]
    public void 版本太舊時要求手動下載()
    {
        var result = UpdatePolicy.Evaluate(
            ReleaseVersion.Parse("1.0.0"), Manifest("1.5.0", minimum: "1.2.0"), InstallationKind.Portable);

        Assert.Equal(UpdateAvailability.ManualDownloadRequired, result.Availability);
        Assert.Contains("太舊", result.Message);
    }

    [Fact]
    public void 達到最低版本要求時可以程式內更新()
    {
        var result = UpdatePolicy.Evaluate(
            ReleaseVersion.Parse("1.2.0"), Manifest("1.5.0", minimum: "1.2.0"), InstallationKind.Portable);

        Assert.Equal(UpdateAvailability.CanUpdateInApp, result.Availability);
    }

    [Fact]
    public void 缺少對應安裝方式的檔案時要求手動下載()
    {
        var result = UpdatePolicy.Evaluate(
            ReleaseVersion.Parse("1.2.0"), Manifest("1.3.0", withInstaller: false), InstallationKind.Installed);

        Assert.Equal(UpdateAvailability.ManualDownloadRequired, result.Availability);
        Assert.Contains("沒有提供", result.Message);
    }

    [Fact]
    public void 開發建置不會自動更新()
    {
        var result = UpdatePolicy.Evaluate(
            ReleaseVersion.Parse("1.2.0"), Manifest("1.3.0"), InstallationKind.DevelopmentBuild);

        Assert.Equal(UpdateAvailability.ManualDownloadRequired, result.Availability);
        Assert.Contains("開發環境", result.Message);
    }

    [Fact]
    public void 訊息包含新舊版本號()
    {
        var result = UpdatePolicy.Evaluate(ReleaseVersion.Parse("1.2.0"), Manifest("1.3.0"), InstallationKind.Portable);

        Assert.Contains("1.3.0", result.Message);
        Assert.Contains("1.2.0", result.Message);
    }
}

public class InstallationInfoTests : IDisposable
{
    private readonly string _root;

    public InstallationInfoTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 有解除安裝程式時判定為安裝版()
    {
        File.WriteAllText(Path.Combine(_root, InstallationInfo.UninstallerName), "x");

        Assert.Equal(InstallationKind.Installed,
            InstallationInfo.Detect(Path.Combine(_root, "app.exe"), _root));
    }

    [Fact]
    public void 沒有解除安裝程式時判定為免安裝版()
        => Assert.Equal(InstallationKind.Portable,
            InstallationInfo.Detect(Path.Combine(_root, "app.exe"), _root));

    [Fact]
    public void 主程序是dotnet時判定為開發建置()
        => Assert.Equal(InstallationKind.DevelopmentBuild,
            InstallationInfo.Detect("/usr/bin/dotnet", _root));

    /// <summary>
    /// 取不出執行檔名稱時要判定為開發建置。
    ///
    /// 這裡刻意傳空字串而不是 null：傳 null 的意思是「沿用目前程序」，
    /// 結果會變成取決於測試主機叫什麼名字（dotnet.exe 或 testhost.exe），
    /// 換一台機器或換一版 SDK 就可能翻盤。
    /// </summary>
    [Fact]
    public void 取不出執行檔名稱時判定為開發建置()
        => Assert.Equal(InstallationKind.DevelopmentBuild, InstallationInfo.Detect(string.Empty, _root));

    [Theory]
    [InlineData("bin", "Debug")]
    [InlineData("bin", "Release")]
    public void 從建置輸出目錄執行時判定為開發建置(string bin, string config)
    {
        var dir = Path.Combine(_root, bin, config, "net8.0-windows") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(dir);

        Assert.Equal(InstallationKind.DevelopmentBuild,
            InstallationInfo.Detect(Path.Combine(dir, "app.exe"), dir));
    }

    [Fact]
    public void 說明文字()
    {
        Assert.Equal("免安裝版", InstallationInfo.Describe(InstallationKind.Portable));
        Assert.Equal("安裝版", InstallationInfo.Describe(InstallationKind.Installed));
    }
}

public class PortableUpdateInstallerTests : IDisposable
{
    private readonly string _root;

    public PortableUpdateInstallerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 替換會保留備份且新檔案就位()
    {
        var current = Path.Combine(_root, "app.exe");
        var incoming = Path.Combine(_root, "new", "app.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(incoming)!);
        File.WriteAllText(current, "舊版");
        File.WriteAllText(incoming, "新版");

        var backup = PortableUpdateInstaller.Swap(current, incoming);

        Assert.Equal("新版", File.ReadAllText(current));
        Assert.Equal("舊版", File.ReadAllText(backup));
        Assert.Equal(current + PortableUpdateInstaller.BackupSuffix, backup);
        Assert.False(File.Exists(incoming));
    }

    [Fact]
    public void 上一次殘留的備份會被清掉再替換()
    {
        var current = Path.Combine(_root, "app.exe");
        var stale = current + PortableUpdateInstaller.BackupSuffix;
        var incoming = Path.Combine(_root, "new.exe");

        File.WriteAllText(current, "v2");
        File.WriteAllText(stale, "v1 殘留");
        File.WriteAllText(incoming, "v3");

        PortableUpdateInstaller.Swap(current, incoming);

        Assert.Equal("v3", File.ReadAllText(current));
        Assert.Equal("v2", File.ReadAllText(stale));
    }

    [Fact]
    public void 找不到新檔案時擲出例外且不動原檔()
    {
        var current = Path.Combine(_root, "app.exe");
        File.WriteAllText(current, "舊版");

        Assert.Throws<FileNotFoundException>(() =>
            PortableUpdateInstaller.Swap(current, Path.Combine(_root, "不存在.exe")));

        Assert.Equal("舊版", File.ReadAllText(current));
    }

    [Fact]
    public void 清理備份()
    {
        var current = Path.Combine(_root, "app.exe");
        var backup = current + PortableUpdateInstaller.BackupSuffix;
        File.WriteAllText(current, "new");
        File.WriteAllText(backup, "old");

        Assert.True(PortableUpdateInstaller.CleanUpBackup(current));
        Assert.False(File.Exists(backup));

        // 沒有備份時回傳 false 而不是擲出例外
        Assert.False(PortableUpdateInstaller.CleanUpBackup(current));
    }

    [Fact]
    public void 從ZIP取出執行檔()
    {
        var zipPath = Path.Combine(_root, "release.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            AddEntry(zip, "portable-win-x64/README.md", "說明文件內容比較長一點點");
            AddEntry(zip, "portable-win-x64/PPT PNG 匯出工具.exe", new string('E', 500));
            AddEntry(zip, "portable-win-x64/請先看我.txt", "短");
        }

        var extracted = PortableUpdateInstaller.ExtractExecutable(zipPath, Path.Combine(_root, "out"));

        Assert.Equal("PPT PNG 匯出工具.exe", Path.GetFileName(extracted));
        Assert.True(File.Exists(extracted));
    }

    [Fact]
    public void ZIP裡沒有執行檔時擲出可讀的例外()
    {
        var zipPath = Path.Combine(_root, "bad.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            AddEntry(zip, "readme.txt", "沒有 exe");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PortableUpdateInstaller.ExtractExecutable(zipPath, Path.Combine(_root, "out2")));

        Assert.Contains("執行檔", ex.Message);
    }

    /// <summary>
    /// 免安裝版現在發佈裸的單一 .exe，不再包 ZIP，所以下載回來的就是可以直接替換的檔案。
    /// </summary>
    [Fact]
    public void 下載的是裸exe時直接沿用()
    {
        var exePath = Path.Combine(_root, "PPT-PNG-Exporter-v1.3.0-Portable-win-x64.exe");
        File.WriteAllText(exePath, "新版內容");

        var resolved = PortableUpdateInstaller.ResolveExecutable(exePath, Path.Combine(_root, "out3"));

        Assert.Equal(exePath, resolved);
        Assert.Equal("新版內容", File.ReadAllText(resolved));
    }

    /// <summary>舊版發行的是 ZIP，使用者可能從那些版本更新上來，因此仍要能解壓縮。</summary>
    [Fact]
    public void 下載的是ZIP時解出執行檔()
    {
        var zipPath = Path.Combine(_root, "legacy.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            AddEntry(zip, "PPT PNG 匯出工具.exe", new string('E', 400));

        var resolved = PortableUpdateInstaller.ResolveExecutable(zipPath, Path.Combine(_root, "out4"));

        Assert.Equal("PPT PNG 匯出工具.exe", Path.GetFileName(resolved));
        Assert.True(File.Exists(resolved));
    }

    [Fact]
    public void 不認得的更新檔格式擲出可讀的例外()
    {
        var path = Path.Combine(_root, "更新檔.msi");
        File.WriteAllText(path, "x");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            PortableUpdateInstaller.ResolveExecutable(path, Path.Combine(_root, "out5")));

        Assert.Contains(".exe", ex.Message);
    }

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }
}

public class FileIntegrityTests : IDisposable
{
    private readonly string _file;

    public FileIntegrityTests()
    {
        _file = Path.Combine(Path.GetTempPath(), "PptPngExporterTests-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(_file, "hello");
    }

    public void Dispose()
    {
        try { File.Delete(_file); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 計算SHA256()
    {
        // "hello" 的 SHA-256
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            FileIntegrity.ComputeSha256(_file));
    }

    [Fact]
    public void 雜湊相符時通過()
        => Assert.True(FileIntegrity.Verify(_file, "2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824"));

    [Fact]
    public void 雜湊不符時失敗()
        => Assert.False(FileIntegrity.Verify(_file, "0000000000000000000000000000000000000000000000000000000000000000"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 沒有提供雜湊時視為驗證失敗(string? hash)
        => Assert.False(FileIntegrity.Verify(_file, hash));
}

public class GitHubReleaseSourceTests
{
    /// <summary>把指定的回應直接餵給 HttpClient。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _responses;
        public List<string> Requested { get; } = new();
        public StubHandler(Dictionary<string, string> responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);

            if (!_responses.TryGetValue(url, out var body))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static UpdateConfiguration Config() => new() { Owner = "someone", Repository = "PptPngExporter" };

    [Fact]
    public async Task 讀取隨發行上傳的清單()
    {
        var config = Config();
        var manifest = new UpdateManifest
        {
            Version = "1.3.0",
            Portable = new UpdateAsset { FileName = "portable.zip", Sha256 = "aa", Size = 10 },
            Installer = new UpdateAsset { FileName = "setup.exe", Sha256 = "bb", Size = 20 }
        };

        var releaseJson = """
        {
          "tag_name": "v1.3.0",
          "html_url": "https://github.com/someone/PptPngExporter/releases/tag/v1.3.0",
          "body": "更新說明",
          "assets": [
            { "name": "update-manifest.json", "browser_download_url": "https://example.test/manifest.json", "size": 100 },
            { "name": "portable.zip", "browser_download_url": "https://example.test/portable.zip", "size": 10 },
            { "name": "setup.exe", "browser_download_url": "https://example.test/setup.exe", "size": 20 }
          ]
        }
        """;

        var handler = new StubHandler(new Dictionary<string, string>
        {
            [config.LatestReleaseApiUrl] = releaseJson,
            ["https://example.test/manifest.json"] = JsonSerializer.Serialize(manifest)
        });

        var source = new GitHubReleaseSource(config, null, new HttpClient(handler));
        var result = await source.GetLatestAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("1.3.0", result!.Version);
        Assert.False(result.RequiresManualDownload);
        Assert.Equal("https://example.test/portable.zip", result.Portable!.DownloadUrl);
        Assert.Equal("https://example.test/setup.exe", result.Installer!.DownloadUrl);
        Assert.Equal("更新說明", result.Notes);
    }

    [Fact]
    public async Task 沒有清單的發行會退回推斷且要求手動下載()
    {
        // 這是相容路徑：1.1.0 那種還沒有清單的舊發行。
        // 沒有雜湊可驗證，因此絕不允許自動更新。
        var config = Config();
        var releaseJson = """
        {
          "tag_name": "v1.1.0",
          "html_url": "https://github.com/someone/PptPngExporter/releases/tag/v1.1.0",
          "assets": [
            { "name": "portable.zip", "browser_download_url": "https://example.test/p.zip", "size": 10 }
          ]
        }
        """;

        var handler = new StubHandler(new Dictionary<string, string> { [config.LatestReleaseApiUrl] = releaseJson });
        var source = new GitHubReleaseSource(config, null, new HttpClient(handler));

        var result = await source.GetLatestAsync(CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("v1.1.0", result!.Version);
        Assert.True(result.RequiresManualDownload);
    }

    [Fact]
    public async Task 未設定儲存庫時不會發出請求()
    {
        var handler = new StubHandler(new Dictionary<string, string>());
        var unconfigured = new UpdateConfiguration { Owner = string.Empty };
        var source = new GitHubReleaseSource(unconfigured, null, new HttpClient(handler));

        Assert.Null(await source.GetLatestAsync(CancellationToken.None));
        Assert.Empty(handler.Requested);
    }

    [Fact]
    public void 沒填或還留著佔位字串時視為未設定()
    {
        Assert.False(new UpdateConfiguration { Owner = string.Empty }.IsConfigured);
        Assert.False(new UpdateConfiguration { Owner = "   " }.IsConfigured);
        Assert.False(new UpdateConfiguration { Owner = "REPLACE_WITH_YOUR_GITHUB_ACCOUNT" }.IsConfigured);
        Assert.False(new UpdateConfiguration { Owner = "abc", Repository = string.Empty }.IsConfigured);
        Assert.True(new UpdateConfiguration { Owner = "abc", Repository = "def" }.IsConfigured);
    }

    /// <summary>
    /// 免安裝版只發佈單一 .exe，使用者手上不會有 update.config.json，
    /// 所以編譯進去的預設值就是實際生效的設定。留著佔位字串等於整個自動更新失效，
    /// 而且不會有任何錯誤訊息——只會安靜地什麼都不做。
    /// </summary>
    [Fact]
    public void 內建的預設儲存庫必須是可用的()
    {
        var shipped = new UpdateConfiguration();

        Assert.True(shipped.IsConfigured, "內建預設值仍是未設定狀態，自動更新不會運作。");
        Assert.DoesNotContain("REPLACE_WITH", UpdateConfiguration.DefaultOwner, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://api.github.com/repos/z22115554/PPT-To-PNG/releases/latest", shipped.LatestReleaseApiUrl);
    }

    [Fact]
    public void 網址組成正確()
    {
        var config = new UpdateConfiguration { Owner = "abc", Repository = "def" };

        Assert.Equal("https://api.github.com/repos/abc/def/releases/latest", config.LatestReleaseApiUrl);
        Assert.Equal("https://github.com/abc/def/releases", config.ReleasesPageUrl);
    }
}

public class UpdateServiceTests
{
    private sealed class FakeSource : IReleaseSource
    {
        public UpdateManifest? Manifest { get; set; }
        public Exception? ThrowOnGet { get; set; }
        public byte[] Payload { get; set; } = Encoding.UTF8.GetBytes("內容");

        public Task<UpdateManifest?> GetLatestAsync(CancellationToken cancellationToken)
            => ThrowOnGet is not null ? Task.FromException<UpdateManifest?>(ThrowOnGet) : Task.FromResult(Manifest);

        public Task DownloadAsync(UpdateAsset asset, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, Payload);
            progress?.Report(100);
            return Task.CompletedTask;
        }
    }

    private readonly ITestOutputHelper _output;
    public UpdateServiceTests(ITestOutputHelper output) => _output = output;

    private static UpdateConfiguration Config() => new() { Owner = "someone", Repository = "repo" };

    [Fact]
    public async Task 未設定時回報設定問題()
    {
        var service = new UpdateService(new FakeSource(), new UpdateConfiguration { Owner = string.Empty });

        var result = await service.CheckAsync();

        Assert.Equal(UpdateAvailability.CheckFailed, result.Availability);
        Assert.Contains("update.config.json", result.Message);
    }

    [Fact]
    public async Task 網路失敗時給出友善訊息()
    {
        var service = new UpdateService(
            new FakeSource { ThrowOnGet = new HttpRequestException("no network") },
            Config(), null, InstallationKind.Portable);

        var result = await service.CheckAsync();

        Assert.Equal(UpdateAvailability.CheckFailed, result.Availability);
        Assert.Contains("網路", result.Message);
    }

    [Fact]
    public async Task 沒有取得發行資訊時回報失敗()
    {
        var service = new UpdateService(new FakeSource { Manifest = null }, Config(), null, InstallationKind.Portable);

        Assert.Equal(UpdateAvailability.CheckFailed, (await service.CheckAsync()).Availability);
    }

    [Fact]
    public async Task 雜湊不符時中止更新且不留下檔案()
    {
        var source = new FakeSource
        {
            Manifest = new UpdateManifest
            {
                Version = "99.0.0",
                Portable = new UpdateAsset { FileName = "p.zip", Sha256 = new string('0', 64) }
            }
        };

        var service = new UpdateService(source, Config(), null, InstallationKind.Portable);
        var check = await service.CheckAsync();

        Assert.Equal(UpdateAvailability.CanUpdateInApp, check.Availability);

        var install = await service.DownloadAndApplyAsync(check, null);

        _output.WriteLine(install.Message);
        Assert.False(install.Success);
        Assert.Contains("驗證失敗", install.Message);
        Assert.False(Directory.Exists(Path.Combine(UpdateService.DownloadDirectory, "99.0.0")));
    }

    [Fact]
    public async Task 不可程式內更新時拒絕套用()
    {
        var source = new FakeSource
        {
            Manifest = new UpdateManifest
            {
                Version = "99.0.0",
                RequiresManualDownload = true,
                Portable = new UpdateAsset { FileName = "p.zip", Sha256 = "aa" }
            }
        };

        var service = new UpdateService(source, Config(), null, InstallationKind.Portable);
        var check = await service.CheckAsync();

        var install = await service.DownloadAndApplyAsync(check, null);

        Assert.False(install.Success);
        Assert.Contains("手動", install.Message);
    }

    [Fact]
    public async Task 已是最新版時不會嘗試下載()
    {
        var source = new FakeSource
        {
            Manifest = new UpdateManifest { Version = "0.0.1", Portable = new UpdateAsset { FileName = "p.zip" } }
        };

        var service = new UpdateService(source, Config(), null, InstallationKind.Portable);
        var check = await service.CheckAsync();

        Assert.Equal(UpdateAvailability.UpToDate, check.Availability);
        Assert.False((await service.DownloadAndApplyAsync(check, null)).Success);
    }

    [Fact]
    public void 清理不會擲出例外()
    {
        var exception = Record.Exception(() => UpdateService.CleanUpAfterUpdate());
        Assert.Null(exception);
    }
}

/// <summary>
/// 端到端：用 PowerShell 發行腳本實際產生的 JSON 格式，
/// 走完「解析清單 → 判斷可更新 → 下載 → 驗證雜湊 → 取出執行檔 → 替換」全流程。
/// </summary>
public class UpdatePipelineTests : IDisposable
{
    private readonly string _root;
    private readonly ITestOutputHelper _output;

    public UpdatePipelineTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        GC.SuppressFinalize(this);
    }

    /// <summary>這段 JSON 的欄位與縮排就是 PowerShell 的 ConvertTo-Json 實際輸出的格式。</summary>
    private const string RealWorldManifestJson = """
    {
      "schemaVersion": 1,
      "version": "1.3.0",
      "releasedAt": "2026-07-26",
      "releaseUrl": "https://github.com/someone/PptPngExporter/releases/tag/v1.3.0",
      "requiresManualDownload": false,
      "portable": {
        "fileName": "PPT-PNG-匯出工具-v1.3.0-免安裝版-win-x64.zip",
        "sha256": "697537d0087bd0642fdf5ab4a37b3648dd7ea0b9510cbaadfea606572a2c6a97",
        "size": 300000
      },
      "installer": {
        "fileName": "PPT-PNG-匯出工具-安裝程式-1.3.0.exe",
        "sha256": "16d094588b511d63cadb5f97b278ebde5187421262c03cd77b29744acafcdd37",
        "size": 200000
      }
    }
    """;

    [Fact]
    public void 可以解析發行腳本產生的清單()
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(RealWorldManifestJson);

        Assert.NotNull(manifest);
        Assert.Equal("1.3.0", manifest!.Version);
        Assert.Equal(new ReleaseVersion(1, 3, 0), manifest.GetVersion());
        Assert.False(manifest.RequiresManualDownload);
        Assert.Equal("697537d0087bd0642fdf5ab4a37b3648dd7ea0b9510cbaadfea606572a2c6a97", manifest.Portable!.Sha256);
        Assert.Equal(300000, manifest.Portable.Size);
        Assert.Contains("免安裝版", manifest.Portable.FileName);
        Assert.Contains("安裝程式", manifest.Installer!.FileName);
    }

    private sealed class ZipServingSource : IReleaseSource
    {
        private readonly string _zipPath;
        public UpdateManifest Manifest { get; }

        public ZipServingSource(string zipPath, UpdateManifest manifest)
        {
            _zipPath = zipPath;
            Manifest = manifest;
        }

        public Task<UpdateManifest?> GetLatestAsync(CancellationToken ct) => Task.FromResult<UpdateManifest?>(Manifest);

        public Task DownloadAsync(UpdateAsset asset, string targetPath, IProgress<double>? progress, CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(_zipPath, targetPath, overwrite: true);
            progress?.Report(100);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task 完整流程可以把舊執行檔換成新的()
    {
        // 準備「新版」的免安裝包
        var releaseDir = Path.Combine(_root, "release");
        Directory.CreateDirectory(releaseDir);
        var zipPath = Path.Combine(releaseDir, "new.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("portable-win-x64/PPT PNG 匯出工具.exe");
            using var stream = entry.Open();
            var bytes = Encoding.UTF8.GetBytes(new string('N', 2048));
            stream.Write(bytes, 0, bytes.Length);
        }

        // 模擬「目前安裝的」執行檔
        var installDir = Path.Combine(_root, "install");
        Directory.CreateDirectory(installDir);
        var currentExe = Path.Combine(installDir, "PPT PNG 匯出工具.exe");
        File.WriteAllText(currentExe, "舊版內容");

        var manifest = new UpdateManifest
        {
            Version = "99.9.9",
            Portable = new UpdateAsset
            {
                FileName = "new.zip",
                Sha256 = FileIntegrity.ComputeSha256(zipPath),
                DownloadUrl = "https://example.test/new.zip"
            }
        };

        var service = new UpdateService(
            new ZipServingSource(zipPath, manifest),
            new UpdateConfiguration { Owner = "someone", Repository = "repo" },
            null,
            InstallationKind.Portable,
            currentExecutable: currentExe,
            launchAfterUpdate: false);

        var check = await service.CheckAsync();
        Assert.Equal(UpdateAvailability.CanUpdateInApp, check.Availability);

        var reported = new List<double>();
        var install = await service.DownloadAndApplyAsync(check, new ImmediateProgress(reported.Add));

        _output.WriteLine(install.Message);

        Assert.True(install.Success, install.Message);
        Assert.True(install.RequiresRestart);

        // 新版就位、舊版留作備份
        Assert.Equal(2048, new FileInfo(currentExe).Length);
        Assert.Equal("舊版內容", File.ReadAllText(currentExe + PortableUpdateInstaller.BackupSuffix));
        Assert.Contains(100d, reported);
    }

    [Fact]
    public async Task 雜湊被竄改時不會替換執行檔()
    {
        var releaseDir = Path.Combine(_root, "release2");
        Directory.CreateDirectory(releaseDir);
        var zipPath = Path.Combine(releaseDir, "new.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("app.exe");
            using var stream = entry.Open();
            stream.Write(Encoding.UTF8.GetBytes("惡意內容"));
        }

        var installDir = Path.Combine(_root, "install2");
        Directory.CreateDirectory(installDir);
        var currentExe = Path.Combine(installDir, "app.exe");
        File.WriteAllText(currentExe, "原本的內容");

        var manifest = new UpdateManifest
        {
            Version = "99.9.9",
            Portable = new UpdateAsset
            {
                FileName = "new.zip",
                Sha256 = new string('a', 64),   // 對不上
                DownloadUrl = "https://example.test/new.zip"
            }
        };

        var service = new UpdateService(
            new ZipServingSource(zipPath, manifest),
            new UpdateConfiguration { Owner = "someone", Repository = "repo" },
            null, InstallationKind.Portable, currentExe, launchAfterUpdate: false);

        var install = await service.DownloadAndApplyAsync(await service.CheckAsync(), null);

        Assert.False(install.Success);
        Assert.Equal("原本的內容", File.ReadAllText(currentExe));
        Assert.False(File.Exists(currentExe + PortableUpdateInstaller.BackupSuffix));
    }

    private sealed class ImmediateProgress : IProgress<double>
    {
        private readonly Action<double> _handler;
        public ImmediateProgress(Action<double> handler) => _handler = handler;
        public void Report(double value) => _handler(value);
    }
}
