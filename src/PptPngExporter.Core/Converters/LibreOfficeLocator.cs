using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PptPngExporter.Core.Converters;

/// <summary>尋找本機的 LibreOffice（soffice.exe）。</summary>
public static class LibreOfficeLocator
{
    /// <summary>使用者可用這個環境變數指定自訂安裝位置。</summary>
    public const string EnvironmentVariable = "LIBREOFFICE_PATH";

    private static string? _cached;
    private static bool _searched;

    public static void ResetCache()
    {
        _cached = null;
        _searched = false;
    }

    /// <summary>回傳 soffice.exe 的完整路徑；找不到時回傳 null。</summary>
    public static string? Find()
    {
        if (_searched) return _cached;
        _searched = true;
        _cached = Search();
        return _cached;
    }

    private static string? Search()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            try
            {
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch
            {
                // 路徑不合法，略過
            }
        }
        return null;
    }

    private static IEnumerable<string?> EnumerateCandidates()
    {
        // 1) 使用者指定
        var custom = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(custom))
        {
            yield return custom;
            yield return Path.Combine(custom, "soffice.exe");
            yield return Path.Combine(custom, "program", "soffice.exe");
        }

        if (OperatingSystem.IsWindows())
        {
            // 2) 與本程式一同散布的可攜版 LibreOffice（免安裝資料夾）
            var appDir = AppContext.BaseDirectory;
            yield return Path.Combine(appDir, "LibreOfficePortable", "App", "libreoffice", "program", "soffice.exe");
            yield return Path.Combine(appDir, "LibreOffice", "program", "soffice.exe");

            // 3) 登錄檔
            foreach (var fromRegistry in ReadRegistry()) yield return fromRegistry;

            // 4) 常見安裝路徑
            foreach (var root in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                     })
            {
                if (string.IsNullOrEmpty(root)) continue;
                yield return Path.Combine(root, "LibreOffice", "program", "soffice.exe");
                yield return Path.Combine(root, "LibreOffice 7", "program", "soffice.exe");
                yield return Path.Combine(root, "LibreOffice 25", "program", "soffice.exe");
                yield return Path.Combine(root, "Programs", "LibreOffice", "program", "soffice.exe");
            }
        }
        else
        {
            // 供開發／測試環境使用
            yield return "/usr/bin/soffice";
            yield return "/usr/bin/libreoffice";
            yield return "/opt/libreoffice/program/soffice";
        }

        // 5) PATH
        var exeName = OperatingSystem.IsWindows() ? "soffice.exe" : "soffice";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return Path.Combine(dir.Trim(), exeName);
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> ReadRegistry()
    {
        var results = new List<string>();
        if (!OperatingSystem.IsWindows()) return results;

        (RegistryHive Hive, RegistryView View, string Key, string Value)[] probes =
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\LibreOffice\UNO\InstallPath", ""),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\LibreOffice\UNO\InstallPath", ""),
            (RegistryHive.CurrentUser,  RegistryView.Default,    @"SOFTWARE\LibreOffice\UNO\InstallPath", ""),
            (RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\soffice.exe", ""),
            (RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\soffice.exe", "")
        };

        foreach (var probe in probes)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(probe.Hive, probe.View);
                using var key = baseKey.OpenSubKey(probe.Key);
                if (key?.GetValue(probe.Value) is not string raw || string.IsNullOrWhiteSpace(raw)) continue;

                var value = raw.Trim('"');
                results.Add(value);
                results.Add(Path.Combine(value, "soffice.exe"));
                results.Add(Path.Combine(value, "program", "soffice.exe"));
            }
            catch
            {
                // 沒有權限或機碼不存在
            }
        }

        return results;
    }
}
