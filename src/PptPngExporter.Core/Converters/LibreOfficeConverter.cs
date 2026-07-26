using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using PDFtoImage;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Services;
using SkiaSharp;

namespace PptPngExporter.Core.Converters;

/// <summary>
/// 沒有 PowerPoint（或 PowerPoint 失敗）時使用。
/// 流程：LibreOffice 以無介面模式把簡報轉成 PDF，再用內建的 PDFium 把指定頁面算繪成 PNG。
/// 之所以不直接用 LibreOffice 匯出 PNG，是因為它只會輸出第一頁，且無法指定寬度。
/// </summary>
public sealed class LibreOfficeConverter : ISlideConverter
{
    private readonly IAppLogger _logger;
    private readonly TimeSpan _timeout;
    private bool? _available;

    public LibreOfficeConverter(IAppLogger? logger = null, TimeSpan? timeout = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public ConversionEngine Engine => ConversionEngine.LibreOffice;
    public string DisplayName => "LibreOffice";
    public string? UnavailableReason { get; private set; }

    /// <summary>清除偵測快取，讓使用者安裝 LibreOffice 之後不必重開程式。</summary>
    public void ResetAvailability()
    {
        _available = null;
        UnavailableReason = null;
        LibreOfficeLocator.ResetCache();
    }

    public bool IsAvailable()
    {
        if (_available.HasValue) return _available.Value;

        var path = LibreOfficeLocator.Find();
        if (path is null)
            UnavailableReason = "這台電腦沒有偵測到 LibreOffice。請安裝後再試，或設定環境變數 LIBREOFFICE_PATH 指向 soffice.exe。";

        return (_available = path is not null).Value;
    }

    public IReadOnlyList<string> Convert(ConversionRequest request, IProgress<SlideProgress>? progress, CancellationToken cancellationToken)
    {
        var soffice = LibreOfficeLocator.Find()
                      ?? throw new ConversionException(UnavailableReason ?? "找不到 LibreOffice。");

        // 每次都用獨立的暫存設定檔目錄：避免和使用者已開啟的 LibreOffice 互搶設定檔鎖，
        // 也確保結束後不會留下常駐的快速啟動程序。
        var workDir = Path.Combine(Path.GetTempPath(), "PptPngExporter", Guid.NewGuid().ToString("N"));
        var profileDir = Path.Combine(workDir, "profile");
        var pdfDir = Path.Combine(workDir, "pdf");

        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(pdfDir);

        // 用 Job Object 綁住我們啟動的 soffice 及其子程序（soffice.bin）。
        // 這樣不必掃描程序名稱，也就不可能誤殺使用者在轉檔期間自己開啟的 LibreOffice。
        var job = WindowsJobObject.TryCreate(_logger);
        var owned = new OwnedProcessGuard(_logger);

        try
        {
            var pdfPath = ConvertToPdf(soffice, request.SourcePath, pdfDir, profileDir, job, owned, cancellationToken);
            return Rasterize(pdfPath, request, progress, cancellationToken);
        }
        finally
        {
            // 先給登記過的程序一點時間自行結束，再由 Job 做最後保證
            owned.KillSurvivors(TimeSpan.FromSeconds(6));
            job?.Dispose();
            TryDeleteDirectory(workDir);
        }
    }

    private string ConvertToPdf(
        string soffice, string sourcePath, string pdfDir, string profileDir,
        WindowsJobObject? job, OwnedProcessGuard owned, CancellationToken cancellationToken)
    {
        var fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(LongPath.Extended(fullSource)))
            throw new ConversionException("找不到來源檔案，可能已被移動或刪除。");

        // LibreOffice 對超長路徑與部分中文路徑的處理不穩定，統一複製到短暫存路徑再轉換。
        var stagedSource = Path.Combine(pdfDir, "input" + Path.GetExtension(fullSource));
        File.Copy(LongPath.Extended(fullSource), stagedSource, overwrite: true);

        var startInfo = new ProcessStartInfo
        {
            FileName = soffice,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = pdfDir
        };

        foreach (var arg in new[]
                 {
                     $"-env:UserInstallation={ToFileUri(profileDir)}",
                     "--headless", "--invisible", "--nologo", "--nofirststartwizard",
                     "--norestore", "--nolockcheck", "--nodefault",
                     "--convert-to", "pdf:impress_pdf_Export",
                     "--outdir", pdfDir,
                     stagedSource
                 })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new ConversionException("無法啟動 LibreOffice：" + ex.Message, ex);
        }

        // 盡快納入管控：之後由這個程序產生的子程序會自動屬於同一個 Job
        job?.TryAssign(process);
        owned.Track(process);

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow + _timeout;
        while (!process.WaitForExit(200))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                KillTree(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (DateTime.UtcNow > deadline)
            {
                KillTree(process);
                throw new ConversionException($"LibreOffice 轉換超過 {_timeout.TotalMinutes:0} 分鐘仍未完成，已中止這個檔案。");
            }
        }

        process.WaitForExit();

        var produced = Directory.EnumerateFiles(pdfDir, "*.pdf").FirstOrDefault();
        if (produced is null)
        {
            var detail = stderr.Length > 0 ? stderr.ToString().Trim() : stdout.ToString().Trim();
            _logger.Error("LibreOffice 沒有輸出 PDF。" + detail);
            throw new ConversionException(
                "LibreOffice 無法讀取這份簡報，檔案可能已損毀、受密碼保護，或格式不受支援。" +
                (string.IsNullOrEmpty(detail) ? string.Empty : $"（{Shorten(detail)}）"));
        }

        return produced;
    }

    [SuppressMessage("Interoperability", "CA1416", Justification = "本程式只在 Windows 上執行，PDFium 在 Windows 為支援平台。")]
    private static IReadOnlyList<string> Rasterize(string pdfPath, ConversionRequest request, IProgress<SlideProgress>? progress, CancellationToken cancellationToken)
    {
        var width = ExportOptions.ClampWidth(request.ImageWidth);

        using var pdfStream = File.OpenRead(pdfPath);

        int totalPages;
        try
        {
            totalPages = Conversion.GetPageCount(pdfStream, leaveOpen: true);
        }
        catch (Exception ex)
        {
            throw new ConversionException("無法讀取轉換後的 PDF：" + ex.Message, ex);
        }

        if (totalPages <= 0) throw new ConversionException("這份簡報沒有任何投影片。");

        var pages = request.Pages.Resolve(totalPages);
        if (pages.Count == 0)
            throw new ConversionException($"指定的頁碼超出範圍，這份簡報只有 {totalPages} 頁。");

        LongPath.EnsureDirectory(request.OutputDirectory);

        var options = new RenderOptions
        {
            Width = width,
            WithAspectRatio = true,
            WithAnnotations = true,
            WithFormFill = true,
            AntiAliasing = PdfAntiAliasing.All,
            BackgroundColor = SKColors.White
        };

        var written = new List<string>(pages.Count);
        var naming = new ImageNameBuilder(request.FileNamePrefix, request.Numbering, request.NumberDigits, pages);
        var done = 0;
        progress?.Report(new SlideProgress(0, pages.Count));

        foreach (var pageNumber in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var baseName = naming.Build(done + 1, pageNumber);
            var target = UniquePathResolver.ResolveFile(request.OutputDirectory, baseName, ".png");

            pdfStream.Position = 0;
            using (var bitmap = Conversion.ToImage(pdfStream, page: pageNumber - 1, leaveOpen: true, password: null, options: options))
            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var output = new FileStream(LongPath.Extended(target), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                data.SaveTo(output);
            }

            written.Add(target);
            // 注意：計數必須獨立一行。寫成 progress?.Report(new SlideProgress(++done, ...))
            // 時，progress 為 null 會讓整個引數不被求值，++done 永遠不會執行。
            done++;
            progress?.Report(new SlideProgress(done, pages.Count));
        }

        return written;
    }

    private void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            _logger.Warn("中止 LibreOffice 程序時發生問題：" + ex.Message);
        }
    }

    private static string ToFileUri(string directory)
        => new Uri(Path.GetFullPath(directory) + Path.DirectorySeparatorChar).AbsoluteUri.TrimEnd('/');

    private static string Shorten(string text)
    {
        var single = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= 160 ? single : single[..160] + "…";
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(300);
            }
        }
    }
}
