using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace PptPngExporter.Tests;

/// <summary>
/// 建置腳本的編碼規則。
///
/// 由來：<c>publish-installer-payload.ps1</c> 曾經以「UTF-8 不含 BOM」儲存，
/// 在繁體中文 Windows 上，Windows PowerShell 5.1（powershell.exe）會改用系統 ANSI
/// 編碼（CP950／Big5）讀取，中文註解變成亂碼，其中的位元組破壞了字串引號，
/// 產生一連串看似無關的語法錯誤。Inno Setup 6 讀取 .iss 也是同樣規則。
///
/// 相對地，.bat 絕對不能加 BOM —— cmd.exe 會把 BOM 位元組當成第一個指令的一部分。
/// </summary>
public class BuildScriptEncodingTests
{
    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    private readonly ITestOutputHelper _output;

    public BuildScriptEncodingTests(ITestOutputHelper output) => _output = output;

    /// <summary>從測試組件位置往上找到含有方案檔的原始碼根目錄。</summary>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PptPngExporter.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private string[] BuildFiles(string searchPattern)
    {
        var root = FindRepositoryRoot();
        if (root is null)
        {
            _output.WriteLine("略過：找不到原始碼根目錄（可能是從發佈輸出執行測試）。");
            return Array.Empty<string>();
        }

        var buildDir = Path.Combine(root, "build");
        if (!Directory.Exists(buildDir))
        {
            _output.WriteLine("略過：找不到 build 資料夾。");
            return Array.Empty<string>();
        }

        return Directory.GetFiles(buildDir, searchPattern);
    }

    [Theory]
    [InlineData("*.ps1")]
    [InlineData("*.iss")]
    public void PowerShell與InnoSetup腳本必須含UTF8BOM(string pattern)
    {
        var files = BuildFiles(pattern);
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            var head = new byte[3];
            using (var stream = File.OpenRead(file))
            {
                var read = stream.Read(head, 0, 3);
                Assert.True(read == 3, $"{Path.GetFileName(file)} 太短。");
            }

            _output.WriteLine($"{Path.GetFileName(file)}：{BitConverter.ToString(head)}");

            Assert.True(head.SequenceEqual(Utf8Bom),
                $"{Path.GetFileName(file)} 缺少 UTF-8 BOM。" +
                "Windows PowerShell 5.1 與 Inno Setup 在沒有 BOM 時會用系統 ANSI 編碼讀取，" +
                "繁體中文 Windows 上會造成亂碼與語法錯誤。");
        }
    }

    /// <summary>
    /// 批次檔一律純 ASCII。
    ///
    /// 由來：先前的 .bat 含中文並以 chcp 65001 切換主控台編碼。雖然當時能運作，
    /// 但批次檔的編碼處理是踩過雷的地方（.ps1 缺少 BOM 曾造成整批語法錯誤），
    /// 因此改為所有在地化訊息都由 PowerShell 輸出，批次檔本身不含任何非 ASCII 字元，
    /// 從根本上排除編碼問題。
    /// </summary>
    [Fact]
    public void 批次檔必須是純ASCII()
    {
        var files = BuildFiles("*.bat");
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var nonAscii = bytes.Where(b => b > 0x7F).ToArray();

            _output.WriteLine($"{Path.GetFileName(file)}：{bytes.Length} bytes，非 ASCII {nonAscii.Length} 個");

            Assert.True(nonAscii.Length == 0,
                $"{Path.GetFileName(file)} 含有非 ASCII 位元組，中文訊息請改由 PowerShell 腳本輸出。");
        }
    }

    /// <summary>批次檔檔名本身也必須是 ASCII，避免解壓縮工具弄壞檔名而無法雙擊。</summary>
    [Fact]
    public void 批次檔的檔名必須是ASCII()
    {
        var files = BuildFiles("*.bat");
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            Assert.True(name.All(char.IsAscii), $"批次檔名 {name} 含有非 ASCII 字元。");
        }
    }

    [Fact]
    public void 版本號只在一個地方定義()
    {
        var root = FindRepositoryRoot();
        if (root is null) return;

        var iss = Path.Combine(root, "build", "installer.iss");
        if (!File.Exists(iss)) return;

        var content = File.ReadAllText(iss);

        // installer.iss 不可以再寫死版本號，必須由建置腳本以 /DAppVersion 傳入
        Assert.Contains("#ifndef AppVersion", content);

        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var match = System.Text.RegularExpressions.Regex.Match(props, @"<Version>([^<]+)</Version>");
        Assert.True(match.Success, "Directory.Build.props 必須有 <Version>");
        _output.WriteLine("唯一版本來源：" + match.Groups[1].Value);
    }

    [Fact]
    public void 批次檔絕對不可以有BOM()
    {
        var files = BuildFiles("*.bat");
        if (files.Length == 0) return;

        foreach (var file in files)
        {
            var head = new byte[3];
            using (var stream = File.OpenRead(file))
            {
                _ = stream.Read(head, 0, 3);
            }

            Assert.False(head.SequenceEqual(Utf8Bom),
                $"{Path.GetFileName(file)} 不應該有 BOM —— cmd.exe 會把 BOM 位元組當成指令的一部分。");
        }
    }

    [Theory]
    [InlineData("*.ps1")]
    [InlineData("*.iss")]
    [InlineData("*.bat")]
    public void 建置腳本必須是合法的UTF8(string pattern)
    {
        var files = BuildFiles(pattern);
        if (files.Length == 0) return;

        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var payload = bytes.Length >= 3 && bytes.Take(3).SequenceEqual(Utf8Bom) ? bytes.Skip(3).ToArray() : bytes;

            var exception = Record.Exception(() => strict.GetString(payload));

            Assert.True(exception is null, $"{Path.GetFileName(file)} 不是合法的 UTF-8：{exception?.Message}");
        }
    }

    [Fact]
    public void 一鍵批次檔存在且指向正確的腳本()
    {
        var files = BuildFiles("*.bat");
        if (files.Length == 0) return;

        var names = files.Select(Path.GetFileName).ToArray();
        Assert.Contains("build-installer.bat", names);
        Assert.Contains("build-portable.bat", names);

        var root = FindRepositoryRoot()!;

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.Contains("ExecutionPolicy Bypass", content);
            Assert.Contains("%~dp0", content);   // 必須用腳本自身位置，否則從別的目錄雙擊會失敗

            // 批次檔指向的 .ps1 必須真的存在。
            // 先把 %~dp0 換成分隔符號，否則會match到 "dp0publish-....ps1"。
            var normalized = content.Replace("%~dp0", "/");
            var referenced = System.Text.RegularExpressions.Regex.Matches(normalized, @"[\w\-]+\.ps1")
                .Select(m => m.Value).Distinct();

            foreach (var script in referenced)
                Assert.True(File.Exists(Path.Combine(root, "build", script)), $"{Path.GetFileName(file)} 指向不存在的腳本 {script}");
        }
    }
}
