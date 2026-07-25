using System.Runtime.InteropServices;

namespace PptPngExporter.Core.IO;

/// <summary>
/// 長路徑處理。App 的資訊清單已開啟 longPathAware，但仍保留 <c>\\?\</c> 前綴作為保險，
/// 讓沒有啟用群組原則的機器也能寫入超過 260 字元的路徑。
/// </summary>
public static class LongPath
{
    /// <summary>超過此長度就視為「長路徑」，需要額外處理。</summary>
    public const int Threshold = 240;

    public static bool IsLong(string path) => path.Length >= Threshold;

    /// <summary>需要時加上擴充長度前綴。非 Windows 平台原樣回傳。</summary>
    public static string Extended(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return path;
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (!Path.IsPathRooted(path)) return path;

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            return @"\\?\UNC\" + path[2..];

        return @"\\?\" + path;
    }

    public static void EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(IsLong(path) ? Extended(path) : path);
    }
}

/// <summary>
/// 產生不會覆蓋既有檔案或資料夾的路徑。同名時自動附加「 (2)」、「 (3)」…
/// </summary>
public static class UniquePathResolver
{
    private const int MaxAttempts = 9999;

    /// <summary>
    /// 在 <paramref name="parentDirectory"/> 下取得一個尚未被使用的資料夾路徑。
    /// 不會實際建立資料夾。
    /// </summary>
    public static string ResolveDirectory(string parentDirectory, string desiredName)
    {
        ArgumentException.ThrowIfNullOrEmpty(parentDirectory);
        var baseName = FileNameSanitizer.Sanitize(desiredName, "簡報");

        for (var i = 1; i <= MaxAttempts; i++)
        {
            var candidateName = i == 1 ? baseName : $"{baseName} ({i})";
            var candidate = Path.Combine(parentDirectory, candidateName);
            if (!Exists(candidate)) return candidate;
        }

        return Path.Combine(parentDirectory, $"{baseName} ({DateTime.Now:yyyyMMdd-HHmmss})");
    }

    /// <summary>
    /// 在 <paramref name="directory"/> 下取得一個尚未被使用的檔案路徑。
    /// </summary>
    /// <param name="extension">副檔名，可含或不含前導點。</param>
    public static string ResolveFile(string directory, string baseName, string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        var ext = string.IsNullOrEmpty(extension)
            ? string.Empty
            : extension.StartsWith('.') ? extension : "." + extension;

        var stem = FileNameSanitizer.Sanitize(baseName, "圖片");

        for (var i = 1; i <= MaxAttempts; i++)
        {
            var candidateName = i == 1 ? stem + ext : $"{stem} ({i}){ext}";
            var candidate = Path.Combine(directory, candidateName);
            if (!Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    /// <summary>同時檢查檔案與資料夾，避免「資料夾與檔案同名」造成的建立失敗。</summary>
    private static bool Exists(string path)
    {
        var probe = LongPath.IsLong(path) ? LongPath.Extended(path) : path;
        return File.Exists(probe) || Directory.Exists(probe);
    }
}
