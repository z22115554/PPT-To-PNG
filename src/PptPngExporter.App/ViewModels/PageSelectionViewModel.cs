using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using PptPngExporter.App.Infrastructure;
using PptPngExporter.Core.Converters;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Services;

namespace PptPngExporter.App.ViewModels;

/// <summary>挑選視窗中的一張投影片縮圖。</summary>
public sealed class SlideThumbnail : ObservableObject
{
    private bool _isSelected;
    private BitmapImage? _image;

    public SlideThumbnail(int pageNumber, string imagePath)
    {
        PageNumber = pageNumber;
        ImagePath = imagePath;
    }

    public int PageNumber { get; }
    public string ImagePath { get; }
    public string PageLabel => PageNumber.ToString();

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>
    /// 延遲載入圖片，並限制解碼寬度以控制記憶體用量
    /// （大量投影片時，全尺寸解碼很容易吃掉數百 MB）。
    /// </summary>
    public BitmapImage? Image
    {
        get
        {
            if (_image is not null) return _image;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(ImagePath);
                bitmap.DecodePixelWidth = 220;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                bitmap.Freeze();
                _image = bitmap;
            }
            catch
            {
                _image = null;
            }

            return _image;
        }
    }
}

/// <summary>挑選視窗中的一份簡報（含它的所有縮圖）。</summary>
public sealed class SlideGroup : ObservableObject
{
    private string _statusText = string.Empty;
    private string? _errorMessage;

    public SlideGroup(PresentationItem source)
    {
        Source = source;
        FileName = source.FileName;
        FolderPath = source.FolderPath;

        SelectAllCommand = new RelayCommand(() => SetAll(true));
        SelectNoneCommand = new RelayCommand(() => SetAll(false));
        InvertCommand = new RelayCommand(Invert);
    }

    public PresentationItem Source { get; }
    public string FileName { get; }
    public string FolderPath { get; }

    public ObservableCollection<SlideThumbnail> Slides { get; } = new();

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand InvertCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public int SelectedCount => Slides.Count(s => s.IsSelected);

    public IReadOnlyList<int> SelectedPages => Slides.Where(s => s.IsSelected).Select(s => s.PageNumber).ToArray();

    public event EventHandler? SelectionChanged;

    public void Add(SlideThumbnail slide)
    {
        slide.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SlideThumbnail.IsSelected)) return;
            RefreshStatus();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        Slides.Add(slide);
        RefreshStatus();
    }

    public void SetAll(bool selected)
    {
        foreach (var slide in Slides) slide.IsSelected = selected;
    }

    private void Invert()
    {
        foreach (var slide in Slides) slide.IsSelected = !slide.IsSelected;
    }

    public void RefreshStatus()
    {
        StatusText = Slides.Count == 0 ? "沒有可預覽的投影片" : $"已選 {SelectedCount} / {Slides.Count} 頁";
        OnPropertyChanged(nameof(SelectedCount));
    }
}

/// <summary>
/// 「從縮圖挑選頁面」視窗的邏輯。
/// 縮圖由 <see cref="SlidePreviewService"/> 產生並快取，第二次開啟同一份簡報是即時的。
/// </summary>
public sealed class PageSelectionViewModel : ObservableObject
{
    private readonly SlidePreviewService _previews;
    private readonly EnginePreference _preference;
    private readonly IAppLogger _logger;
    private CancellationTokenSource? _cts;

    private bool _isLoading;
    private double _loadProgress;
    private string _loadingText = string.Empty;
    private string _summaryText = string.Empty;
    private string? _loadError;

    public PageSelectionViewModel(
        IReadOnlyList<PresentationItem> items,
        IReadOnlyList<ISlideConverter> converters,
        EnginePreference preference,
        IAppLogger logger)
    {
        _previews = new SlidePreviewService(converters, logger);
        _preference = preference;
        _logger = logger;

        Items = items;

        SelectAllCommand = new RelayCommand(() => ForEachGroup(g => g.SetAll(true)), () => !IsLoading);
        SelectNoneCommand = new RelayCommand(() => ForEachGroup(g => g.SetAll(false)), () => !IsLoading);
        CancelLoadingCommand = new RelayCommand(CancelLoading, () => IsLoading);
    }

    public IReadOnlyList<PresentationItem> Items { get; }

    public ObservableCollection<SlideGroup> Groups { get; } = new();

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand CancelLoadingCommand { get; }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(IsReady));
            SelectAllCommand.RaiseCanExecuteChanged();
            SelectNoneCommand.RaiseCanExecuteChanged();
            CancelLoadingCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsReady => !_isLoading;

    public double LoadProgress
    {
        get => _loadProgress;
        private set => SetProperty(ref _loadProgress, value);
    }

    public string LoadingText
    {
        get => _loadingText;
        private set => SetProperty(ref _loadingText, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string? LoadError
    {
        get => _loadError;
        private set => SetProperty(ref _loadError, value);
    }

    public int TotalSelected => Groups.Sum(g => g.SelectedCount);

    /// <summary>產生（或取用快取的）縮圖。單一簡報失敗不會影響其他簡報。</summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadError = null;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            for (var i = 0; i < Items.Count; i++)
            {
                if (token.IsCancellationRequested) break;

                var item = Items[i];
                var group = new SlideGroup(item);
                group.SelectionChanged += (_, _) => RefreshSummary();
                Groups.Add(group);

                LoadingText = $"正在準備預覽：{item.FileName}（{i + 1} / {Items.Count}）";
                LoadProgress = Items.Count == 0 ? 0 : i * 100d / Items.Count;

                try
                {
                    var preview = await StaRunner.RunAsync(() =>
                        _previews.GetPreview(item.FullPath, _preference, SlidePreviewService.DefaultThumbnailWidth, token));

                    var previouslyPicked = item.SelectedPages;

                    for (var page = 1; page <= preview.SlideCount; page++)
                    {
                        var thumb = new SlideThumbnail(page, preview.ThumbnailPaths[page - 1])
                        {
                            // 沿用上次的勾選；第一次進來時預設全選
                            IsSelected = previouslyPicked is null || previouslyPicked.Contains(page)
                        };
                        group.Add(thumb);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"產生 {item.FileName} 的預覽失敗：{ex.Message}");
                    group.ErrorMessage = ex.Message;
                }

                group.RefreshStatus();
                RefreshSummary();
            }

            LoadProgress = 100;
        }
        finally
        {
            IsLoading = false;
            _cts?.Dispose();
            _cts = null;
            LoadingText = string.Empty;
            RefreshSummary();
        }
    }

    private void CancelLoading()
    {
        _cts?.Cancel();
        LoadingText = "正在停止…";
    }

    private void ForEachGroup(Action<SlideGroup> action)
    {
        foreach (var group in Groups) action(group);
    }

    private void RefreshSummary()
    {
        var total = Groups.Sum(g => g.Slides.Count);
        var selected = TotalSelected;

        SummaryText = total == 0
            ? "沒有可挑選的投影片"
            : $"共 {Groups.Count} 份簡報、{total} 頁，已選取 {selected} 頁";

        OnPropertyChanged(nameof(TotalSelected));
    }

    /// <summary>把勾選結果寫回清單項目。</summary>
    public void ApplySelection()
    {
        foreach (var group in Groups)
        {
            if (group.Slides.Count == 0) continue;
            group.Source.SetPageSelection(group.SelectedPages, group.Slides.Count);
        }
    }

    public void Close() => _cts?.Cancel();
}
