using PptPngExporter.Core.IO;
using Xunit;

namespace PptPngExporter.Tests;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("2025年度簡報", "2025年度簡報")]
    [InlineData("報告 v2", "報告 v2")]
    [InlineData("A_B-C", "A_B-C")]
    public void 合法名稱維持原樣(string input, string expected)
        => Assert.Equal(expected, FileNameSanitizer.Sanitize(input));

    [Fact]
    public void 非法字元被替換成底線()
        => Assert.Equal("公司_季報_", FileNameSanitizer.Sanitize("公司/季報?"));

    [Fact]
    public void 連續非法字元只會產生一個底線()
        => Assert.Equal("a_b", FileNameSanitizer.Sanitize("a<<>>|b"));

    [Fact]
    public void 移除控制字元()
        => Assert.Equal("ab", FileNameSanitizer.Sanitize("a\u0001\u0002b").Replace("_", ""));

    [Theory]
    [InlineData("報告.")]
    [InlineData("報告 ")]
    [InlineData("報告...")]
    [InlineData(" 報告")]
    public void 去除頭尾的空白與句點(string input)
        => Assert.Equal("報告", FileNameSanitizer.Sanitize(input));

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("CON.pptx")]
    public void Windows保留裝置名稱會被改寫(string input)
    {
        var result = FileNameSanitizer.Sanitize(input);
        Assert.StartsWith("_", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("///")]
    [InlineData("...")]
    public void 清理後為空時使用預設名稱(string? input)
        => Assert.Equal("簡報", FileNameSanitizer.Sanitize(input, "簡報"));

    [Fact]
    public void 過長的名稱會被截斷()
    {
        var longName = new string('葉', 500);

        var result = FileNameSanitizer.Sanitize(longName, "簡報", maxLength: 100);

        Assert.Equal(100, result.Length);
    }

    [Fact]
    public void 截斷後不會留下結尾句點()
    {
        var input = new string('a', 99) + "..." + new string('b', 50);

        var result = FileNameSanitizer.Sanitize(input, "簡報", maxLength: 100);

        Assert.DoesNotContain(result[^1], ". ");
    }

    [Fact]
    public void 中文與空白路徑名稱可正常處理()
        => Assert.Equal("我的 簡報 檔案", FileNameSanitizer.Sanitize("我的  簡報   檔案"));

    [Fact]
    public void Emoji與日文獲得保留()
        => Assert.Equal("スライド📊", FileNameSanitizer.Sanitize("スライド📊"));

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void 空前綴代表不加前綴(string? input, string expected)
        => Assert.Equal(expected, FileNameSanitizer.SanitizePrefix(input));

    [Fact]
    public void 前綴會被清理但保留中文()
        => Assert.Equal("投影片_", FileNameSanitizer.SanitizePrefix("投影片_"));

    [Fact]
    public void 前綴中的非法字元被移除()
        => Assert.DoesNotContain('/', FileNameSanitizer.SanitizePrefix("a/b"));

    [Theory]
    [InlineData("投影片_", 3, 9, "投影片_03")]
    [InlineData("投影片_", 3, 120, "投影片_003")]
    [InlineData("", 7, 12, "07")]
    [InlineData("p", 100, 1200, "p0100")]
    public void 圖片檔名依總頁數補零(string prefix, int page, int max, string expected)
        => Assert.Equal(expected, FileNameSanitizer.BuildImageName(prefix, page, max));
}

public class UniquePathResolverTests : IDisposable
{
    private readonly string _root;

    public UniquePathResolverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PptPngExporterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void 資料夾不存在時直接使用原名()
    {
        var result = UniquePathResolver.ResolveDirectory(_root, "我的簡報");

        Assert.Equal(Path.Combine(_root, "我的簡報"), result);
        Assert.False(Directory.Exists(result));
    }

    [Fact]
    public void 資料夾已存在時附加編號而不覆蓋()
    {
        Directory.CreateDirectory(Path.Combine(_root, "我的簡報"));

        var result = UniquePathResolver.ResolveDirectory(_root, "我的簡報");

        Assert.Equal(Path.Combine(_root, "我的簡報 (2)"), result);
    }

    [Fact]
    public void 多次呼叫會持續遞增編號()
    {
        for (var i = 1; i <= 4; i++)
        {
            var path = UniquePathResolver.ResolveDirectory(_root, "簡報");
            Directory.CreateDirectory(path);
        }

        Assert.True(Directory.Exists(Path.Combine(_root, "簡報")));
        Assert.True(Directory.Exists(Path.Combine(_root, "簡報 (2)")));
        Assert.True(Directory.Exists(Path.Combine(_root, "簡報 (3)")));
        Assert.True(Directory.Exists(Path.Combine(_root, "簡報 (4)")));
    }

    [Fact]
    public void 同名的檔案也會讓資料夾改名()
    {
        File.WriteAllText(Path.Combine(_root, "簡報"), "占位");

        var result = UniquePathResolver.ResolveDirectory(_root, "簡報");

        Assert.Equal(Path.Combine(_root, "簡報 (2)"), result);
    }

    [Fact]
    public void 圖片檔名衝突時附加編號()
    {
        File.WriteAllText(Path.Combine(_root, "投影片_01.png"), "x");

        var result = UniquePathResolver.ResolveFile(_root, "投影片_01", ".png");

        Assert.Equal(Path.Combine(_root, "投影片_01 (2).png"), result);
    }

    [Fact]
    public void 副檔名可省略前導點()
    {
        var withDot = UniquePathResolver.ResolveFile(_root, "a", ".png");
        var withoutDot = UniquePathResolver.ResolveFile(_root, "a", "png");

        Assert.Equal(withDot, withoutDot);
    }

    [Fact]
    public void 資料夾名稱中的非法字元會先被清理()
    {
        var result = UniquePathResolver.ResolveDirectory(_root, "2025/年報?");

        Assert.Equal(Path.Combine(_root, "2025_年報_"), result);
    }

    [Fact]
    public void 既有檔案不會被覆寫()
    {
        var first = UniquePathResolver.ResolveFile(_root, "圖", ".png");
        File.WriteAllText(first, "原始內容");

        var second = UniquePathResolver.ResolveFile(_root, "圖", ".png");
        File.WriteAllText(second, "新內容");

        Assert.NotEqual(first, second);
        Assert.Equal("原始內容", File.ReadAllText(first));
    }
}
