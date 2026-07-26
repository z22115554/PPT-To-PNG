namespace PptPngExporter.Core.Converters;

/// <summary>
/// Office 應用程式層級設定的暫時覆寫。
///
/// 存在的理由：這些設定是<b>應用程式全域</b>的，不是單一文件的。借用使用者已開啟的
/// PowerPoint 時，若改了設定卻沒還原，使用者回到 PowerPoint 會發現安全性或警示行為被動過。
///
/// 刻意設計成不依賴 COM，讓「記錄原值 → 覆寫 → 逆序還原」這段邏輯可以被單元測試涵蓋。
/// </summary>
public sealed class OfficeSettingsGuard : IDisposable
{
    private readonly Func<string, int?> _read;
    private readonly Func<string, int, bool> _write;
    private readonly IAppLogger _logger;

    // 用堆疊確保還原順序與套用順序相反
    private readonly Stack<(string Name, int Original)> _applied = new();
    private bool _disposed;

    public OfficeSettingsGuard(Func<string, int?> read, Func<string, int, bool> write, IAppLogger? logger = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>已成功覆寫並待還原的設定數量。</summary>
    public int PendingRestoreCount => _applied.Count;

    /// <summary>
    /// 覆寫一項設定並記住原值。
    /// 讀不到原值時仍會嘗試覆寫，但不會登記還原（沒有原值可還原）。
    /// </summary>
    /// <returns>是否成功覆寫。</returns>
    public bool Apply(string name, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int? original;
        try
        {
            original = _read(name);
        }
        catch (Exception ex)
        {
            _logger.Warn($"讀取 {name} 失敗：{ex.Message}");
            original = null;
        }

        bool written;
        try
        {
            written = _write(name, value);
        }
        catch (Exception ex)
        {
            _logger.Warn($"設定 {name} 失敗：{ex.Message}");
            return false;
        }

        if (!written)
        {
            _logger.Warn($"設定 {name} 失敗（應用程式拒絕）。");
            return false;
        }

        // 原值與新值相同就不必登記還原
        if (original.HasValue && original.Value != value)
            _applied.Push((name, original.Value));

        return true;
    }

    /// <summary>還原所有已覆寫的設定。可重複呼叫。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        while (_applied.Count > 0)
        {
            var (name, original) = _applied.Pop();
            try
            {
                _write(name, original);
            }
            catch (Exception ex)
            {
                _logger.Warn($"還原 {name} 失敗：{ex.Message}");
            }
        }
    }
}

/// <summary>PowerPoint 自動化用得到的常數。</summary>
public static class PowerPointConstants
{
    /// <summary>ppAlertsNone：自動化過程中不要跳出警示視窗。</summary>
    public const int AlertsNone = 1;

    public const string DisplayAlerts = "DisplayAlerts";
    public const string AutomationSecurity = "AutomationSecurity";

    // MsoAutomationSecurity
    public const int AutomationSecurityLow = 1;          // 直接執行巨集（Office 自動化的預設值）
    public const int AutomationSecurityByUI = 2;         // 依使用者的巨集設定
    public const int AutomationSecurityForceDisable = 3; // 一律停用巨集

    // MsoTriState
    public const int MsoTrue = -1;
    public const int MsoFalse = 0;
}
