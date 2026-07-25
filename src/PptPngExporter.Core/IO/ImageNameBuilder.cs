using PptPngExporter.Core.Models;

namespace PptPngExporter.Core.IO;

/// <summary>
/// 決定每張輸出圖片的檔名。
///
/// 兩種編號方式的差別，在挑選不連續的頁面時最明顯。
/// 例如挑選第 1、5、7 張：
///   連續編號（預設）→ 投影片_001、投影片_002、投影片_003
///   原始頁碼        → 投影片_001、投影片_005、投影片_007
/// </summary>
public sealed class ImageNameBuilder
{
    private readonly string _prefix;
    private readonly FileNumbering _numbering;

    public ImageNameBuilder(string prefix, FileNumbering numbering, int digitsSetting, IReadOnlyList<int> pages)
    {
        _prefix = prefix ?? string.Empty;
        _numbering = numbering;

        // 決定補零位數所依據的「最大數字」會隨編號方式不同：
        // 連續編號看的是總張數，原始頁碼看的是最大頁碼。
        var largest = pages.Count == 0
            ? 1
            : numbering == FileNumbering.Sequential ? pages.Count : pages[^1];

        Digits = digitsSetting > 0
            ? digitsSetting
            : Math.Max(2, largest.ToString().Length);
    }

    /// <summary>實際使用的補零位數。</summary>
    public int Digits { get; }

    /// <summary>
    /// 組出不含副檔名的圖片檔名。
    /// </summary>
    /// <param name="ordinal">這是本次輸出的第幾張（從 1 起算）。</param>
    /// <param name="pageNumber">在原簡報中的頁碼（從 1 起算）。</param>
    public string Build(int ordinal, int pageNumber)
    {
        var number = _numbering == FileNumbering.Sequential ? ordinal : pageNumber;

        // 位數設定小於實際數字時不截斷，只是不再補零
        var text = number.ToString().PadLeft(Digits, '0');

        return _prefix.Length == 0 ? text : _prefix + text;
    }
}
