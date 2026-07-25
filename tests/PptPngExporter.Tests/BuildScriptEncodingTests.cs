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
        Assert.Contains("建置安裝版.bat", names);
        Assert.Contains("建置免安裝版.bat", names);

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.Contains("ExecutionPolicy Bypass", content);
            Assert.Contains("%~dp0", content);   // 必須用腳本自身位置，否則從別的目錄雙擊會失敗
        }
    }
}
