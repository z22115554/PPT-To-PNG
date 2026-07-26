using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PptPngExporter.Core.Converters;

namespace PptPngExporter.Core.Services;

/// <summary>
/// Windows Job Object 包裝。指派給 Job 的程序（及其後續產生的子程序）
/// 會在 Job 控制代碼關閉時一併結束。
///
/// 這是取代「依程序名稱掃描並殺除」的正確做法：只會影響我們自己啟動的程序樹，
/// 不可能誤殺使用者在轉檔期間開啟的 PowerPoint 或 LibreOffice。
/// </summary>
public sealed class WindowsJobObject : IDisposable
{
    private const int JobObjectExtendedLimitInfoClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private IntPtr _handle;
    private readonly IAppLogger _logger;

    private WindowsJobObject(IntPtr handle, IAppLogger logger)
    {
        _handle = handle;
        _logger = logger;
    }

    public bool IsValid => _handle != IntPtr.Zero;

    /// <summary>
    /// 建立 Job Object。非 Windows 平台或建立失敗時回傳 null，呼叫端需自行採用後備做法。
    /// </summary>
    public static WindowsJobObject? TryCreate(IAppLogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            return TryCreateWindows(log);
        }
        catch (Exception ex)
        {
            log.Warn("建立 Job Object 失敗：" + ex.Message);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static WindowsJobObject? TryCreateWindows(IAppLogger log)
    {
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            log.Warn($"CreateJobObject 失敗（Win32 錯誤 {Marshal.GetLastWin32Error()}）。");
            return null;
        }

        var info = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(info, buffer, fDeleteOld: false);

            if (!SetInformationJobObject(handle, JobObjectExtendedLimitInfoClass, buffer, (uint)size))
            {
                log.Warn($"SetInformationJobObject 失敗（Win32 錯誤 {Marshal.GetLastWin32Error()}）。");
                CloseHandle(handle);
                return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return new WindowsJobObject(handle, log);
    }

    /// <summary>把程序指派給這個 Job。</summary>
    public bool TryAssign(Process process)
    {
        if (!IsValid || !OperatingSystem.IsWindows()) return false;

        try
        {
            if (process.HasExited) return false;
            if (AssignProcessToJobObject(_handle, process.Handle)) return true;

            _logger.Warn($"AssignProcessToJobObject 失敗（Win32 錯誤 {Marshal.GetLastWin32Error()}）。");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warn("指派程序到 Job Object 失敗：" + ex.Message);
            return false;
        }
    }

    /// <summary>關閉 Job，連同其中所有程序一併結束。</summary>
    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle == IntPtr.Zero) return;

        try
        {
            CloseHandle(handle);
        }
        catch
        {
            // 關閉失敗無從補救
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint infoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}

/// <summary>
/// 只結束「明確登記過、確定由本程式啟動」的程序。
///
/// 取代舊版的 ProcessGuard：舊版用「轉檔前後的 PID 差集」判斷，
/// 使用者若在轉檔途中才開啟 PowerPoint 或 LibreOffice，也會被當成殘留程序關掉。
/// </summary>
public sealed class OwnedProcessGuard
{
    private readonly IAppLogger _logger;
    private readonly HashSet<int> _ownedPids = new();

    public OwnedProcessGuard(IAppLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    public IReadOnlyCollection<int> OwnedPids => _ownedPids;

    /// <summary>登記一個確定由本程式啟動的程序。</summary>
    public void Track(int processId)
    {
        if (processId > 0) _ownedPids.Add(processId);
    }

    public void Track(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited) Track(process.Id);
        }
        catch
        {
            // 程序已結束
        }
    }

    /// <summary>
    /// 等待登記過的程序自行結束；超過寬限時間仍在的才強制關閉（含其子程序）。
    /// 沒有登記過的程序一律不碰。
    /// </summary>
    public void KillSurvivors(TimeSpan grace)
    {
        if (_ownedPids.Count == 0) return;

        var deadline = DateTime.UtcNow + grace;

        while (DateTime.UtcNow < deadline && _ownedPids.Any(IsRunning))
            Thread.Sleep(200);

        foreach (var pid in _ownedPids.Where(IsRunning).ToArray())
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                _logger.Warn($"強制關閉本程式啟動的殘留程序 {process.ProcessName} (PID {pid})。");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (ArgumentException)
            {
                // 期間內自行結束了
            }
            catch (Exception ex)
            {
                _logger.Warn($"關閉 PID {pid} 失敗：{ex.Message}");
            }
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
