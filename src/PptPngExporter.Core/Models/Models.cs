using PptPngExporter.Core.Parsing;

namespace PptPngExporter.Core.Models;

/// <summary>實際執行轉檔的引擎。</summary>
public enum ConversionEngine
{
    None = 0,
    PowerPoint = 1,
    LibreOffice = 2
}

/// <summary>使用者選擇的轉檔方式偏好。</summary>
public enum EnginePreference
{
    /// <summary>優先使用 PowerPoint，失敗或未安裝時自動改用 LibreOffice。</summary>
    Auto = 0,
    PowerPointOnly = 1,
    LibreOfficeOnly = 2
}

/// <summary>輸出圖片的編號方式。</summary>
public enum FileNumbering
{
    /// <summary>依輸出順序連續編號：挑第 1、5、7 張會得到 001、002、003。</summary>
    Sequential = 0,

    /// <summary>沿用原始頁碼：挑第 1、5、7 張會得到 001、005、007。</summary>
    OriginalPage = 1
}

public enum ExportStatus
{
    Pending = 0,
    Running = 1,
    Success = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>批次轉檔的整體設定。</summary>
public sealed class ExportOptions
{
    public required string OutputRoot { get; init; }
    public PageRangeSpec Pages { get; init; } = PageRangeSpec.All;
    public int ImageWidth { get; init; } = 1920;
    public string FileNamePrefix { get; init; } = string.Empty;
    public EnginePreference Engine { get; init; } = EnginePreference.Auto;

    /// <summary>輸出檔名的編號方式。</summary>
    public FileNumbering Numbering { get; init; } = FileNumbering.Sequential;

    /// <summary>補零位數；0 代表依張數自動決定。</summary>
    public int NumberDigits { get; init; } = 3;

    public const int MinWidth = 320;
    public const int MaxWidth = 10000;

    /// <summary>把寬度限制在安全範圍內，避免 PowerPoint / PDFium 因極端值失敗。</summary>
    public static int ClampWidth(int width) => Math.Clamp(width, MinWidth, MaxWidth);
}

/// <summary>
/// 一份待轉換的簡報。<see cref="Pages"/> 為 null 時沿用 <see cref="ExportOptions.Pages"/>，
/// 不為 null 則代表使用者為這一份簡報單獨挑選了頁面。
/// </summary>
public sealed class ExportJob
{
    public required string SourcePath { get; init; }
    public Parsing.PageRangeSpec? Pages { get; init; }

    public static ExportJob For(string path) => new() { SourcePath = path };
}

/// <summary>單一簡報的轉檔結果。</summary>
public sealed class ExportResult
{
    public required string SourcePath { get; init; }
    public string SourceName => Path.GetFileName(SourcePath);
    public ExportStatus Status { get; set; } = ExportStatus.Pending;
    public ConversionEngine EngineUsed { get; set; } = ConversionEngine.None;
    public string? OutputDirectory { get; set; }
    public int ImageCount { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>供 UI 顯示的一行狀態說明。</summary>
    public string StatusText => Status switch
    {
        ExportStatus.Pending => "等待中",
        ExportStatus.Running => "轉換中",
        ExportStatus.Success => $"完成 · {ImageCount} 張 · {EngineName}",
        ExportStatus.Failed => "失敗",
        ExportStatus.Cancelled => "已取消",
        _ => string.Empty
    };

    public string EngineName => EngineUsed switch
    {
        ConversionEngine.PowerPoint => "PowerPoint",
        ConversionEngine.LibreOffice => "LibreOffice",
        _ => "—"
    };
}

/// <summary>整批工作的彙總結果。</summary>
public sealed class BatchExportReport
{
    public required IReadOnlyList<ExportResult> Results { get; init; }
    public required string OutputRoot { get; init; }
    public bool WasCancelled { get; init; }

    public int SuccessCount => Results.Count(r => r.Status == ExportStatus.Success);
    public int FailedCount => Results.Count(r => r.Status == ExportStatus.Failed);
    public int CancelledCount => Results.Count(r => r.Status == ExportStatus.Cancelled);
    public int TotalImages => Results.Sum(r => r.ImageCount);
}

/// <summary>轉檔過程回報給 UI 的進度。</summary>
public sealed class ProgressReport
{
    public int FilesCompleted { get; init; }
    public int FilesTotal { get; init; }
    public string CurrentFileName { get; init; } = string.Empty;
    public int SlidesCompleted { get; init; }
    public int SlidesTotal { get; init; }
    public string Message { get; init; } = string.Empty;

    /// <summary>0–100 的整體百分比，會把目前檔案的頁面進度也一併計入。</summary>
    public double OverallPercent
    {
        get
        {
            if (FilesTotal <= 0) return 0;
            var perFile = 100d / FilesTotal;
            var partial = SlidesTotal > 0 ? perFile * SlidesCompleted / SlidesTotal : 0;
            return Math.Clamp(FilesCompleted * perFile + partial, 0, 100);
        }
    }
}
