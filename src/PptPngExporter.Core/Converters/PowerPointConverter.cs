using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PptPngExporter.Core.Interop;
using PptPngExporter.Core.IO;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Services;
using static PptPngExporter.Core.Converters.PowerPointConstants;

namespace PptPngExporter.Core.Converters;

/// <summary>
/// 透過 COM 自動化呼叫本機 PowerPoint 匯出投影片。還原度最高。必須在 STA 執行緒上呼叫。
///
/// 三個安全性／穩定性要點：
/// 1. 開檔前一律把 AutomationSecurity 設為 ForceDisable，避免簡報夾帶的巨集被執行。
///    Office 自動化的預設是 msoAutomationSecurityLow，也就是「直接執行巨集」。
/// 2. 使用者已開著 PowerPoint 時只借用，結束時不關閉，並還原改動過的設定。
/// 3. 需要強制收尾時，只針對「由 Application.HWND 反查出來、確定是我們啟動的」那個 PID，
///    不做程序名稱掃描。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PowerPointConverter : ISlideConverter
{
    private const string ProgId = "PowerPoint.Application";

    private readonly IAppLogger _logger;
    private bool? _available;

    public PowerPointConverter(IAppLogger? logger = null) => _logger = logger ?? NullLogger.Instance;

    public ConversionEngine Engine => ConversionEngine.PowerPoint;
    public string DisplayName => "Microsoft PowerPoint";
    public string? UnavailableReason { get; private set; }

    public void ResetAvailability()
    {
        _available = null;
        UnavailableReason = null;
    }

    public bool IsAvailable()
    {
        if (_available.HasValue) return _available.Value;

        if (!OperatingSystem.IsWindows())
        {
            UnavailableReason = "PowerPoint 轉換只能在 Windows 上使用。";
            return (_available = false).Value;
        }

        try
        {
            var registered = ComObject.IsRegistered(ProgId);
            if (!registered) UnavailableReason = "這台電腦沒有偵測到 Microsoft PowerPoint。";
            return (_available = registered).Value;
        }
        catch (Exception ex)
        {
            UnavailableReason = "偵測 PowerPoint 時發生問題：" + ex.Message;
            return (_available = false).Value;
        }
    }

    /// <summary>
    /// 開始一批轉檔：整批共用同一個 PowerPoint 執行個體，避免每個檔案都冷啟動一次。
    /// 回傳的物件必須在同一條 STA 執行緒上釋放。
    /// </summary>
    public IDisposable? BeginBatch(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !IsAvailable()) return null;

        try
        {
            _batchSession = PowerPointSession.Create(_logger, cancellationToken);
            return new BatchScope(this);
        }
        catch (Exception ex)
        {
            // 開不起來就退回「每個檔案各自開一次」，由 Convert 去回報真正的錯誤
            _logger.Warn("無法建立共用的 PowerPoint 工作階段，將改為每個檔案各自啟動：" + ex.Message);
            _batchSession = null;
            return null;
        }
    }

    private PowerPointSession? _batchSession;

    private sealed class BatchScope : IDisposable
    {
        private readonly PowerPointConverter _owner;
        public BatchScope(PowerPointConverter owner) => _owner = owner;

        public void Dispose()
        {
            var session = _owner._batchSession;
            _owner._batchSession = null;
            session?.Dispose();
        }
    }

    public IReadOnlyList<string> Convert(ConversionRequest request, IProgress<SlideProgress>? progress, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new ConversionException("PowerPoint 轉換只能在 Windows 上使用。");

        // 整批共用的 session 若存在就沿用，否則自己開一個並負責關閉
        var session = _batchSession;
        var ownsSession = session is null;
        session ??= PowerPointSession.Create(_logger, cancellationToken);

        // PowerPoint 忙碌時每次呼叫最多重試 10 秒。掛上這次的權杖，按下停止才不必等重試跑完；
        // 由 App / Presentations 衍生出來的 Slides / Slide 會自動繼承。
        session.UseToken(cancellationToken);

        var policy = session.Policy;
        ComObject? presentation = null;
        var weOpenedPresentation = false;

        try
        {
            var fullPath = Path.GetFullPath(request.SourcePath);
            presentation = session.FindAlreadyOpen(fullPath);

            if (presentation is null)
            {
                presentation = session.Presentations.CallObject(
                    "Open",
                    fullPath,   // FileName
                    MsoTrue,    // ReadOnly
                    MsoFalse,   // Untitled
                    MsoFalse);  // WithWindow
                weOpenedPresentation = true;
            }
            else
            {
                _logger.Info($"{Path.GetFileName(fullPath)} 已在 PowerPoint 中開啟，直接沿用且不會關閉它。");
            }

            using var slides = presentation.GetObject("Slides");
            var totalSlides = slides.Get<int>("Count");

            if (totalSlides <= 0) throw new ConversionException("這份簡報沒有任何投影片。");

            var pages = request.Pages.Resolve(totalSlides);
            if (pages.Count == 0)
                throw new ConversionException($"指定的頁碼超出範圍，這份簡報只有 {totalSlides} 頁。");

            var (width, height) = ResolveSize(presentation, request.ImageWidth);

            LongPath.EnsureDirectory(request.OutputDirectory);

            var written = new List<string>(pages.Count);
            var naming = new ImageNameBuilder(request.FileNamePrefix, request.Numbering, request.NumberDigits, pages);
            var done = 0;
            progress?.Report(new SlideProgress(0, pages.Count));

            foreach (var pageNumber in pages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Slides.Item 在型別庫中是「方法」，必須用 InvokeMethod 呼叫；
                // 用屬性旗標會得到 DISP_E_MEMBERNOTFOUND (0x80020003)。
                using var slide = slides.CallObject("Item", pageNumber);

                var baseName = naming.Build(done + 1, pageNumber);
                var target = UniquePathResolver.ResolveFile(request.OutputDirectory, baseName, ".png");

                ExportSlide(slide, target, width, height);
                written.Add(target);

                // 計數必須獨立一行：progress 為 null 時，?. 會讓整個引數不被求值。
                done++;
                progress?.Report(new SlideProgress(done, pages.Count));
            }

            return written;
        }
        catch (OperationCanceledException) { throw; }
        catch (ConversionException) { throw; }
        catch (ComInvocationException com)
        {
            _logger.Error($"PowerPoint COM 呼叫失敗，成員：{com.MemberName}", com);
            throw new ConversionException(DescribeComError(com), com);
        }
        catch (COMException com)
        {
            throw new ConversionException($"PowerPoint 轉換失敗（錯誤碼 0x{com.HResult:X8}）：{com.Message}", com);
        }
        catch (Exception ex)
        {
            throw new ConversionException("PowerPoint 轉換失敗：" + ex.Message, ex);
        }
        finally
        {
            if (presentation is not null && policy.MayClosePresentation(weOpenedPresentation))
                SafeClose(presentation);

            presentation?.Dispose();

            // 整批共用時，Application 由 BatchScope 在整批結束後才釋放
            if (ownsSession) session.Dispose();
        }
    }

    private static void ExportSlide(ComObject slide, string targetPath, int width, int height)
    {
        if (!LongPath.IsLong(targetPath))
        {
            slide.Call("Export", targetPath, "PNG", width, height);
            if (!File.Exists(targetPath))
                throw new ConversionException("PowerPoint 沒有產生預期的圖片檔，可能是磁碟空間不足或沒有寫入權限。");
            return;
        }

        var stagingDir = Path.Combine(Path.GetTempPath(), "PptPngExporter", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(stagingDir);
        var stagingFile = Path.Combine(stagingDir, "slide.png");

        try
        {
            slide.Call("Export", stagingFile, "PNG", width, height);
            if (!File.Exists(stagingFile))
                throw new ConversionException("PowerPoint 沒有產生預期的圖片檔。");

            File.Move(LongPath.Extended(stagingFile), LongPath.Extended(targetPath), overwrite: false);
        }
        finally
        {
            try { Directory.Delete(stagingDir, recursive: true); } catch { }
        }
    }

    private static (int Width, int Height) ResolveSize(ComObject presentation, int requestedWidth)
    {
        var width = ExportOptions.ClampWidth(requestedWidth);
        var ratio = 9d / 16d;

        try
        {
            using var pageSetup = presentation.GetObject("PageSetup");
            var slideWidth = pageSetup.Get<double>("SlideWidth");
            var slideHeight = pageSetup.Get<double>("SlideHeight");
            if (slideWidth > 0 && slideHeight > 0) ratio = slideHeight / slideWidth;
        }
        catch { }

        var height = (int)Math.Round(width * ratio, MidpointRounding.AwayFromZero);
        return (width, Math.Max(1, height));
    }

    private void SafeClose(ComObject presentation)
    {
        try { presentation.Call("Close"); }
        catch (Exception ex) { _logger.Warn("關閉簡報時發生問題：" + ex.Message); }
    }

    private static string DescribeComError(ComInvocationException com) => (uint)com.ComHResult switch
    {
        0x80020003 => $"PowerPoint 不認得自動化指令「{com.MemberName}」。這通常代表 Office 版本較舊或安裝檔案損毀，請試著修復 Office，或改用 LibreOffice 轉換。",
        0x800A175D => "PowerPoint 目前正忙碌或有對話視窗開啟，請關閉後再試一次。",

        // 這三個是 COM「伺服器忙碌」類錯誤。ComObject 已經自動重試過 10 秒仍未成功，
        // 代表 PowerPoint 真的卡住了——最常見的原因是它跳出對話框在等使用者回應。
        0x80010001 or 0x8001010A or 0x800AC472 =>
            "PowerPoint 持續忙碌，重試 10 秒後仍無回應。請切到 PowerPoint 視窗看看是不是有對話框" +
            "（例如字型遺失、修復檔案的提示）在等待回應，處理完再試一次；或改用 LibreOffice 轉換。",

        0x80048240 => "PowerPoint 無法開啟這個檔案，檔案可能已損毀或格式不受支援。",
        0x80004005 => "PowerPoint 回報一般性錯誤，這個檔案可能已受密碼保護或損毀。",
        _ => $"PowerPoint 轉換失敗（{com.MemberName}，錯誤碼 0x{com.ComHResult:X8}）。"
    };
}
