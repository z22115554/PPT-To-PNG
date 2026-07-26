namespace PptPngExporter.Core.IO;

/// <summary>
/// 磁碟空間檢查。
///
/// 300 張投影片在 3840px 寬度下可能產生 1–2 GB 的 PNG，而使用者通常是在跑到一半、
/// 磁碟寫不進去時才發現。這裡做兩件事：開始前先粗估並提醒，以及把「磁碟已滿」的
/// 系統錯誤翻譯成看得懂的訊息，而不是丟出一句「發生未預期的錯誤」。
/// </summary>
public static class DiskSpace
{
    /// <summary>低於這個可用空間就直接擋下來——連幾張圖都放不下。</summary>
    public const long CriticalFreeBytes = 100L * 1024 * 1024;

    /// <summary>低於這個可用空間會提醒，但仍然讓使用者繼續。</summary>
    public const long WarnFreeBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>取得指定路徑所在磁碟區的可用空間；查不到時回傳 null。</summary>
    public static long? GetAvailableFreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return null;

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch
        {
            // 網路磁碟機或權限不足時查不到，這種情況就不做預檢
            return null;
        }
    }

    /// <summary>
    /// 開始轉檔前的檢查。回傳 null 代表沒問題；否則為要顯示給使用者的說明。
    /// </summary>
    /// <param name="isBlocking">true 代表空間已經少到不該繼續。</param>
    public static string? Check(string outputRoot, out bool isBlocking)
    {
        isBlocking = false;

        var free = GetAvailableFreeBytes(outputRoot);
        if (free is not { } available) return null;

        if (available < CriticalFreeBytes)
        {
            isBlocking = true;
            return $"輸出位置的可用空間只剩 {Describe(available)}，不足以存放轉出的圖片。" +
                   "請先清出空間，或把輸出位置改到別的磁碟。";
        }

        if (available < WarnFreeBytes)
        {
            return $"輸出位置的可用空間只剩 {Describe(available)}。" +
                   "高解析度的大量投影片可能需要數 GB，建議先確認空間是否足夠。";
        }

        return null;
    }

    /// <summary>判斷例外是不是「磁碟已滿」。</summary>
    public static bool IsDiskFull(Exception ex)
    {
        // ERROR_HANDLE_DISK_FULL = 0x27、ERROR_DISK_FULL = 0x70
        const int handleDiskFull = unchecked((int)0x80070027);
        const int diskFull = unchecked((int)0x80070070);

        for (var current = ex; current is not null; current = current.InnerException!)
        {
            if (current is IOException && current.HResult is handleDiskFull or diskFull) return true;
            if (current.InnerException is null) break;
        }

        return false;
    }

    public static string Describe(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024d / 1024d / 1024d:0.#} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / 1024d / 1024d:0.#} MB";
        return $"{bytes / 1024d:0.#} KB";
    }
}
