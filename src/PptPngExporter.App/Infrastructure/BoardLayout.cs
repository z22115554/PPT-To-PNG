using System.Windows;

namespace PptPngExporter.App.Infrastructure;

/// <summary>
/// 挑選視窗的版面尺寸。抽成常數是為了讓版面計算與 WPF 完全脫鉤，方便測試。
/// </summary>
public sealed record BoardMetrics
{
    /// <summary>縮圖卡片（含右／下間距）佔用的格子寬度。</summary>
    public double CellWidth { get; init; } = 222;

    /// <summary>縮圖卡片（含右／下間距）佔用的格子高度。</summary>
    public double CellHeight { get; init; } = 148;

    /// <summary>群組標題列的高度。</summary>
    public double HeaderHeight { get; init; } = 88;

    /// <summary>群組之間的額外留白（第一組不套用）。</summary>
    public double HeaderTopGap { get; init; } = 14;

    public static BoardMetrics Default { get; } = new();
}

/// <summary>
/// 「群組標題 + 縮圖流式排列」的版面計算。
///
/// 為什麼需要這個：原本挑選視窗是 ItemsControl + WrapPanel 包在 ScrollViewer 裡，
/// 三者都不虛擬化，所有縮圖會一次全部具現化並解碼。單份 300 頁還撐得住，
/// 但視窗是依簡報分組的，10 份各 300 頁就是 3000 個視覺樹，開啟時會凍住好幾秒。
///
/// 虛擬化需要「不具現化元素也能算出每一項的位置」，這個類別就是負責這件事。
/// 版面規則很簡單：標題獨佔一整列，縮圖以固定格子大小由左至右流動、放不下就換行。
/// </summary>
public sealed class BoardLayout
{
    private readonly Rect[] _bounds;
    private readonly double[] _tops;

    /// <param name="isHeader">每一項是不是群組標題。</param>
    /// <param name="viewportWidth">可用寬度。小於一個格子寬時以一欄計算。</param>
    public BoardLayout(IReadOnlyList<bool> isHeader, double viewportWidth, BoardMetrics? metrics = null)
    {
        Metrics = metrics ?? BoardMetrics.Default;
        ViewportWidth = viewportWidth;

        var usableWidth = Math.Max(Metrics.CellWidth, double.IsFinite(viewportWidth) ? viewportWidth : Metrics.CellWidth);
        Columns = Math.Max(1, (int)Math.Floor(usableWidth / Metrics.CellWidth));

        _bounds = new Rect[isHeader.Count];
        _tops = new double[isHeader.Count];

        var y = 0d;
        var column = 0;
        var seenHeader = false;

        for (var i = 0; i < isHeader.Count; i++)
        {
            if (isHeader[i])
            {
                // 標題前先把未填滿的那一列結束掉
                if (column > 0)
                {
                    y += Metrics.CellHeight;
                    column = 0;
                }

                if (seenHeader) y += Metrics.HeaderTopGap;
                seenHeader = true;

                _bounds[i] = new Rect(0, y, usableWidth, Metrics.HeaderHeight);
                y += Metrics.HeaderHeight;
                continue;
            }

            _bounds[i] = new Rect(column * Metrics.CellWidth, y, Metrics.CellWidth, Metrics.CellHeight);

            if (++column >= Columns)
            {
                column = 0;
                y += Metrics.CellHeight;
            }
        }

        if (column > 0) y += Metrics.CellHeight;

        TotalHeight = y;

        for (var i = 0; i < _bounds.Length; i++) _tops[i] = _bounds[i].Top;
    }

    public BoardMetrics Metrics { get; }
    public double ViewportWidth { get; }
    public int Columns { get; }
    public int Count => _bounds.Length;
    public double TotalHeight { get; }

    public Rect GetBounds(int index) => _bounds[index];

    /// <summary>
    /// 取得與 [<paramref name="top"/>, <paramref name="bottom"/>] 有交集的項目索引範圍（含頭含尾）。
    /// 沒有任何項目時回傳 (0, -1)。
    /// </summary>
    public (int First, int Last) GetVisibleRange(double top, double bottom)
    {
        if (_bounds.Length == 0 || bottom < top) return (0, -1);

        // _tops 是非遞減的，可以二分搜尋
        var first = FirstIndexEndingAfter(top);
        if (first >= _bounds.Length) return (0, -1);

        var last = first;
        while (last + 1 < _bounds.Length && _tops[last + 1] <= bottom) last++;

        return (first, last);
    }

    /// <summary>找出第一個「底部超過 y」的項目。</summary>
    private int FirstIndexEndingAfter(double y)
    {
        // 先用 top 二分找到候選位置，再往回退，因為同一列的項目 top 相同
        var low = 0;
        var high = _bounds.Length - 1;
        var candidate = _bounds.Length;

        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (_tops[mid] >= y)
            {
                candidate = mid;
                high = mid - 1;
            }
            else
            {
                low = mid + 1;
            }
        }

        // 前一列可能仍有部分露在 y 之下
        var index = candidate;
        while (index > 0 && _bounds[index - 1].Bottom > y) index--;

        return index;
    }
}
