using PptPngExporter.Core.Converters;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace PptPngExporter.Tests;

/// <summary>
/// 大量投影片與極端環境下的行為。
/// </summary>
public class LibreOfficeTimeoutTests
{
    private readonly ITestOutputHelper _output;
    public LibreOfficeTimeoutTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 逾時原本固定 5 分鐘，與投影片數量無關，300 張的簡報很容易撞到而整份失敗。
    /// </summary>
    [Fact]
    public void 檔案越大逾時越長()
    {
        var small = LibreOfficeConverter.TimeoutFor(1 * 1024 * 1024);
        var medium = LibreOfficeConverter.TimeoutFor(50L * 1024 * 1024);
        var large = LibreOfficeConverter.TimeoutFor(200L * 1024 * 1024);

        _output.WriteLine($"1 MB → {small.TotalMinutes:0.#} 分、50 MB → {medium.TotalMinutes:0.#} 分、200 MB → {large.TotalMinutes:0.#} 分");

        Assert.True(small < medium);
        Assert.True(medium < large);
    }

    [Fact]
    public void 再小的檔案也有基本寬限()
    {
        Assert.Equal(LibreOfficeConverter.BaseTimeout, LibreOfficeConverter.TimeoutFor(0));
        Assert.Equal(LibreOfficeConverter.BaseTimeout, LibreOfficeConverter.TimeoutFor(-1));
        Assert.True(LibreOfficeConverter.TimeoutFor(1024) >= LibreOfficeConverter.BaseTimeout);
    }

    /// <summary>沒有上限的話，真的卡死時會無限等待。</summary>
    [Fact]
    public void 再大的檔案也不會超過上限()
    {
        Assert.Equal(LibreOfficeConverter.MaxTimeout, LibreOfficeConverter.TimeoutFor(100L * 1024 * 1024 * 1024));
    }

    [Fact]
    public void 一份典型的三百張簡報會拿到比五分鐘更長的時間()
    {
        // 300 張含圖片的簡報大約 80–150 MB
        var timeout = LibreOfficeConverter.TimeoutFor(120L * 1024 * 1024);

        Assert.True(timeout > TimeSpan.FromMinutes(5),
            $"300 張的簡報只拿到 {timeout.TotalMinutes:0.#} 分鐘，和舊的固定值一樣。");
    }
}

public class DiskSpaceTests
{
    [Fact]
    public void 可用空間查得到()
    {
        var free = DiskSpace.GetAvailableFreeBytes(Path.GetTempPath());

        Assert.NotNull(free);
        Assert.True(free > 0);
    }

    [Fact]
    public void 查不到的路徑不會擲出例外()
    {
        Assert.Null(DiskSpace.GetAvailableFreeBytes(@"\\不存在的主機\分享區"));
    }

    [Theory]
    [InlineData(500L * 1024, "KB")]
    [InlineData(5L * 1024 * 1024, "MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "GB")]
    public void 大小以易讀的單位呈現(long bytes, string unit)
        => Assert.EndsWith(unit, DiskSpace.Describe(bytes));

    [Fact]
    public void 磁碟已滿的例外會被辨識出來()
    {
        var diskFull = new IOException("磁碟已滿") { HResult = unchecked((int)0x80070070) };
        var handleFull = new IOException("磁碟已滿") { HResult = unchecked((int)0x80070027) };

        Assert.True(DiskSpace.IsDiskFull(diskFull));
        Assert.True(DiskSpace.IsDiskFull(handleFull));
        Assert.True(DiskSpace.IsDiskFull(new InvalidOperationException("外層", diskFull)));
    }

    [Fact]
    public void 其他例外不會被誤判為磁碟已滿()
    {
        Assert.False(DiskSpace.IsDiskFull(new IOException("檔案被鎖住")));
        Assert.False(DiskSpace.IsDiskFull(new UnauthorizedAccessException()));
    }

    [Fact]
    public void 空間充足時不會回報問題()
    {
        var note = DiskSpace.Check(Path.GetTempPath(), out var blocking);

        // 測試機通常空間充足；重點是不會誤擋
        Assert.False(blocking);
        if (note is not null) Assert.Contains("可用空間", note);
    }
}

/// <summary>
/// 縮圖快取的清理。
///
/// 快取鍵包含簡報的最後修改時間與程式版本，所以每改一次簡報、每更新一次程式
/// 就會多出一整套快取，舊的永遠不會再被命中。沒有清理機制的話會無限累積。
/// </summary>
public class PreviewCacheSweepTests : IDisposable
{
    private readonly string _root;
    private readonly string _originalLocalAppData;

    public PreviewCacheSweepTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PptPngPreviewSweep-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);

        // SlidePreviewService.CacheRoot 是從 LocalApplicationData 算出來的
        _originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? string.Empty;
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _originalLocalAppData);
        try { Directory.Delete(_root, true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static string MakeCacheEntry(string cacheRoot, string name, int fileCount, int sizeEach, DateTime lastWriteUtc)
    {
        var dir = Path.Combine(cacheRoot, name);
        Directory.CreateDirectory(dir);

        for (var i = 0; i < fileCount; i++)
        {
            var file = Path.Combine(dir, $"{i:D4}.png");
            File.WriteAllBytes(file, new byte[sizeEach]);
            File.SetLastWriteTimeUtc(file, lastWriteUtc);
        }

        Directory.SetCreationTimeUtc(dir, lastWriteUtc);
        return dir;
    }

    [Fact]
    public void 過期的快取會被清掉而最近用過的會留下()
    {
        var cacheRoot = SlidePreviewService.CacheRoot;
        Directory.CreateDirectory(cacheRoot);

        var stale = MakeCacheEntry(cacheRoot, "powerpoint-舊的", 3, 1024,
            DateTime.UtcNow - SlidePreviewService.CacheMaxAge - TimeSpan.FromDays(1));

        var fresh = MakeCacheEntry(cacheRoot, "powerpoint-新的", 3, 1024, DateTime.UtcNow);

        var freed = SlidePreviewService.SweepCache();

        Assert.False(Directory.Exists(stale), "過期的快取應該被清掉。");
        Assert.True(Directory.Exists(fresh), "最近用過的快取不應該被動到。");
        Assert.True(freed > 0);
    }

    [Fact]
    public void 沒有快取資料夾時不會出錯()
    {
        Assert.Equal(0, SlidePreviewService.SweepCache());
    }

    [Fact]
    public void 全部都在有效期內時不會刪任何東西()
    {
        var cacheRoot = SlidePreviewService.CacheRoot;
        Directory.CreateDirectory(cacheRoot);

        var a = MakeCacheEntry(cacheRoot, "powerpoint-a", 2, 512, DateTime.UtcNow);
        var b = MakeCacheEntry(cacheRoot, "libreoffice-b", 2, 512, DateTime.UtcNow);

        Assert.Equal(0, SlidePreviewService.SweepCache());
        Assert.True(Directory.Exists(a));
        Assert.True(Directory.Exists(b));
    }
}
