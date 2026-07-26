using PptPngExporter.Core.Models;
using PptPngExporter.Core.Parsing;

namespace PptPngExporter.Core.Converters;

/// <summary>單一簡報的轉檔委託內容。</summary>
public sealed class ConversionRequest
{
    public required string SourcePath { get; init; }
    public required string OutputDirectory { get; init; }
    public PageRangeSpec Pages { get; init; } = PageRangeSpec.All;
    public int ImageWidth { get; init; } = 1920;
    public string FileNamePrefix { get; init; } = string.Empty;
    public FileNumbering Numbering { get; init; } = FileNumbering.Sequential;
    public int NumberDigits { get; init; } = 3;
}

public sealed record SlideProgress(int Completed, int Total);

/// <summary>轉檔失敗。Message 為可直接顯示給使用者的繁體中文說明。</summary>
public sealed class ConversionException : Exception
{
    public ConversionException(string message, Exception? inner = null) : base(message, inner) { }
}

public interface ISlideConverter
{
    ConversionEngine Engine { get; }

    /// <summary>顯示給使用者的引擎名稱。</summary>
    string DisplayName { get; }

    /// <summary>目前環境是否可使用此引擎（會快取結果）。</summary>
    bool IsAvailable();

    /// <summary>不可用時的原因說明；可用時為 null。</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// 清除偵測快取。使用者在程式執行中途安裝了 PowerPoint 或 LibreOffice 時，
    /// 必須能重新偵測，否則要重開程式才會生效。
    /// </summary>
    void ResetAvailability();

    /// <summary>
    /// 執行轉檔並回傳實際寫出的圖片路徑。失敗請擲出 <see cref="ConversionException"/>。
    /// </summary>
    IReadOnlyList<string> Convert(ConversionRequest request, IProgress<SlideProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// 宣告接下來會連續轉換多個檔案，讓引擎有機會把昂貴的資源留著重複使用
    /// （PowerPoint 會共用同一個 Application，省下每個檔案一次的冷啟動）。
    ///
    /// 回傳 null 代表這個引擎沒有可共用的東西。回傳的物件必須在整批結束時、
    /// 且在<b>同一條執行緒</b>上釋放——COM 物件有執行緒親和性。
    /// </summary>
    IDisposable? BeginBatch(CancellationToken cancellationToken) => null;
}

public interface IAppLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? exception = null) { }
}
