using PptPngExporter.Core.Models;

namespace PptPngExporter.Core.Converters;

/// <summary>
/// 「目前的引擎設定能不能開始轉換」的單一判斷來源。
///
/// 介面用它決定是否啟用開始按鈕，批次服務用它產生錯誤訊息。
/// 兩邊共用同一份規則，避免出現「按鈕可以按，但每個檔案都失敗」這種矛盾。
/// </summary>
public static class EngineAvailability
{
    /// <summary>目前設定是否有可用的引擎。</summary>
    public static bool CanRun(EnginePreference preference, bool hasPowerPoint, bool hasLibreOffice)
        => preference switch
        {
            EnginePreference.PowerPointOnly => hasPowerPoint,
            EnginePreference.LibreOfficeOnly => hasLibreOffice,
            _ => hasPowerPoint || hasLibreOffice
        };

    /// <summary>
    /// 不能開始時的說明；可以開始時回傳 null。
    /// 訊息會直接顯示給使用者，所以要具體說出「該怎麼辦」。
    /// </summary>
    public static string? DescribeBlocker(EnginePreference preference, bool hasPowerPoint, bool hasLibreOffice)
    {
        if (CanRun(preference, hasPowerPoint, hasLibreOffice)) return null;

        return preference switch
        {
            EnginePreference.PowerPointOnly =>
                "目前設定為「只用 PowerPoint」，但這台電腦沒有偵測到 PowerPoint。" +
                (hasLibreOffice
                    ? "請改選「自動」或「只用 LibreOffice」。"
                    : "請安裝 PowerPoint 或 LibreOffice。"),

            EnginePreference.LibreOfficeOnly =>
                "目前設定為「只用 LibreOffice」，但這台電腦沒有偵測到 LibreOffice。" +
                (hasPowerPoint
                    ? "請改選「自動」或「只用 PowerPoint」。"
                    : "請安裝 LibreOffice 或 PowerPoint。"),

            _ => "找不到可用的轉換方式，請安裝 Microsoft PowerPoint 或 LibreOffice。"
        };
    }

    /// <summary>依偏好排出引擎的嘗試順序。</summary>
    public static IReadOnlyList<T> Order<T>(IEnumerable<T> converters, EnginePreference preference)
        where T : ISlideConverter
        => preference switch
        {
            EnginePreference.PowerPointOnly => converters.Where(c => c.Engine == ConversionEngine.PowerPoint).ToList(),
            EnginePreference.LibreOfficeOnly => converters.Where(c => c.Engine == ConversionEngine.LibreOffice).ToList(),
            _ => converters
                .OrderBy(c => c.Engine == ConversionEngine.PowerPoint ? 0 : c.Engine == ConversionEngine.LibreOffice ? 1 : 2)
                .ToList()
        };
}
