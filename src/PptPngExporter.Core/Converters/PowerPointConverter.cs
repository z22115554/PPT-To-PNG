using System.Diagnostics;
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

    public IReadOnlyList<string> Convert(ConversionRequest request, IProgress<SlideProgress>? progress, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new ConversionException("PowerPoint 轉換只能在 Windows 上使用。");

        var policy = new PowerPointSessionPolicy(IsPowerPointRunning());
        _logger.Info("PowerPoint 工作階段：" + policy.Describe());

        var guard = new OwnedProcessGuard(_logger);

        ComObject? app = null;
        ComObject? presentations = null;
        ComObject? presentation = null;
        OfficeSettingsGuard? settings = null;
        var weOpenedPresentation = false;

        try
        {
            app = ComObject.TryCreate(ProgId)
                  ?? throw new ConversionException("無法啟動 PowerPoint，請確認 Office 安裝是否正常。");

            var comApp = app;
            settings = new OfficeSettingsGuard(
                read: name => comApp.TryGetInt(name),
                write: (name, value) => comApp.TrySet(name, value),
                logger: _logger);

            // 這一行必須在開啟任何外部簡報「之前」執行。
            // Office 自動化的巨集安全性預設是 msoAutomationSecurityLow（直接執行巨集），
            // 使用者拖進來的 .ppt / .pps 可能夾帶巨集。
            if (!settings.Apply(AutomationSecurity, AutomationSecurityForceDisable))
                _logger.Warn("無法設定 AutomationSecurity，這個版本的 PowerPoint 可能不支援；巨集停用無法保證。");

            settings.Apply(DisplayAlerts, AlertsNone);

            // 只登記由我們啟動的執行個體，作為 Quit 失敗時的最後手段
            if (policy.MayKillLeftoverProcesses)
            {
                var pid = TryGetProcessId(app);
                if (pid is { } value)
                {
                    guard.Track(value);
                    _logger.Info($"本程式啟動的 PowerPoint PID = {value}。");
                }
                else
                {
                    _logger.Warn("無法取得 PowerPoint 的 PID，結束時只會呼叫 Quit()，不做強制關閉。");
                }
            }

            presentations = app.GetObject("Presentations");

            var fullPath = Path.GetFullPath(request.SourcePath);
            presentation = FindAlreadyOpen(presentations, fullPath);

            if (presentation is null)
            {
                presentation = presentations.CallObject(
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
            presentations?.Dispose();

            // 還原 AutomationSecurity 與 DisplayAlerts。借用使用者的執行個體時這是必要的。
            settings?.Dispose();

            if (app is not null && policy.MayQuitApplication) SafeQuit(app);
            else if (app is not null) _logger.Info("PowerPoint 由使用者開啟，保持原狀不關閉。");

            app?.Dispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            // 只會動到上面明確登記的 PID
            guard.KillSurvivors(TimeSpan.FromSeconds(8));
        }
    }

    private static bool IsPowerPointRunning()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("POWERPNT")) { p.Dispose(); return true; }
            return false;
        }
        catch
        {
            // 查不到時保守處理：當作使用者已開啟，寧可不關也不要誤關
            return true;
        }
    }

    /// <summary>由 Application.HWND 反查實際的程序 ID。</summary>
    private int? TryGetProcessId(ComObject app)
    {
        try
        {
            var hwnd = app.TryGetInt("HWND");
            if (hwnd is not { } handle || handle == 0) return null;

            _ = GetWindowThreadProcessId(new IntPtr(handle), out var pid);
            return pid == 0 ? null : (int)pid;
        }
        catch (Exception ex)
        {
            _logger.Warn("取得 PowerPoint PID 失敗：" + ex.Message);
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private ComObject? FindAlreadyOpen(ComObject presentations, string fullPath)
    {
        try
        {
            var count = presentations.Get<int>("Count");
            for (var i = 1; i <= count; i++)
            {
                var candidate = presentations.CallObject("Item", i);
                try
                {
                    var name = candidate.GetOrDefault<string>("FullName", string.Empty);
                    if (!string.IsNullOrEmpty(name) &&
                        string.Equals(Path.GetFullPath(name), fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
                catch { }
                candidate.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.Warn("列舉已開啟的簡報時發生問題：" + ex.Message);
        }

        return null;
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

    private void SafeQuit(ComObject app)
    {
        try { app.Call("Quit"); }
        catch (Exception ex) { _logger.Warn("結束 PowerPoint 時發生問題：" + ex.Message); }
    }

    private static string DescribeComError(ComInvocationException com) => (uint)com.ComHResult switch
    {
        0x80020003 => $"PowerPoint 不認得自動化指令「{com.MemberName}」。這通常代表 Office 版本較舊或安裝檔案損毀，請試著修復 Office，或改用 LibreOffice 轉換。",
        0x800A175D => "PowerPoint 目前正忙碌或有對話視窗開啟，請關閉後再試一次。",
        0x80048240 => "PowerPoint 無法開啟這個檔案，檔案可能已損毀或格式不受支援。",
        0x80004005 => "PowerPoint 回報一般性錯誤，這個檔案可能已受密碼保護或損毀。",
        _ => $"PowerPoint 轉換失敗（{com.MemberName}，錯誤碼 0x{com.ComHResult:X8}）。"
    };
}
