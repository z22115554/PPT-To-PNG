using PptPngExporter.App.Infrastructure;
using Xunit;

namespace PptPngExporter.Tests;

/// <summary>
/// 挑選視窗虛擬化面板的版面計算。
///
/// 這部分沒有辦法用眼睛驗證——面板只會具現化「算出來看得到」的項目，
/// 算錯的話畫面上不是空白就是重疊，而且要有幾百張縮圖才看得出來。
/// 因此把數學抽成純函式並在這裡涵蓋所有分支。
/// </summary>
public class BoardLayoutTests
{
    private static readonly BoardMetrics M = new()
    {
        CellWidth = 200,
        CellHeight = 100,
        HeaderHeight = 80,
        HeaderTopGap = 20
    };

    /// <summary>true = 群組標題。</summary>
    private static bool[] Board(string pattern) => pattern.Select(c => c == 'H').ToArray();

    [Theory]
    [InlineData(1000, 5)]
    [InlineData(999, 4)]
    [InlineData(400, 2)]
    [InlineData(200, 1)]
    [InlineData(150, 1)]   // 比一格還窄也至少要有一欄，否則會除以零
    [InlineData(0, 1)]
    public void 欄數依可用寬度計算(double width, int expected)
    {
        var layout = new BoardLayout(Board("T"), width, M);
        Assert.Equal(expected, layout.Columns);
    }

    [Fact]
    public void 縮圖由左至右流動並在填滿時換行()
    {
        // 3 欄，6 張縮圖 → 兩列
        var layout = new BoardLayout(Board("TTTTTT"), 600, M);

        Assert.Equal(3, layout.Columns);

        Assert.Equal(0, layout.GetBounds(0).X);
        Assert.Equal(200, layout.GetBounds(1).X);
        Assert.Equal(400, layout.GetBounds(2).X);
        Assert.Equal(0, layout.GetBounds(3).X);

        Assert.Equal(0, layout.GetBounds(0).Y);
        Assert.Equal(0, layout.GetBounds(2).Y);
        Assert.Equal(100, layout.GetBounds(3).Y);
        Assert.Equal(100, layout.GetBounds(5).Y);

        Assert.Equal(200, layout.TotalHeight);
    }

    [Fact]
    public void 標題獨佔整列且第一組沒有上方留白()
    {
        var layout = new BoardLayout(Board("HTT"), 600, M);

        var header = layout.GetBounds(0);
        Assert.Equal(0, header.Y);
        Assert.Equal(80, header.Height);
        Assert.Equal(600, header.Width);   // 佔滿寬度

        // 縮圖從標題底下開始
        Assert.Equal(80, layout.GetBounds(1).Y);
        Assert.Equal(0, layout.GetBounds(1).X);
    }

    [Fact]
    public void 第二組之後的標題有額外留白()
    {
        //  H  T T T   (一列剛好填滿)  H  T
        var layout = new BoardLayout(Board("HTTTHT"), 600, M);

        Assert.Equal(0, layout.GetBounds(0).Y);
        Assert.Equal(80, layout.GetBounds(1).Y);        // 第一組的縮圖列
        Assert.Equal(80 + 100 + 20, layout.GetBounds(4).Y);  // 標題列 + 縮圖列 + 群組間留白
    }

    [Fact]
    public void 未填滿的一列會在下個標題前先結束()
    {
        // 3 欄，第一組只有 2 張 → 那一列仍然佔滿一整格高度
        var layout = new BoardLayout(Board("HTTHT"), 600, M);

        Assert.Equal(80, layout.GetBounds(1).Y);
        Assert.Equal(80, layout.GetBounds(2).Y);

        // 標題 80 + 未滿的一列 100 + 群組間留白 20
        Assert.Equal(200, layout.GetBounds(3).Y);
        Assert.Equal(280, layout.GetBounds(4).Y);
    }

    [Fact]
    public void 總高度涵蓋最後一列()
    {
        var layout = new BoardLayout(Board("HTTTT"), 600, M);   // 3 欄 → 兩列
        Assert.Equal(80 + 100 + 100, layout.TotalHeight);
    }

    [Fact]
    public void 空清單不會炸掉()
    {
        var layout = new BoardLayout(Array.Empty<bool>(), 600, M);

        Assert.Equal(0, layout.Count);
        Assert.Equal(0, layout.TotalHeight);
        Assert.Equal((0, -1), layout.GetVisibleRange(0, 1000));
    }

    [Fact]
    public void 可視範圍只涵蓋有交集的項目()
    {
        // 3 欄、9 張縮圖 → 三列，y = 0 / 100 / 200
        var layout = new BoardLayout(Board("TTTTTTTTT"), 600, M);

        // 只看得到第一列
        Assert.Equal((0, 2), layout.GetVisibleRange(0, 99));

        // 第一列與第二列都有露出來
        Assert.Equal((0, 5), layout.GetVisibleRange(0, 100));

        // 捲到中間：第二列
        Assert.Equal((3, 5), layout.GetVisibleRange(100, 199));
    }

    /// <summary>
    /// 上緣切在一列中間時，那一列必須算進來——否則捲動時最上面會出現空白。
    /// </summary>
    [Fact]
    public void 上緣切在列中間時該列仍要算進來()
    {
        var layout = new BoardLayout(Board("TTTTTTTTT"), 600, M);

        var (first, last) = layout.GetVisibleRange(150, 250);

        Assert.Equal(3, first);   // 第二列（y=100，底部 200）仍有露出
        Assert.Equal(8, last);    // 第三列（y=200）也有露出
    }

    [Fact]
    public void 可視範圍完全在內容之外時回傳空範圍()
    {
        var layout = new BoardLayout(Board("TTT"), 600, M);
        Assert.Equal((0, -1), layout.GetVisibleRange(5000, 6000));
    }

    /// <summary>
    /// 大量項目時，可視範圍必須遠小於總數——這正是虛擬化的意義。
    /// </summary>
    [Fact]
    public void 三千張縮圖只需要具現化少數幾個()
    {
        var items = new bool[3000];
        for (var i = 0; i < items.Length; i += 301) items[i] = true;   // 每 300 張前面放一個標題

        var layout = new BoardLayout(items, 1000, M);   // 5 欄

        var (first, last) = layout.GetVisibleRange(0, 700);   // 約 7 列
        var realized = last - first + 1;

        Assert.True(realized < 60, $"可視範圍具現化了 {realized} 個項目，虛擬化沒有生效。");
        Assert.True(layout.TotalHeight > 60_000, "總高度應該涵蓋全部項目。");
    }

    [Fact]
    public void 每一項的位置都不重疊且依序遞增()
    {
        var layout = new BoardLayout(Board("HTTTTTHTTT"), 600, M);

        for (var i = 1; i < layout.Count; i++)
        {
            var previous = layout.GetBounds(i - 1);
            var current = layout.GetBounds(i);

            var sameRow = Math.Abs(previous.Y - current.Y) < 0.01;

            if (sameRow)
                Assert.True(current.X >= previous.Right - 0.01, $"第 {i} 項與前一項在同一列卻重疊。");
            else
                Assert.True(current.Y >= previous.Y, $"第 {i} 項的 Y 比前一項小。");
        }
    }
}
