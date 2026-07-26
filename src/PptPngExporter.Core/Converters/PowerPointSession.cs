using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PptPngExporter.Core.Interop;
using PptPngExporter.Core.Services;
using static PptPngExporter.Core.Converters.PowerPointConstants;

namespace PptPngExporter.Core.Converters;

/// <summary>
/// 一次 PowerPoint 自動化工作階段：啟動（或借用）Application、套用安全性設定，
/// 結束時依 <see cref="PowerPointSessionPolicy"/> 決定要不要關閉它。
///
/// 抽出來的目的是讓「整批簡報共用同一個 PowerPoint」成為可能。原本每個檔案都會
/// 啟動再關閉一次 PowerPoint，100 份簡報就是 100 次冷啟動，光啟動就要好幾分鐘。
///
/// <b>執行緒親和性</b>：COM 物件綁在建立它的 STA 執行緒上，因此同一個 session
/// 必須從頭到尾在同一條執行緒上使用與釋放。BatchExportService 整個 Run 都在
/// 同一條 STA 執行緒上執行，符合這個前提。
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PowerPointSession : IDisposable
{
    private const string ProgId = "PowerPoint.Application";

    private readonly IAppLogger _logger;
    private readonly OwnedProcessGuard _guard;
    private OfficeSettingsGuard? _settings;
    private bool _disposed;

    private PowerPointSession(IAppLogger logger, ComObject app, PowerPointSessionPolicy policy)
    {
        _logger = logger;
        _guard = new OwnedProcessGuard(logger);
        App = app;
        Policy = policy;
    }

    public ComObject App { get; }
    public ComObject Presentations { get; private set; } = null!;
    public PowerPointSessionPolicy Policy { get; }

    public static PowerPointSession Create(IAppLogger logger, CancellationToken cancellationToken)
    {
        var policy = new PowerPointSessionPolicy(IsPowerPointRunning());
        logger.Info("PowerPoint 工作階段：" + policy.Describe());

        var app = ComObject.TryCreate(ProgId)
                  ?? throw new ConversionException("無法啟動 PowerPoint，請確認 Office 安裝是否正常。");

        var session = new PowerPointSession(logger, app, policy);

        try
        {
            session.UseToken(cancellationToken);

            session._settings = new OfficeSettingsGuard(
                read: name => app.TryGetInt(name),
                write: (name, value) => app.TrySet(name, value),
                logger: logger);

            // 這一行必須在開啟任何外部簡報「之前」執行。
            // Office 自動化的巨集安全性預設是 msoAutomationSecurityLow（直接執行巨集），
            // 使用者拖進來的 .ppt / .pps 可能夾帶巨集。
            if (!session._settings.Apply(AutomationSecurity, AutomationSecurityForceDisable))
                logger.Warn("無法設定 AutomationSecurity，這個版本的 PowerPoint 可能不支援；巨集停用無法保證。");

            session._settings.Apply(DisplayAlerts, AlertsNone);

            // 只登記由我們啟動的執行個體，作為 Quit 失敗時的最後手段
            if (policy.MayKillLeftoverProcesses)
            {
                var pid = TryGetProcessId(app, logger);
                if (pid is { } value)
                {
                    session._guard.Track(value);
                    logger.Info($"本程式啟動的 PowerPoint PID = {value}。");
                }
                else
                {
                    logger.Warn("無法取得 PowerPoint 的 PID，結束時只會呼叫 Quit()，不做強制關閉。");
                }
            }

            session.Presentations = app.GetObject("Presentations");
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 換上這次轉檔的取消權杖。session 會跨多個檔案重複使用，而每個檔案有自己的權杖；
    /// 之後從 App / Presentations 衍生出來的子物件會繼承新的權杖。
    /// </summary>
    public void UseToken(CancellationToken cancellationToken)
    {
        App.CancellationToken = cancellationToken;
        if (Presentations is not null) Presentations.CancellationToken = cancellationToken;
    }

    /// <summary>找出已經在 PowerPoint 中開啟的同一份簡報；沒有則回傳 null。</summary>
    public ComObject? FindAlreadyOpen(string fullPath)
    {
        try
        {
            var count = Presentations.Get<int>("Count");
            for (var i = 1; i <= count; i++)
            {
                var candidate = Presentations.CallObject("Item", i);
                try
                {
                    var name = candidate.GetOrDefault<string>("FullName", string.Empty);
                    if (!string.IsNullOrEmpty(name) &&
                        string.Equals(Path.GetFullPath(name), fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
                catch { }
                candidate.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("列舉已開啟的簡報時發生問題：" + ex.Message);
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Presentations?.Dispose();

        // 還原 AutomationSecurity 與 DisplayAlerts。借用使用者的執行個體時這是必要的。
        _settings?.Dispose();

        if (Policy.MayQuitApplication) SafeQuit();
        else _logger.Info("PowerPoint 由使用者開啟，保持原狀不關閉。");

        App.Dispose();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        // 只會動到上面明確登記的 PID
        _guard.KillSurvivors(TimeSpan.FromSeconds(8));
    }

    private void SafeQuit()
    {
        try { App.Call("Quit"); }
        catch (Exception ex) { _logger.Warn("結束 PowerPoint 時發生問題：" + ex.Message); }
    }

    private static bool IsPowerPointRunning()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("POWERPNT")) { p.Dispose(); return true; }
            return false;
        }
        catch
        {
            // 查不到時保守處理：當作使用者已開啟，寧可不關也不要誤關
            return true;
        }
    }

    /// <summary>由 Application.HWND 反查實際的程序 ID。</summary>
    private static int? TryGetProcessId(ComObject app, IAppLogger logger)
    {
        try
        {
            var hwnd = app.TryGetInt("HWND");
            if (hwnd is not { } handle || handle == 0) return null;

            _ = GetWindowThreadProcessId(new IntPtr(handle), out var pid);
            return pid == 0 ? null : (int)pid;
        }
        catch (Exception ex)
        {
            logger.Warn("取得 PowerPoint PID 失敗：" + ex.Message);
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
