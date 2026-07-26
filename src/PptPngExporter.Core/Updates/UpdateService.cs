using System.Diagnostics;
using System.IO.Compression;
using PptPngExporter.Core.Converters;

namespace PptPngExporter.Core.Updates;

/// <summary>套用更新的結果。</summary>
public sealed class UpdateInstallResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }

    /// <summary>需要重新啟動程式才能完成。</summary>
    public bool RequiresRestart { get; init; }

    /// <summary>更新程式已經啟動，本程式應該立刻結束。</summary>
    public bool ShouldExitNow { get; init; }

    public static UpdateInstallResult Failed(string message) => new() { Success = false, Message = message };
}

/// <summary>
/// 免安裝版的就地替換。
///
/// Windows 不允許覆寫執行中的 .exe，但<b>允許改名</b>。
/// 因此流程是：把執行中的 exe 改名成備份 → 把新的 exe 放到原位置 → 重新啟動 → 下次啟動時刪掉備份。
/// 這比另外寫一支等待用的批次檔可靠得多（不會有防毒攔截或視窗閃爍的問題）。
/// </summary>
public static class PortableUpdateInstaller
{
    public const string BackupSuffix = ".old";

    /// <summary>
    /// 把 <paramref name="currentExe"/> 換成 <paramref name="newExe"/>。
    /// 回傳備份檔路徑。失敗時會嘗試復原。
    /// </summary>
    public static string Swap(string currentExe, string newExe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentExe);
        ArgumentException.ThrowIfNullOrWhiteSpace(newExe);

        if (!File.Exists(newExe)) throw new FileNotFoundException("找不到新版執行檔。", newExe);

        var backup = currentExe + BackupSuffix;

        // 清掉上一次殘留的備份，否則改名會失敗
        TryDelete(backup);

        File.Move(currentExe, backup);

        try
        {
            File.Move(newExe, currentExe);
        }
        catch
        {
            // 放不回去就把舊的搬回原位，至少維持可用
            try { File.Move(backup, currentExe); } catch { }
            throw;
        }

        return backup;
    }

    /// <summary>刪除上次更新留下的備份。程式啟動時呼叫。</summary>
    public static bool CleanUpBackup(string? currentExe = null)
    {
        try
        {
            var exe = currentExe ?? Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return false;

            var backup = exe + BackupSuffix;
            if (!File.Exists(backup)) return false;

            File.Delete(backup);
            return true;
        }
        catch
        {
            // 舊版可能還沒完全結束，下次啟動再試
            return false;
        }
    }

    /// <summary>
    /// 把下載回來的更新檔轉成「可以直接替換的 .exe 路徑」。
    ///
    /// 免安裝版現在發佈的是裸的單一 .exe（不再包 ZIP），但舊版發行的是 ZIP，
    /// 而且使用者可能從任一版更新上來，因此兩種都要能處理。
    /// </summary>
    public static string ResolveExecutable(string downloadedFile, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadedFile);

        if (!File.Exists(downloadedFile))
            throw new FileNotFoundException("找不到下載的更新檔。", downloadedFile);

        if (downloadedFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return ExtractExecutable(downloadedFile, destinationDirectory);

        if (downloadedFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return downloadedFile;

        throw new InvalidOperationException(
            $"不認得的更新檔格式：{Path.GetFileName(downloadedFile)}（只支援 .exe 與 .zip）。");
    }

    /// <summary>從下載的 ZIP 取出唯一的 .exe 到指定位置。</summary>
    public static string ExtractExecutable(string zipPath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        using var archive = ZipFile.OpenRead(zipPath);

        var entry = archive.Entries
                        .Where(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(e => e.Length)
                        .FirstOrDefault()
                    ?? throw new InvalidOperationException("更新檔裡找不到執行檔。");

        var target = Path.Combine(destinationDirectory, entry.Name);
        entry.ExtractToFile(target, overwrite: true);
        return target;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

/// <summary>檢查與套用更新。</summary>
public sealed class UpdateService
{
    private readonly IReleaseSource _source;
    private readonly UpdateConfiguration _config;
    private readonly IAppLogger _logger;
    private readonly InstallationKind _installation;
    private readonly string? _currentExecutable;
    private readonly bool _launchAfterUpdate;

    /// <param name="currentExecutable">
    /// 要被替換的執行檔。預設為目前程序的執行檔；測試時必須指定，
    /// 否則會去動到測試主機的執行檔。
    /// </param>
    /// <param name="launchAfterUpdate">替換完成後是否啟動新版。測試時關閉。</param>
    public UpdateService(
        IReleaseSource source,
        UpdateConfiguration config,
        IAppLogger? logger = null,
        InstallationKind? installation = null,
        string? currentExecutable = null,
        bool launchAfterUpdate = true)
    {
        _source = source;
        _config = config;
        _logger = logger ?? NullLogger.Instance;
        _installation = installation ?? InstallationInfo.Detect();
        _currentExecutable = currentExecutable;
        _launchAfterUpdate = launchAfterUpdate;
    }

    public InstallationKind Installation => _installation;
    public UpdateConfiguration Configuration => _config;

    /// <summary>暫存下載檔的位置。</summary>
    public static string DownloadDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PptPngExporter", "updates");

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = ReleaseVersion.Current;

        if (!_config.IsConfigured)
            return UpdateCheckResult.Failed("尚未設定更新來源，請在 update.config.json 填入 GitHub 儲存庫。", current);

        try
        {
            var manifest = await _source.GetLatestAsync(cancellationToken);
            if (manifest is null)
                return UpdateCheckResult.Failed("沒有取得任何發行資訊。", current);

            var result = UpdatePolicy.Evaluate(current, manifest, _installation);
            _logger.Info($"檢查更新：目前 {result.CurrentVersion}，最新 {result.LatestVersion}，結果 {result.Availability}。");
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn("檢查更新失敗：" + ex.Message);
            return UpdateCheckResult.Failed("無法連線到更新伺服器，請確認網路連線。", current);
        }
        catch (Exception ex)
        {
            _logger.Error("檢查更新時發生錯誤。", ex);
            return UpdateCheckResult.Failed("檢查更新時發生問題：" + ex.Message, current);
        }
    }

    /// <summary>下載並套用更新。呼叫前請先確認 <see cref="UpdateAvailability.CanUpdateInApp"/>。</summary>
    public async Task<UpdateInstallResult> DownloadAndApplyAsync(
        UpdateCheckResult check,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        if (check.Availability != UpdateAvailability.CanUpdateInApp || check.Asset is null)
            return UpdateInstallResult.Failed("這個版本無法在程式內更新，請手動下載。");

        var asset = check.Asset;
        var workDir = Path.Combine(DownloadDirectory, check.LatestVersion.ToString());

        try
        {
            Directory.CreateDirectory(workDir);
            var downloaded = Path.Combine(workDir, asset.FileName);

            _logger.Info($"開始下載更新：{asset.FileName}");
            await _source.DownloadAsync(asset, downloaded, progress, cancellationToken);

            // 雜湊來自以 HTTPS 取得的清單，可擋下傳輸損毀與網路中間人竄改
            if (!FileIntegrity.Verify(downloaded, asset.Sha256))
            {
                _logger.Error($"更新檔雜湊不符：預期 {asset.Sha256}，實際 {FileIntegrity.ComputeSha256(downloaded)}");
                TryDeleteDirectory(workDir);
                return UpdateInstallResult.Failed("下載的更新檔驗證失敗，已中止更新。請稍後再試或手動下載。");
            }

            _logger.Info("更新檔驗證通過。");

            return _installation == InstallationKind.Installed
                ? RunInstaller(downloaded)
                : ApplyPortable(downloaded, workDir);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(workDir);
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("套用更新失敗。", ex);
            return UpdateInstallResult.Failed("更新失敗：" + ex.Message);
        }
    }

    private UpdateInstallResult ApplyPortable(string downloadedFile, string workDir)
    {
        var currentExe = _currentExecutable ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe))
            return UpdateInstallResult.Failed("無法取得目前執行檔的位置。");

        var incoming = PortableUpdateInstaller.ResolveExecutable(downloadedFile, Path.Combine(workDir, "extracted"));

        PortableUpdateInstaller.Swap(currentExe, incoming);
        _logger.Info("免安裝版已就地替換完成。");

        if (!_launchAfterUpdate)
        {
            return new UpdateInstallResult
            {
                Success = true,
                RequiresRestart = true,
                Message = "更新完成，請重新開啟程式。"
            };
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = currentExe, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warn("自動重新啟動失敗：" + ex.Message);
            return new UpdateInstallResult
            {
                Success = true,
                RequiresRestart = true,
                Message = "更新完成，請手動重新開啟程式。"
            };
        }

        return new UpdateInstallResult
        {
            Success = true,
            RequiresRestart = true,
            ShouldExitNow = true,
            Message = "更新完成，正在重新啟動…"
        };
    }

    private UpdateInstallResult RunInstaller(string installerPath)
    {
        try
        {
            // Inno Setup 的靜默安裝參數。CLOSEAPPLICATIONS / RESTARTAPPLICATIONS
            // 讓安裝程式自己處理「檔案正在使用中」。
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NORESTART",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            return UpdateInstallResult.Failed("無法啟動安裝程式：" + ex.Message);
        }

        return new UpdateInstallResult
        {
            Success = true,
            RequiresRestart = true,
            ShouldExitNow = true,
            Message = "安裝程式已啟動，請依畫面指示完成更新。"
        };
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    /// <summary>清理舊的下載暫存與更新備份。程式啟動時呼叫。</summary>
    public static void CleanUpAfterUpdate(IAppLogger? logger = null)
    {
        var log = logger ?? NullLogger.Instance;

        if (PortableUpdateInstaller.CleanUpBackup()) log.Info("已清除上一版的備份檔。");

        try
        {
            if (!Directory.Exists(DownloadDirectory)) return;

            foreach (var dir in Directory.EnumerateDirectories(DownloadDirectory))
            {
                try
                {
                    if (Directory.GetCreationTimeUtc(dir) < DateTime.UtcNow.AddDays(-3))
                        Directory.Delete(dir, recursive: true);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            log.Warn("清理更新暫存時發生問題：" + ex.Message);
        }
    }
}
