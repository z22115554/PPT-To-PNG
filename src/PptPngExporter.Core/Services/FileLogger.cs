using System.Text;
using PptPngExporter.Core.Converters;

namespace PptPngExporter.Core.Services;

/// <summary>
/// 簡易檔案記錄器。使用者回報問題時可直接把記錄檔附上。
/// 記錄失敗不會影響主流程。
/// </summary>
public sealed class FileLogger : IAppLogger
{
    private readonly object _gate = new();
    private readonly string _logPath;

    public FileLogger(string? directory = null)
    {
        var dir = directory ?? DefaultDirectory;
        try
        {
            Directory.CreateDirectory(dir);
            _logPath = Path.Combine(dir, $"log-{DateTime.Now:yyyyMMdd}.txt");
            Trim(dir);
        }
        catch
        {
            _logPath = Path.Combine(Path.GetTempPath(), $"PptPngExporter-{DateTime.Now:yyyyMMdd}.log");
        }
    }

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PptPngExporter", "logs");

    public string LogPath => _logPath;

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(level).Append("] ").AppendLine(message);
            if (exception is not null) sb.AppendLine(exception.ToString());

            lock (_gate)
            {
                File.AppendAllText(_logPath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 記錄失敗時保持安靜
        }
    }

    /// <summary>只保留最近 14 天的記錄檔。</summary>
    private static void Trim(string directory)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-14);
            foreach (var file in Directory.EnumerateFiles(directory, "log-*.txt"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch
        {
            // 略過
        }
    }
}
