using System.Diagnostics;
using PptPngExporter.Core.Converters;

namespace PptPngExporter.Core.Services;

/// <summary>
/// 記錄轉檔前既有的程序，結束後只清掉「本程式造成的」殘留程序，
/// 絕不會誤殺使用者自己開著的 PowerPoint 或 LibreOffice。
/// </summary>
public sealed class ProcessGuard
{
    private readonly string _processName;
    private readonly HashSet<int> _preExisting;
    private readonly IAppLogger _logger;

    private ProcessGuard(string processName, HashSet<int> preExisting, IAppLogger logger)
    {
        _processName = processName;
        _preExisting = preExisting;
        _logger = logger;
    }

    /// <summary>建立快照。processName 不含 .exe，例如 "POWERPNT"、"soffice"。</summary>
    public static ProcessGuard Snapshot(string processName, IAppLogger? logger = null)
        => new(processName, GetPids(processName), logger ?? NullLogger.Instance);

    /// <summary>
    /// 等待新產生的程序自行結束，超過寬限時間仍存在就強制關閉（含子程序）。
    /// </summary>
    public void KillSurvivors(TimeSpan grace)
    {
        var deadline = DateTime.UtcNow + grace;

        while (DateTime.UtcNow < deadline)
        {
            if (GetPids(_processName).Except(_preExisting).Any() == false) return;
            Thread.Sleep(200);
        }

        foreach (var pid in GetPids(_processName).Except(_preExisting).ToArray())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                _logger.Warn($"強制關閉殘留程序 {_processName} (PID {pid})。");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (ArgumentException)
            {
                // 程序在期間內自行結束
            }
            catch (Exception ex)
            {
                _logger.Warn($"無法關閉殘留程序 {_processName} (PID {pid})：{ex.Message}");
            }
        }
    }

    private static HashSet<int> GetPids(string processName)
    {
        try
        {
            var set = new HashSet<int>();
            foreach (var p in Process.GetProcessesByName(processName))
            {
                try { set.Add(p.Id); }
                finally { p.Dispose(); }
            }
            return set;
        }
        catch
        {
            return new HashSet<int>();
        }
    }
}
