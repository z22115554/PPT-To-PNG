using PptPngExporter.Core.Parsing;
using Xunit;

namespace PptPngExporter.Tests;

public class PageRangeParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 空白輸入視為全部頁面(string? input)
    {
        var spec = PageRangeParser.Parse(input);

        Assert.True(spec.IsAll);
        Assert.Equal(new[] { 1, 2, 3 }, spec.Resolve(3));
    }

    [Fact]
    public void 解析題目給的範例()
    {
        var spec = PageRangeParser.Parse("1-5,8,12-15");

        Assert.False(spec.IsAll);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 8, 12, 13, 14, 15 }, spec.Resolve(20));
    }

    [Fact]
    public void 單一頁碼()
        => Assert.Equal(new[] { 7 }, PageRangeParser.Parse("7").Resolve(10));

    [Fact]
    public void 重複與重疊的區間會合併且排序()
    {
        var spec = PageRangeParser.Parse("5,1-3,2-4,5,1");

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, spec.Resolve(10));
    }

    [Fact]
    public void 亂序輸入會排序()
        => Assert.Equal(new[] { 2, 6, 9 }, PageRangeParser.Parse("9,2,6").Resolve(10));

    [Fact]
    public void 顛倒的區間會自動修正()
        => Assert.Equal(new[] { 3, 4, 5, 6, 7, 8, 9 }, PageRangeParser.Parse("9-3").Resolve(20));

    [Fact]
    public void 開放結尾代表到最後一頁()
        => Assert.Equal(new[] { 4, 5, 6 }, PageRangeParser.Parse("4-").Resolve(6));

    [Fact]
    public void 開放起頭代表從第一頁開始()
        => Assert.Equal(new[] { 1, 2, 3 }, PageRangeParser.Parse("-3").Resolve(10));

    [Fact]
    public void 超出總頁數的部分會被裁切()
        => Assert.Equal(new[] { 8, 9, 10 }, PageRangeParser.Parse("8-99").Resolve(10));

    [Fact]
    public void 完全超出範圍時回傳空清單()
        => Assert.Empty(PageRangeParser.Parse("50-60").Resolve(10));

    [Theory]
    [InlineData("１-５,８")]          // 全形數字
    [InlineData("1－5，8")]           // 全形破折號與逗號
    [InlineData(" 1 - 5 , 8 ")]      // 多餘空白
    [InlineData("1~5、8")]            // 波浪號與頓號
    [InlineData("1-5,8,")]           // 尾端多餘逗號
    public void 常見的中文輸入習慣都能解析(string input)
        => Assert.Equal(new[] { 1, 2, 3, 4, 5, 8 }, PageRangeParser.Parse(input).Resolve(10));

    [Theory]
    [InlineData("abc")]
    [InlineData("1-a")]
    [InlineData("1--5")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("3.5")]
    [InlineData("999999999999")]
    public void 不合法的輸入會被拒絕並附上中文說明(string input)
    {
        var ok = PageRangeParser.TryParse(input, out var spec, out var error);

        Assert.False(ok);
        Assert.Null(spec);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Parse對不合法輸入擲出可顯示的例外()
    {
        var ex = Assert.Throws<PageRangeFormatException>(() => PageRangeParser.Parse("!!"));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void 總頁數為零時回傳空清單()
        => Assert.Empty(PageRangeSpec.All.Resolve(0));

    [Fact]
    public void 開放結尾與封閉區間混用()
        => Assert.Equal(new[] { 1, 2, 5, 6, 7 }, PageRangeParser.Parse("1-2,5-").Resolve(7));
}
