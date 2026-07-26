using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PptPngExporter.Core.Interop;

/// <summary>COM 呼叫失敗，並帶有失敗的成員名稱，方便從記錄檔判斷問題點。</summary>
public sealed class ComInvocationException : Exception
{
    public ComInvocationException(string memberName, int hResult, Exception inner)
        : base($"呼叫 COM 成員「{memberName}」失敗（0x{hResult:X8}）：{inner.Message}", inner)
    {
        MemberName = memberName;
        ComHResult = hResult;
    }

    public string MemberName { get; }
    public int ComHResult { get; }
}

/// <summary>
/// 以晚期繫結（IDispatch）操作 COM 物件的極小型包裝。
/// 這樣專案不需參考 Microsoft.Office.Interop.PowerPoint，也就不需要在建置機器上安裝 Office，
/// 而且能同時相容各種 Office 版本。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ComObject : IDisposable
{
    // PowerPoint 忙碌時常見的 HRESULT
    private const int RPC_E_CALL_REJECTED = unchecked((int)0x80010001);
    private const int RPC_E_SERVERCALL_RETRYLATER = unchecked((int)0x8001010A);
    private const int VBA_E_IGNORE = unchecked((int)0x800AC472);

    private object? _instance;
    private readonly bool _ownsInstance;

    private ComObject(object instance, bool ownsInstance)
    {
        _instance = instance;
        _ownsInstance = ownsInstance;
    }

    public object Instance => _instance ?? throw new ObjectDisposedException(nameof(ComObject));

    public static ComObject Wrap(object instance) => new(instance, ownsInstance: true);

    /// <summary>依 ProgID 建立（或取得既有的）COM 執行個體；找不到元件時回傳 null。</summary>
    public static ComObject? TryCreate(string progId)
    {
        var type = Type.GetTypeFromProgID(progId, throwOnError: false);
        if (type is null) return null;

        var instance = Activator.CreateInstance(type);
        return instance is null ? null : new ComObject(instance, ownsInstance: true);
    }

    /// <summary>檢查系統是否註冊了指定 ProgID（不會實際啟動程式）。</summary>
    public static bool IsRegistered(string progId) => Type.GetTypeFromProgID(progId, throwOnError: false) is not null;

    /// <summary>
    /// 取得成員並包成 <see cref="ComObject"/>。
    ///
    /// 同時帶上 GetProperty 與 InvokeMethod 兩個旗標：Office 型別庫裡有些成員宣告成屬性、
    /// 有些宣告成方法（例如 Slides.Item 是方法而不是屬性），只用單一旗標會得到
    /// DISP_E_MEMBERNOTFOUND (0x80020003)。
    /// </summary>
    public ComObject GetObject(string memberName, params object?[] args)
        => Wrap(InvokeCore(BindingFlags.GetProperty | BindingFlags.InvokeMethod, memberName, args)
                ?? throw new InvalidOperationException($"COM 成員 {memberName} 回傳 null。"));

    public T Get<T>(string memberName, params object?[] args)
        => (T)System.Convert.ChangeType(
            InvokeCore(BindingFlags.GetProperty | BindingFlags.InvokeMethod, memberName, args)
            ?? throw new InvalidOperationException($"COM 成員 {memberName} 回傳 null。"),
            typeof(T), CultureInfo.InvariantCulture);

    /// <summary>取得整數屬性；取不到時回傳 null 而不擲出例外。</summary>
    public int? TryGetInt(string memberName)
    {
        try { return Get<int>(memberName); }
        catch { return null; }
    }

    /// <summary>取得屬性值；取不到時回傳 fallback 而不擲出例外。</summary>
    public T GetOrDefault<T>(string memberName, T fallback)
    {
        try { return Get<T>(memberName); }
        catch { return fallback; }
    }

    public void Set(string memberName, object value)
        => InvokeCore(BindingFlags.SetProperty, memberName, new object?[] { value });

    /// <summary>設定屬性；失敗時安靜略過並回傳 false。</summary>
    public bool TrySet(string memberName, object value)
    {
        try { Set(memberName, value); return true; }
        catch { return false; }
    }

    public object? Call(string memberName, params object?[] args)
        => InvokeCore(BindingFlags.InvokeMethod, memberName, args);

    public ComObject CallObject(string memberName, params object?[] args)
        => Wrap(InvokeCore(BindingFlags.InvokeMethod, memberName, args)
                ?? throw new InvalidOperationException($"COM 方法 {memberName} 回傳 null。"));

    /// <summary>
    /// 呼叫 COM 成員，並在「伺服器忙碌」類錯誤時自動重試。
    /// PowerPoint 在開啟大型檔案或使用者正在操作時很容易短暫拒絕呼叫。
    /// </summary>
    private object? InvokeCore(BindingFlags flags, string name, object?[] args)
    {
        var target = Instance;
        const int maxAttempts = 40;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return target.GetType().InvokeMember(
                    name, flags | BindingFlags.Public | BindingFlags.Instance,
                    binder: null, target: target, args: args, culture: CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is COMException com && IsBusy(com) && attempt < maxAttempts)
            {
                Thread.Sleep(250);
            }
            catch (COMException com) when (IsBusy(com) && attempt < maxAttempts)
            {
                Thread.Sleep(250);
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                // 附上成員名稱，否則記錄檔只會看到「找不到成員」，無從判斷是哪一個
                throw new ComInvocationException(name, tie.InnerException.HResult, tie.InnerException);
            }
            catch (COMException com)
            {
                throw new ComInvocationException(name, com.HResult, com);
            }
            catch (MissingMemberException mme)
            {
                throw new ComInvocationException(name, unchecked((int)0x80020003), mme);
            }
        }
    }

    private static bool IsBusy(COMException com)
        => com.HResult is RPC_E_CALL_REJECTED or RPC_E_SERVERCALL_RETRYLATER or VBA_E_IGNORE;

    public void Dispose()
    {
        var obj = Interlocked.Exchange(ref _instance, null);
        if (obj is null || !_ownsInstance) return;

        try
        {
            if (Marshal.IsComObject(obj)) Marshal.FinalReleaseComObject(obj);
        }
        catch
        {
            // 釋放失敗不影響主流程
        }
    }
}
