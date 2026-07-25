namespace PptPngExporter.Core.Converters;

/// <summary>
/// 決定轉檔結束時「可以動什麼、不可以動什麼」。
///
/// 背景：PowerPoint 是<b>單一執行個體</b>的 COM 伺服器。使用者已經開著 PowerPoint 時，
/// 建立 COM 物件並不會另外開一個，而是回傳使用者正在用的那一個。
/// 這種情況下呼叫 Quit() 等於幫使用者按下關閉，可能連同未存檔的簡報一起關掉。
///
/// 這個類別刻意設計成沒有任何 COM 相依，讓這個判斷可以被單元測試涵蓋。
/// </summary>
public sealed class PowerPointSessionPolicy
{
    public PowerPointSessionPolicy(bool powerPointWasAlreadyRunning)
    {
        AttachedToExistingInstance = powerPointWasAlreadyRunning;
    }

    /// <summary>我們是「借用」使用者已開啟的 PowerPoint，而不是自己啟動的。</summary>
    public bool AttachedToExistingInstance { get; }

    /// <summary>只有自己啟動的執行個體才可以結束。</summary>
    public bool MayQuitApplication => !AttachedToExistingInstance;

    /// <summary>只有自己啟動的執行個體才可以強制關閉殘留程序。</summary>
    public bool MayKillLeftoverProcesses => !AttachedToExistingInstance;

    /// <summary>
    /// 只有我們自己開啟的簡報才可以關閉。
    /// 使用者原本就開著這個檔案時，關掉它會讓使用者的工作消失。
    /// </summary>
    public bool MayClosePresentation(bool weOpenedIt) => weOpenedIt;

    /// <summary>
    /// 借用使用者的執行個體時，變更過的應用程式設定必須還原，
    /// 否則使用者回到 PowerPoint 會發現警示訊息被關掉了。
    /// </summary>
    public bool MustRestoreApplicationSettings => AttachedToExistingInstance;

    public string Describe() => AttachedToExistingInstance
        ? "借用使用者已開啟的 PowerPoint（結束時不會關閉它）"
        : "由本程式啟動 PowerPoint（結束時會一併關閉）";
}
