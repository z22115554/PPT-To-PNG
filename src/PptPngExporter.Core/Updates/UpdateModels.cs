using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;

namespace PptPngExporter.Core.Updates;

/// <summary>
/// 版本號。接受 "1.2.0"、"v1.2.0"、"1.2.0.0"、"1.3.0-beta.1" 等寫法。
/// 比較時只看前三個數字，預發行版本（含 '-'）永遠小於同號的正式版。
/// </summary>
public readonly struct ReleaseVersion : IComparable<ReleaseVersion>, IEquatable<ReleaseVersion>
{
    public ReleaseVersion(int major, int minor, int patch, string? preRelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string? PreRelease { get; }

    public bool IsPreRelease => PreRelease is not null;

    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];

        string? pre = null;
        var dash = value.IndexOf('-');
        if (dash >= 0)
        {
            pre = value[(dash + 1)..];
            value = value[..dash];
        }

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length) { numbers[i] = 0; continue; }
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], pre);
        return true;
    }

    public static ReleaseVersion Parse(string text)
        => TryParse(text, out var v) ? v : throw new FormatException($"無法解析版本號「{text}」。");

    /// <summary>本程式目前的版本。</summary>
    public static ReleaseVersion Current { get; } =
        TryParse(typeof(ReleaseVersion).Assembly.GetName().Version?.ToString(), out var v)
            ? v
            : new ReleaseVersion(0, 0, 0);

    public int CompareTo(ReleaseVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // 正式版 > 預發行版
        return (IsPreRelease, other.IsPreRelease) switch
        {
            (false, true) => 1,
            (true, false) => -1,
            (true, true) => string.CompareOrdinal(PreRelease, other.PreRelease),
            _ => 0
        };
    }

    public bool Equals(ReleaseVersion other) => CompareTo(other) == 0;
    public override bool Equals(object? obj) => obj is ReleaseVersion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;
    public static bool operator >=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) >= 0;
    public static bool operator <=(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) <= 0;
    public static bool operator ==(ReleaseVersion a, ReleaseVersion b) => a.Equals(b);
    public static bool operator !=(ReleaseVersion a, ReleaseVersion b) => !a.Equals(b);

    public override string ToString()
        => PreRelease is null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";
}

/// <summary>更新檔案的描述。</summary>
public sealed class UpdateAsset
{
    [JsonPropertyName("fileName")] public string FileName { get; set; } = string.Empty;
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; set; }

    /// <summary>由發佈來源填入的實際下載網址。</summary>
    [JsonIgnore] public string? DownloadUrl { get; set; }
}

/// <summary>
/// 隨每次發行一起上傳的 update-manifest.json。
///
/// 讓「這一版能不能用軟體內部更新」由發行端決定：
/// 有重大架構變更時把 <see cref="RequiresManualDownload"/> 設為 true，
/// 或用 <see cref="MinimumInAppUpdateFrom"/> 指定太舊的版本必須手動重裝。
/// </summary>
public sealed class UpdateManifest
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("releasedAt")] public string? ReleasedAt { get; set; }
    [JsonPropertyName("releaseUrl")] public string? ReleaseUrl { get; set; }
    [JsonPropertyName("notes")] public string? Notes { get; set; }

    /// <summary>true 代表這一版必須手動下載安裝（重大架構變更）。</summary>
    [JsonPropertyName("requiresManualDownload")] public bool RequiresManualDownload { get; set; }

    /// <summary>低於這個版本的使用者必須手動重裝。</summary>
    [JsonPropertyName("minimumInAppUpdateFrom")] public string? MinimumInAppUpdateFrom { get; set; }

    [JsonPropertyName("portable")] public UpdateAsset? Portable { get; set; }
    [JsonPropertyName("installer")] public UpdateAsset? Installer { get; set; }

    public ReleaseVersion GetVersion()
        => ReleaseVersion.TryParse(Version, out var v) ? v : new ReleaseVersion(0, 0, 0);
}

/// <summary>安裝方式。決定要下載哪一個檔案、以及用什麼方式套用。</summary>
public enum InstallationKind
{
    /// <summary>免安裝版：單一 .exe，用改名的方式就地替換。</summary>
    Portable = 0,

    /// <summary>安裝版：由 Inno Setup 安裝，執行新的安裝程式覆蓋。</summary>
    Installed = 1,

    /// <summary>從原始碼執行（開發中），不進行自動更新。</summary>
    DevelopmentBuild = 2
}

public enum UpdateAvailability
{
    /// <summary>已經是最新版。</summary>
    UpToDate = 0,

    /// <summary>有新版本，可以直接在程式內更新。</summary>
    CanUpdateInApp = 1,

    /// <summary>有新版本，但必須手動下載安裝。</summary>
    ManualDownloadRequired = 2,

    /// <summary>檢查失敗。</summary>
    CheckFailed = 3
}

/// <summary>檢查更新的結果。</summary>
public sealed class UpdateCheckResult
{
    public required UpdateAvailability Availability { get; init; }
    public ReleaseVersion CurrentVersion { get; init; }
    public ReleaseVersion LatestVersion { get; init; }
    public UpdateManifest? Manifest { get; init; }
    public UpdateAsset? Asset { get; init; }
    public InstallationKind Installation { get; init; }

    /// <summary>顯示給使用者的說明。</summary>
    public string Message { get; init; } = string.Empty;

    public string? ReleaseUrl => Manifest?.ReleaseUrl;

    public bool HasUpdate => Availability is UpdateAvailability.CanUpdateInApp or UpdateAvailability.ManualDownloadRequired;

    public static UpdateCheckResult Failed(string message, ReleaseVersion current) => new()
    {
        Availability = UpdateAvailability.CheckFailed,
        CurrentVersion = current,
        LatestVersion = current,
        Message = message
    };
}

/// <summary>
/// 「要不要更新、能不能在程式內更新」的判斷。
/// 刻意做成純函式，不碰網路與檔案系統，讓所有分支都能被測試涵蓋。
/// </summary>
public static class UpdatePolicy
{
    public static UpdateCheckResult Evaluate(
        ReleaseVersion current,
        UpdateManifest manifest,
        InstallationKind installation)
    {
        var latest = manifest.GetVersion();

        if (latest <= current)
        {
            return new UpdateCheckResult
            {
                Availability = UpdateAvailability.UpToDate,
                CurrentVersion = current,
                LatestVersion = latest,
                Manifest = manifest,
                Installation = installation,
                Message = $"已經是最新版本（{current}）。"
            };
        }

        // 從原始碼執行時不自動更新，否則會覆蓋開發中的建置結果
        if (installation == InstallationKind.DevelopmentBuild)
        {
            return Manual(current, latest, manifest, installation,
                $"有新版本 {latest}，但目前是從開發環境執行，請自行重新建置。");
        }

        if (manifest.RequiresManualDownload)
        {
            return Manual(current, latest, manifest, installation,
                $"版本 {latest} 有較大的變更，需要手動下載安裝。");
        }

        if (!string.IsNullOrWhiteSpace(manifest.MinimumInAppUpdateFrom)
            && ReleaseVersion.TryParse(manifest.MinimumInAppUpdateFrom, out var minimum)
            && current < minimum)
        {
            return Manual(current, latest, manifest, installation,
                $"目前版本（{current}）太舊，無法直接更新到 {latest}，請手動下載安裝。");
        }

        var asset = installation == InstallationKind.Installed ? manifest.Installer : manifest.Portable;

        if (asset is null || string.IsNullOrWhiteSpace(asset.FileName))
        {
            return Manual(current, latest, manifest, installation,
                $"版本 {latest} 沒有提供適用於目前安裝方式的更新檔，請手動下載。");
        }

        return new UpdateCheckResult
        {
            Availability = UpdateAvailability.CanUpdateInApp,
            CurrentVersion = current,
            LatestVersion = latest,
            Manifest = manifest,
            Asset = asset,
            Installation = installation,
            Message = $"有新版本 {latest} 可以更新（目前 {current}）。"
        };
    }

    private static UpdateCheckResult Manual(
        ReleaseVersion current, ReleaseVersion latest,
        UpdateManifest manifest, InstallationKind installation, string message) => new()
    {
        Availability = UpdateAvailability.ManualDownloadRequired,
        CurrentVersion = current,
        LatestVersion = latest,
        Manifest = manifest,
        Installation = installation,
        Message = message
    };
}
