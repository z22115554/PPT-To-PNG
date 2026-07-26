using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PptPngExporter.Core.Converters;

namespace PptPngExporter.Core.Updates;

/// <summary>
/// 更新來源設定。
///
/// 預設值編譯進程式，但可以用執行檔旁邊的 update.config.json 覆寫，
/// 這樣換 repo 或改用自架伺服器時不必重新編譯。
/// </summary>
public sealed class UpdateConfiguration
{
    /// <summary>GitHub 帳號或組織名稱。</summary>
    [JsonPropertyName("owner")] public string Owner { get; set; } = DefaultOwner;

    /// <summary>GitHub 儲存庫名稱。</summary>
    [JsonPropertyName("repository")] public string Repository { get; set; } = DefaultRepository;

    /// <summary>是否在啟動時自動檢查更新。</summary>
    [JsonPropertyName("checkOnStartup")] public bool CheckOnStartup { get; set; } = true;

    /// <summary>兩次自動檢查之間至少間隔幾小時。</summary>
    [JsonPropertyName("minimumHoursBetweenChecks")] public int MinimumHoursBetweenChecks { get; set; } = 20;

    // ─────────────────────────────────────────────────────────
    // 要換 GitHub 儲存庫時改這兩行就好（或放一份 update.config.json 在執行檔旁邊）。
    //
    // 免安裝版只發佈單一 .exe，使用者手上不會有 update.config.json，
    // 因此這兩個常數就是實際生效的設定，不能留佔位字串。
    public const string DefaultOwner = "z22115554";
    public const string DefaultRepository = "PPT-To-PNG";
    // ─────────────────────────────────────────────────────────

    public const string ConfigFileName = "update.config.json";
    public const string ManifestAssetName = "update-manifest.json";

    /// <summary>設定是否已經填好（沒填就不要去打 API）。</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Owner)
        && !Owner.StartsWith("REPLACE_WITH", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(Repository);

    public string LatestReleaseApiUrl => $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
    public string ReleasesPageUrl => $"https://github.com/{Owner}/{Repository}/releases";

    public static UpdateConfiguration Load(string? directory = null)
    {
        try
        {
            var path = Path.Combine(directory ?? AppContext.BaseDirectory, ConfigFileName);
            if (!File.Exists(path)) return new UpdateConfiguration();

            return JsonSerializer.Deserialize<UpdateConfiguration>(File.ReadAllText(path))
                   ?? new UpdateConfiguration();
        }
        catch
        {
            return new UpdateConfiguration();
        }
    }
}

/// <summary>判斷程式是以哪一種方式安裝的。</summary>
public static class InstallationInfo
{
    /// <summary>Inno Setup 產生的解除安裝程式檔名。</summary>
    public const string UninstallerName = "unins000.exe";

    public static InstallationKind Detect(string? processPath = null, string? baseDirectory = null)
    {
        processPath ??= Environment.ProcessPath;
        baseDirectory ??= AppContext.BaseDirectory;

        // 用 dotnet run 執行時，主程序是 dotnet 而不是我們的 exe。
        // 取不出執行檔名稱時同樣視為開發建置——寧可不自動更新，也不要誤判成免安裝版而去替換不該動的檔案。
        var processName = processPath is null ? null : Path.GetFileNameWithoutExtension(processPath);
        if (string.IsNullOrEmpty(processName) || processName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return InstallationKind.DevelopmentBuild;

        try
        {
            if (File.Exists(Path.Combine(baseDirectory, UninstallerName)))
                return InstallationKind.Installed;
        }
        catch
        {
            // 沒有權限讀取時當作免安裝版處理
        }

        // 從建置輸出目錄執行 = 開發中
        var normalized = baseDirectory.Replace('/', Path.DirectorySeparatorChar);
        if (normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            return InstallationKind.DevelopmentBuild;

        return InstallationKind.Portable;
    }

    public static string Describe(InstallationKind kind) => kind switch
    {
        InstallationKind.Portable => "免安裝版",
        InstallationKind.Installed => "安裝版",
        _ => "開發建置"
    };
}

/// <summary>取得最新發行資訊的來源。抽成介面以便測試。</summary>
public interface IReleaseSource
{
    Task<UpdateManifest?> GetLatestAsync(CancellationToken cancellationToken);
    Task DownloadAsync(UpdateAsset asset, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>從 GitHub Releases 取得更新資訊。</summary>
public sealed class GitHubReleaseSource : IReleaseSource
{
    private readonly UpdateConfiguration _config;
    private readonly IAppLogger _logger;
    private readonly HttpClient _http;

    public GitHubReleaseSource(UpdateConfiguration config, IAppLogger? logger = null, HttpClient? http = null)
    {
        _config = config;
        _logger = logger ?? NullLogger.Instance;
        _http = http ?? CreateClient();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub API 一定要有 User-Agent，否則回 403
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PptPngExporter", ReleaseVersion.Current.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<UpdateManifest?> GetLatestAsync(CancellationToken cancellationToken)
    {
        if (!_config.IsConfigured)
        {
            _logger.Warn("尚未設定更新來源的 GitHub 儲存庫，略過檢查。");
            return null;
        }

        var release = await _http.GetFromJsonAsync<GitHubRelease>(_config.LatestReleaseApiUrl, cancellationToken)
                      ?? throw new InvalidOperationException("GitHub 沒有回傳發行資訊。");

        var assets = release.Assets ?? new List<GitHubAsset>();

        // 優先使用隨發行上傳的 update-manifest.json
        var manifestAsset = assets.FirstOrDefault(a =>
            string.Equals(a.Name, UpdateConfiguration.ManifestAssetName, StringComparison.OrdinalIgnoreCase));

        UpdateManifest manifest;

        if (manifestAsset?.BrowserDownloadUrl is { Length: > 0 } manifestUrl)
        {
            var json = await _http.GetStringAsync(manifestUrl, cancellationToken);
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json)
                       ?? throw new InvalidOperationException("update-manifest.json 格式不正確。");
        }
        else
        {
            // 沒有清單時退回用發行資訊推斷（相容於還沒有清單的舊發行）
            _logger.Info("這個發行沒有 update-manifest.json，改用發行資訊推斷。");
            manifest = BuildFallbackManifest(release, assets);
        }

        manifest.ReleaseUrl ??= release.HtmlUrl;
        manifest.Notes ??= release.Body;
        if (string.IsNullOrWhiteSpace(manifest.Version)) manifest.Version = release.TagName ?? string.Empty;

        // 把實際下載網址填回去
        Attach(manifest.Portable, assets);
        Attach(manifest.Installer, assets);

        return manifest;
    }

    private static void Attach(UpdateAsset? asset, List<GitHubAsset> assets)
    {
        if (asset is null || string.IsNullOrWhiteSpace(asset.FileName)) return;

        var match = assets.FirstOrDefault(a => string.Equals(a.Name, asset.FileName, StringComparison.OrdinalIgnoreCase));
        asset.DownloadUrl = match?.BrowserDownloadUrl;
        if (asset.Size == 0 && match is not null) asset.Size = match.Size;
    }

    /// <summary>免安裝版的附件名稱關鍵字。免安裝版現在也是 .exe，不能再用副檔名分辨。</summary>
    private static bool LooksPortable(string? name) =>
        name is not null
        && (name.Contains("Portable", StringComparison.OrdinalIgnoreCase)
            || name.Contains("免安裝", StringComparison.Ordinal)
            || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    private static UpdateManifest BuildFallbackManifest(GitHubRelease release, List<GitHubAsset> assets)
    {
        var runnable = assets.Where(a =>
            a.Name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true
            || a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true).ToList();

        var portable = runnable.FirstOrDefault(a => LooksPortable(a.Name));
        var installer = runnable.FirstOrDefault(a => !LooksPortable(a.Name));

        return new UpdateManifest
        {
            Version = release.TagName ?? string.Empty,
            ReleaseUrl = release.HtmlUrl,
            Notes = release.Body,
            // 沒有雜湊可驗證就不允許自動更新，請使用者手動下載
            RequiresManualDownload = true,
            Portable = portable is null ? null : new UpdateAsset { FileName = portable.Name ?? string.Empty, Size = portable.Size },
            Installer = installer is null ? null : new UpdateAsset { FileName = installer.Name ?? string.Empty, Size = installer.Size }
        };
    }

    public async Task DownloadAsync(UpdateAsset asset, string targetPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(asset.DownloadUrl))
            throw new InvalidOperationException("這個更新檔沒有可用的下載網址。");

        using var response = await _http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? asset.Size;

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long written = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            written += read;
            if (total > 0) progress?.Report(Math.Clamp(written * 100d / total, 0, 100));
        }

        progress?.Report(100);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool PreRelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}

/// <summary>雜湊驗證。</summary>
public static class FileIntegrity
{
    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>比對雜湊。expected 為空字串時視為未提供而回傳 false。</summary>
    public static bool Verify(string path, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return false;

        var actual = ComputeSha256(path);
        return string.Equals(actual, expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }
}
