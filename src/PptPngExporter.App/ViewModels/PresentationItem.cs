using System.IO;
using PptPngExporter.App.Infrastructure;
using PptPngExporter.Core.Models;

namespace PptPngExporter.App.ViewModels;

/// <summary>清單中的一份簡報。</summary>
public sealed class PresentationItem : ObservableObject
{
    private bool _isSelected = true;
    private ExportStatus _status = ExportStatus.Pending;
    private string _statusText = "待轉換";
    private string? _errorMessage;
    private string? _outputDirectory;

    public PresentationItem(string path)
    {
        FullPath = Path.GetFullPath(path);
        FileName = Path.GetFileName(FullPath);
        FolderPath = Path.GetDirectoryName(FullPath) ?? string.Empty;

        try
        {
            var info = new FileInfo(FullPath);
            SizeText = FormatSize(info.Length);
        }
        catch
        {
            SizeText = "—";
        }
    }

    public string FullPath { get; }
    public string FileName { get; }
    public string FolderPath { get; }
    public string SizeText { get; }

    /// <summary>副檔名（大寫，不含點），顯示在清單的類型標籤上。</summary>
    public string Kind => Path.GetExtension(FullPath).TrimStart('.').ToUpperInvariant();

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ExportStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value)) OnPropertyChanged(nameof(HasOutput));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string? OutputDirectory
    {
        get => _outputDirectory;
        set
        {
            if (SetProperty(ref _outputDirectory, value)) OnPropertyChanged(nameof(HasOutput));
        }
    }

    public bool HasOutput => !string.IsNullOrEmpty(_outputDirectory) && Directory.Exists(_outputDirectory);

    /// <summary>使用者用縮圖挑選的頁碼；null 代表沿用整批的頁面設定。</summary>
    public IReadOnlySet<int>? SelectedPages { get; private set; }

    /// <summary>這份簡報的總頁數（產生過縮圖之後才會有值）。</summary>
    public int TotalSlides { get; private set; }

    public bool HasPageSelection => SelectedPages is { Count: > 0 };

    /// <summary>顯示在清單列上的挑選摘要，例如「已挑 3 / 10 頁」。</summary>
    public string PageSelectionSummary => SelectedPages is null
        ? string.Empty
        : TotalSlides > 0
            ? $"已挑 {SelectedPages.Count} / {TotalSlides} 頁"
            : $"已挑 {SelectedPages.Count} 頁";

    public void SetPageSelection(IEnumerable<int> pages, int totalSlides)
    {
        SelectedPages = new HashSet<int>(pages);
        TotalSlides = totalSlides;
        OnPropertyChanged(nameof(SelectedPages));
        OnPropertyChanged(nameof(TotalSlides));
        OnPropertyChanged(nameof(HasPageSelection));
        OnPropertyChanged(nameof(PageSelectionSummary));
    }

    public void ClearPageSelection()
    {
        SelectedPages = null;
        TotalSlides = 0;
        OnPropertyChanged(nameof(SelectedPages));
        OnPropertyChanged(nameof(HasPageSelection));
        OnPropertyChanged(nameof(PageSelectionSummary));
    }

    public void Reset()
    {
        Status = ExportStatus.Pending;
        StatusText = "待轉換";
        ErrorMessage = null;
        OutputDirectory = null;
    }

    public void Apply(ExportResult result)
    {
        Status = result.Status;
        StatusText = result.StatusText;
        ErrorMessage = result.Status == ExportStatus.Failed ? result.ErrorMessage : null;
        OutputDirectory = result.OutputDirectory;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{size:0} {units[unit]}" : $"{size:0.#} {units[unit]}";
    }
}
