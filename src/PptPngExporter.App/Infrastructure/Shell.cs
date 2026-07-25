using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PptPngExporter.App.Infrastructure;

/// <summary>與 Windows 檔案總管互動的小工具。</summary>
public static class Shell
{
    /// <summary>在檔案總管中開啟資料夾。</summary>
    public static bool OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (!Directory.Exists(path)) return false;
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>在檔案總管中開啟資料夾並選取指定檔案。</summary>
    public static bool RevealFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            if (!File.Exists(path)) return OpenFolder(Path.GetDirectoryName(path));
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{Path.GetFullPath(path)}\"",
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// PowerPoint 的 COM 自動化必須在 STA 執行緒上呼叫，
/// 而 UI 執行緒不能被長時間阻塞，因此另開一條專用的 STA 背景執行緒。
/// </summary>
public static class StaRunner
{
    public static Task<T> RunAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (OperationCanceledException) { tcs.SetCanceled(); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        {
            IsBackground = true,
            Name = "轉檔工作執行緒"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}

/// <summary>記住使用者上次的設定，下次開啟時直接沿用。</summary>
public sealed class AppSettings
{
    public string? OutputFolder { get; set; }
    public bool UseAllPages { get; set; } = true;
    public string? PageRange { get; set; }
    public string? ImageWidth { get; set; }
    public string? FileNamePrefix { get; set; }
    public int EnginePreference { get; set; }
    public int PageMode { get; set; }
    public int Numbering { get; set; }
    public int NumberDigits { get; set; } = 3;

    [JsonIgnore]
    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PptPngExporter", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 設定存不起來不影響使用
        }
    }
}
