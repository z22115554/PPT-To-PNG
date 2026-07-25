using System.Text;

namespace PptPngExporter.Core.IO;

/// <summary>
/// 將任意文字轉為 Windows 可用的檔名／資料夾名稱。
/// 會保留中文、日文等非 ASCII 文字，只處理真正不合法的部分。
/// </summary>
public static class FileNameSanitizer
{
    /// <summary>Windows 保留的裝置名稱，不能當作檔名主檔名使用。</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    // 明確列出，不使用 Path.GetInvalidFileNameChars()：在非 Windows 平台上該清單較短，
    // 會導致行為隨執行環境改變，也讓測試無法跨平台重現。
    private static readonly char[] IllegalChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    public const int DefaultMaxLength = 100;

    /// <summary>
    /// 產生安全檔名／資料夾名稱。
    /// </summary>
    /// <param name="raw">原始文字。</param>
    /// <param name="fallback">清理後為空時使用的預設名稱。</param>
    /// <param name="maxLength">最大長度（UTF-16 字元數）。</param>
    public static string Sanitize(string? raw, string fallback = "未命名", int maxLength = DefaultMaxLength)
    {
        if (maxLength < 1) maxLength = 1;

        var sb = new StringBuilder((raw ?? string.Empty).Length);
        var lastWasUnderscore = false;

        foreach (var ch in raw ?? string.Empty)
        {
            var isIllegal = char.IsControl(ch)
                            || Array.IndexOf(IllegalChars, ch) >= 0
                            || ch == '\u0000';

            if (isIllegal)
            {
                // 連續的非法字元合併成單一底線，避免產生 "a____b"
                if (!lastWasUnderscore) { sb.Append('_'); lastWasUnderscore = true; }
                continue;
            }

            // 各種全形／不斷行空白統一成一般空白
            var c = char.IsWhiteSpace(ch) ? ' ' : ch;
            sb.Append(c);
            lastWasUnderscore = false;
        }

        var name = sb.ToString();

        // 壓縮多餘空白
        while (name.Contains("  ", StringComparison.Ordinal))
            name = name.Replace("  ", " ", StringComparison.Ordinal);

        // Windows 不允許結尾為空白或句點
        name = name.Trim().TrimEnd('.', ' ').Trim();
        name = name.TrimStart('.', ' ').Trim();

        if (name.Length > maxLength)
        {
            name = name[..maxLength].TrimEnd('.', ' ', '_').Trim();
        }

        // 若清理後只剩下佔位底線（例如原文是 "///"），視同空白並改用預設名稱
        if (name.Length == 0 || name.Trim('_', ' ', '.', '-').Length == 0)
        {
            if (string.IsNullOrWhiteSpace(fallback)) return string.Empty;
            var fb = Sanitize(fallback, string.Empty, maxLength);
            return fb.Length > 0 ? fb : "未命名";
        }

        // 保留裝置名稱（含 "CON.txt" 這種形式）需要改名
        var stem = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        if (ReservedNames.Contains(stem))
            name = "_" + name;

        return name;
    }

    /// <summary>
    /// 清理使用者輸入的檔名前綴。允許空字串（代表不加前綴）。
    /// </summary>
    public static string SanitizePrefix(string? raw, int maxLength = 60)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        // fallback 傳空字串：清理後若無有效內容就當作「不加前綴」
        return Sanitize(raw, fallback: string.Empty, maxLength: maxLength);
    }

    /// <summary>
    /// 依據總頁數決定補零位數，並組出圖片檔名（不含副檔名）。
    /// </summary>
    public static string BuildImageName(string prefix, int pageNumber, int maxPageNumber)
    {
        var digits = Math.Max(2, maxPageNumber.ToString().Length);
        var number = pageNumber.ToString().PadLeft(digits, '0');
        return string.IsNullOrEmpty(prefix) ? number : prefix + number;
    }
}
