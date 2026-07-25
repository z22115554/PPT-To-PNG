using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using PptPngExporter.App.Infrastructure;
using PptPngExporter.Core.Converters;
using PptPngExporter.Core.Models;
using PptPngExporter.Core.Parsing;
using PptPngExporter.Core.Services;

namespace PptPngExporter.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IAppLogger _logger;
    private readonly AppSettings _settings;
    private readonly PowerPointConverter _powerPoint;
    private readonly LibreOfficeConverter _libreOffice;

    private CancellationTokenSource? _cts;

    private bool _isBusy;
    private PageMode _pageMode = PageMode.All;
    private string _pageRangeText = string.Empty;
    private string? _pageRangeError;
    private string _imageWidthText = "1920";
    private string? _imageWidthError;
    private string _fileNamePrefix = "投影片_";
    private FileNumbering _numbering = FileNumbering.Sequential;
    private int _numberDigits = 3;
    private string _outputFolder = string.Empty;
    private EnginePreference _enginePreference = EnginePreference.Auto;
    private double _progressValue;
    private string _progressDetail = string.Empty;
    private string _statusMessage = string.Empty;
    private string _summaryText = string.Empty;
    private bool _hasFinished;
    private bool _lastRunHadFailures;

    public MainViewModel(IAppLogger logger)
    {
        _logger = logger;
        _settings = AppSettings.Load();
        _powerPoint = new PowerPointConverter(logger);
        _libreOffice = new LibreOfficeConverter(logger);

        Files.CollectionChanged += OnFilesChanged;

        AddFilesCommand = new RelayCommand(AddFiles, () => !IsBusy);
        RemoveSelectedCommand = new RelayCommand(RemoveSelected, () => !IsBusy && Files.Any(f => f.IsSelected));
        ClearAllCommand = new RelayCommand(ClearAll, () => !IsBusy && Files.Count > 0);
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true), () => !IsBusy && Files.Any(f => !f.IsSelected));
        SelectNoneCommand = new RelayCommand(() => SetAllSelected(false), () => !IsBusy && Files.Any(f => f.IsSelected));
        BrowseOutputCommand = new RelayCommand(BrowseOutput, () => !IsBusy);
        StartCommand = new RelayCommand(RunStart, CanStart);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy && _cts is { IsCancellationRequested: false });
        OpenOutputCommand = new RelayCommand(() => OpenOutputFolder(), () => Directory.Exists(OutputFolder));
        OpenItemFolderCommand = new RelayCommand<PresentationItem>(item => Shell.OpenFolder(item?.OutputDirectory), item => item?.HasOutput == true);
        OpenLogCommand = new RelayCommand(() => Shell.OpenFolder(FileLogger.DefaultDirectory));
        RecheckEnginesCommand = new RelayCommand(RecheckEngines, () => !IsBusy);
        DownloadLibreOfficeCommand = new RelayCommand(() => Shell.OpenUrl(LibreOfficeDownloadUrl));
        OpenPagePickerCommand = new RelayCommand(OpenPagePicker, () => !IsBusy && Files.Any(f => f.IsSelected));

        RestoreSettings();
        RefreshEngineStatus();
    }

    public ObservableCollection<PresentationItem> Files { get; } = new();

    public IReadOnlyList<string> WidthPresets { get; } = new[] { "1280", "1920", "2560", "3840" };

    public RelayCommand AddFilesCommand { get; }
    public RelayCommand RemoveSelectedCommand { get; }
    public RelayCommand ClearAllCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand BrowseOutputCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand<PresentationItem> OpenItemFolderCommand { get; }
    public RelayCommand OpenLogCommand { get; }
    public RelayCommand RecheckEnginesCommand { get; }
    public RelayCommand DownloadLibreOfficeCommand { get; }
    public RelayCommand OpenPagePickerCommand { get; }

    /// <summary>要求檢視層開啟縮圖挑選視窗（ViewModel 不直接持有 Window）。</summary>
    public event EventHandler<IReadOnlyList<PresentationItem>>? PagePickerRequested;

    /// <summary>供檢視層建立挑選視窗使用。</summary>
    internal IReadOnlyList<ISlideConverter> Converters => new ISlideConverter[] { _powerPoint, _libreOffice };
    internal IAppLogger Logger => _logger;

    public const string LibreOfficeDownloadUrl = "https://zh-tw.libreoffice.org/download/libreoffice/";

    #region 狀態

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            RaiseCommandStates();
        }
    }

    public bool IsIdle => !_isBusy;

    public bool HasFiles => Files.Count > 0;

    public int SelectedCount => Files.Count(f => f.IsSelected);

    public string FileCountText => Files.Count == 0
        ? "尚未加入任何簡報"
        : $"共 {Files.Count} 份，已勾選 {SelectedCount} 份";

    public PageMode PageMode
    {
        get => _pageMode;
        set
        {
            if (!SetProperty(ref _pageMode, value)) return;
            OnPropertyChanged(nameof(UseAllPages));
            OnPropertyChanged(nameof(UseCustomPages));
            OnPropertyChanged(nameof(UsePickedPages));
            ValidatePageRange();
            OpenPagePickerCommand.RaiseCanExecuteChanged();
        }
    }

    public bool UseAllPages
    {
        get => _pageMode == PageMode.All;
        set { if (value) PageMode = PageMode.All; }
    }

    public bool UseCustomPages
    {
        get => _pageMode == PageMode.Range;
        set { if (value) PageMode = PageMode.Range; }
    }

    /// <summary>從縮圖逐頁勾選。</summary>
    public bool UsePickedPages
    {
        get => _pageMode == PageMode.Picked;
        set { if (value) PageMode = PageMode.Picked; }
    }

    /// <summary>目前已挑選頁面的簡報份數，顯示在按鈕旁。</summary>
    public string PickedSummary
    {
        get
        {
            var picked = Files.Count(f => f.IsSelected && f.HasPageSelection);
            var total = Files.Count(f => f.IsSelected);
            if (total == 0) return "請先在左側勾選簡報";
            return picked == 0
                ? "尚未挑選，按上方按鈕開啟縮圖"
                : $"{picked} / {total} 份已挑選頁面";
        }
    }

    public string PageRangeText
    {
        get => _pageRangeText;
        set
        {
            if (!SetProperty(ref _pageRangeText, value)) return;
            ValidatePageRange();
        }
    }

    public string? PageRangeError
    {
        get => _pageRangeError;
        private set => SetProperty(ref _pageRangeError, value);
    }

    public string ImageWidthText
    {
        get => _imageWidthText;
        set
        {
            if (!SetProperty(ref _imageWidthText, value)) return;
            ValidateWidth();
        }
    }

    public string? ImageWidthError
    {
        get => _imageWidthError;
        private set => SetProperty(ref _imageWidthError, value);
    }

    public string FileNamePrefix
    {
        get => _fileNamePrefix;
        set
        {
            if (SetProperty(ref _fileNamePrefix, value)) OnPropertyChanged(nameof(FileNamePreview));
        }
    }

    public FileNumbering Numbering
    {
        get => _numbering;
        set
        {
            if (!SetProperty(ref _numbering, value)) return;
            OnPropertyChanged(nameof(NumberingSequential));
            OnPropertyChanged(nameof(NumberingOriginal));
            OnPropertyChanged(nameof(FileNamePreview));
        }
    }

    public bool NumberingSequential
    {
        get => _numbering == FileNumbering.Sequential;
        set { if (value) Numbering = FileNumbering.Sequential; }
    }

    public bool NumberingOriginal
    {
        get => _numbering == FileNumbering.OriginalPage;
        set { if (value) Numbering = FileNumbering.OriginalPage; }
    }

    /// <summary>補零位數；0 代表自動。</summary>
    public int NumberDigits
    {
        get => _numberDigits;
        set
        {
            if (!SetProperty(ref _numberDigits, value)) return;
            OnPropertyChanged(nameof(FileNamePreview));
        }
    }

    /// <summary>
    /// 即時預覽輸出檔名。刻意用「挑選第 1、5、7 頁」當例子，
    /// 因為兩種編號方式的差別只有在跳頁時才看得出來。
    /// </summary>
    public string FileNamePreview
    {
        get
        {
            var prefix = Core.IO.FileNameSanitizer.SanitizePrefix(_fileNamePrefix);
            var pages = new[] { 1, 5, 7 };
            var naming = new Core.IO.ImageNameBuilder(prefix, _numbering, _numberDigits, pages);
            var names = pages.Select((page, i) => naming.Build(i + 1, page) + ".png");
            return "挑選第 1、5、7 頁時：" + string.Join("、", names);
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            if (!SetProperty(ref _outputFolder, value)) return;
            OpenOutputCommand.RaiseCanExecuteChanged();
            StartCommand.RaiseCanExecuteChanged();
        }
    }

    public EnginePreference EnginePreference
    {
        get => _enginePreference;
        set
        {
            if (!SetProperty(ref _enginePreference, value)) return;
            OnPropertyChanged(nameof(IsEngineAuto));
            OnPropertyChanged(nameof(IsEnginePowerPoint));
            OnPropertyChanged(nameof(IsEngineLibreOffice));
            RefreshEngineStatus();
        }
    }

    public bool IsEngineAuto
    {
        get => _enginePreference == EnginePreference.Auto;
        set { if (value) EnginePreference = EnginePreference.Auto; }
    }

    public bool IsEnginePowerPoint
    {
        get => _enginePreference == EnginePreference.PowerPointOnly;
        set { if (value) EnginePreference = EnginePreference.PowerPointOnly; }
    }

    public bool IsEngineLibreOffice
    {
        get => _enginePreference == EnginePreference.LibreOfficeOnly;
        set { if (value) EnginePreference = EnginePreference.LibreOfficeOnly; }
    }

    public string EngineStatusText { get; private set; } = string.Empty;

    public bool EngineStatusIsWarning { get; private set; }

    /// <summary>兩種轉換方式都不可用。這時候需要引導使用者，而不是讓他按下開始後看到一整排失敗。</summary>
    public bool NoEngineAvailable { get; private set; }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public string ProgressDetail
    {
        get => _progressDetail;
        private set => SetProperty(ref _progressDetail, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public bool HasFinished
    {
        get => _hasFinished;
        private set => SetProperty(ref _hasFinished, value);
    }

    public bool LastRunHadFailures
    {
        get => _lastRunHadFailures;
        private set => SetProperty(ref _lastRunHadFailures, value);
    }

    #endregion

    #region 加入 / 移除檔案

    public void AddFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇要轉換的簡報",
            Multiselect = true,
            CheckFileExists = true,
            Filter = "PowerPoint 簡報 (*.pptx;*.ppt;*.ppsx;*.pps)|*.pptx;*.ppt;*.ppsx;*.pps|所有檔案 (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true) AddPaths(dialog.FileNames);
    }

    /// <summary>加入檔案或整個資料夾（拖放時會用到）。回傳實際加入的數量。</summary>
    public int AddPaths(IEnumerable<string> paths)
    {
        var expanded = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    expanded.AddRange(Directory
                        .EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Where(BatchExportService.IsSupported));
                }
                else if (File.Exists(path) && BatchExportService.IsSupported(path))
                {
                    expanded.Add(path);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"讀取 {path} 時發生問題：{ex.Message}");
            }
        }

        var existing = Files.Select(f => f.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = 0;

        foreach (var file in expanded.OrderBy(f => f, StringComparer.CurrentCulture))
        {
            var full = Path.GetFullPath(file);
            if (!existing.Add(full)) continue;

            var item = new PresentationItem(full);
            item.PropertyChanged += OnItemPropertyChanged;
            Files.Add(item);
            added++;
        }

        if (added > 0)
        {
            HasFinished = false;
            StatusMessage = $"已加入 {added} 份簡報。";
        }
        else if (expanded.Count > 0)
        {
            StatusMessage = "這些簡報都已經在清單中了。";
        }
        else
        {
            StatusMessage = "沒有找到支援的簡報檔（.ppt、.pptx、.pps、.ppsx）。";
        }

        return added;
    }

    private void RemoveSelected()
    {
        foreach (var item in Files.Where(f => f.IsSelected).ToList())
        {
            item.PropertyChanged -= OnItemPropertyChanged;
            Files.Remove(item);
        }
        StatusMessage = Files.Count == 0 ? "清單已清空。" : "已移除勾選的簡報。";
    }

    private void ClearAll()
    {
        foreach (var item in Files) item.PropertyChanged -= OnItemPropertyChanged;
        Files.Clear();
        HasFinished = false;
        SummaryText = string.Empty;
        ProgressValue = 0;
        ProgressDetail = string.Empty;
        StatusMessage = "清單已清空。";
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var item in Files) item.IsSelected = selected;
    }

    private void OnFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(PickedSummary));
        RaiseCommandStates();
    }

    private void OnItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PresentationItem.IsSelected)) return;
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(FileCountText));
        OnPropertyChanged(nameof(PickedSummary));
        RaiseCommandStates();
    }

    #endregion

    #region 輸出位置

    private void BrowseOutput()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "選擇圖片要儲存的位置",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(OutputFolder) ? OutputFolder : DefaultOutputFolder()
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            OutputFolder = dialog.SelectedPath;
    }

    public bool OpenOutputFolder()
    {
        if (Shell.OpenFolder(OutputFolder)) return true;
        StatusMessage = "找不到輸出資料夾，可能已被移動或刪除。";
        return false;
    }

    private static string DefaultOutputFolder()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "PPT 匯出圖片");

    #endregion

    #region 驗證

    private void ValidatePageRange()
    {
        if (UseAllPages)
        {
            PageRangeError = null;
        }
        else if (UsePickedPages)
        {
            PageRangeError = Files.Any(f => f.IsSelected && f.HasPageSelection)
                ? null
                : "請先按「開啟縮圖挑選頁面」挑選至少一頁。";
        }
        else if (string.IsNullOrWhiteSpace(PageRangeText))
        {
            PageRangeError = "請輸入頁碼，例如 1-5,8,12-15。";
        }
        else
        {
            PageRangeError = PageRangeParser.TryParse(PageRangeText, out _, out var error) ? null : error;
        }

        StartCommand.RaiseCanExecuteChanged();
    }

    private void ValidateWidth()
    {
        if (!int.TryParse(ImageWidthText?.Trim(), out var width))
        {
            ImageWidthError = "請輸入數字，例如 1920。";
        }
        else if (width < ExportOptions.MinWidth || width > ExportOptions.MaxWidth)
        {
            ImageWidthError = $"寬度請介於 {ExportOptions.MinWidth} 到 {ExportOptions.MaxWidth} 之間。";
        }
        else
        {
            ImageWidthError = null;
        }

        StartCommand.RaiseCanExecuteChanged();
    }

    /// <summary>重新偵測轉換引擎。使用者中途安裝了 LibreOffice 時不必重開程式。</summary>
    private void RecheckEngines()
    {
        _powerPoint.ResetAvailability();
        _libreOffice.ResetAvailability();
        RefreshEngineStatus();

        StatusMessage = NoEngineAvailable
            ? "還是沒有找到 PowerPoint 或 LibreOffice。"
            : "偵測完成：" + EngineStatusText;

        StartCommand.RaiseCanExecuteChanged();
    }

    private void OpenPagePicker()
    {
        var targets = Files.Where(f => f.IsSelected).ToList();

        if (targets.Count == 0)
        {
            StatusMessage = "請先在左側勾選要處理的簡報。";
            return;
        }

        if (NoEngineAvailable)
        {
            ShowNoEngineGuidance();
            return;
        }

        PageMode = PageMode.Picked;
        PagePickerRequested?.Invoke(this, targets);
    }

    /// <summary>挑選視窗按下「套用挑選」之後由檢視層呼叫。</summary>
    public void OnPageSelectionApplied()
    {
        OnPropertyChanged(nameof(PickedSummary));
        ValidatePageRange();

        var picked = Files.Count(f => f.IsSelected && f.HasPageSelection);
        var pages = Files.Where(f => f.IsSelected && f.SelectedPages is not null).Sum(f => f.SelectedPages!.Count);
        StatusMessage = picked == 0 ? "沒有挑選任何頁面。" : $"已挑選 {picked} 份簡報、共 {pages} 頁。";
    }

    private bool CanStart()
        => !IsBusy
           && SelectedCount > 0
           && PageRangeError is null
           && ImageWidthError is null
           && !string.IsNullOrWhiteSpace(OutputFolder);

    private void RefreshEngineStatus()
    {
        var hasPowerPoint = _powerPoint.IsAvailable();
        var hasLibreOffice = _libreOffice.IsAvailable();

        (EngineStatusText, EngineStatusIsWarning) = EnginePreference switch
        {
            EnginePreference.PowerPointOnly when !hasPowerPoint =>
                ("這台電腦沒有偵測到 PowerPoint，請改選其他轉換方式。", true),
            EnginePreference.LibreOfficeOnly when !hasLibreOffice =>
                ("這台電腦沒有偵測到 LibreOffice，請改選其他轉換方式。", true),
            _ when hasPowerPoint && hasLibreOffice =>
                ("已偵測到 PowerPoint 與 LibreOffice。若 PowerPoint 正開著，程式會借用它，結束後不會關閉。", false),
            _ when hasPowerPoint =>
                ("已偵測到 PowerPoint，將以最高還原度轉換。若 PowerPoint 正開著，程式會借用它，結束後不會關閉。", false),
            _ when hasLibreOffice =>
                ("沒有偵測到 PowerPoint，將使用 LibreOffice 轉換。", false),
            _ =>
                ("找不到 PowerPoint 或 LibreOffice。", true)
        };

        NoEngineAvailable = !hasPowerPoint && !hasLibreOffice;

        OnPropertyChanged(nameof(EngineStatusText));
        OnPropertyChanged(nameof(EngineStatusIsWarning));
        OnPropertyChanged(nameof(NoEngineAvailable));
    }

    #endregion

    #region 執行轉檔

    /// <summary>
    /// ICommand 是同步的，這裡負責把非同步流程接起來。
    /// 任何逸出的例外都必須在這裡攔下，否則 async void 會直接讓程式結束。
    /// </summary>
    private async void RunStart()
    {
        try
        {
            await StartAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("啟動轉換時發生未預期的錯誤。", ex);
            IsBusy = false;
            ProgressDetail = string.Empty;
            StatusMessage = "無法開始轉換：" + ex.Message;
        }
    }

    public async Task StartAsync()
    {
        if (!CanStart()) return;

        // 沒有任何可用引擎時，給出可以照做的指示，而不是讓每個檔案都標記失敗
        if (NoEngineAvailable && !ShowNoEngineGuidance()) return;

        var targets = Files.Where(f => f.IsSelected).ToList();
        foreach (var item in targets) item.Reset();
        foreach (var item in Files.Where(f => !f.IsSelected))
        {
            item.Status = ExportStatus.Pending;
            item.StatusText = "未勾選";
        }

        var options = new ExportOptions
        {
            OutputRoot = OutputFolder,
            Pages = UseCustomPages ? PageRangeParser.Parse(PageRangeText) : PageRangeSpec.All,
            ImageWidth = int.Parse(ImageWidthText.Trim()),
            FileNamePrefix = FileNamePrefix,
            Engine = EnginePreference,
            Numbering = Numbering,
            NumberDigits = NumberDigits
        };

        // 挑選模式下，每份簡報帶自己的頁碼；沒挑過的簡報則輸出全部頁面
        var jobs = targets.Select(t => new ExportJob
        {
            SourcePath = t.FullPath,
            Pages = UsePickedPages && t.SelectedPages is { Count: > 0 }
                ? PageRangeSpec.FromPages(t.SelectedPages)
                : null
        }).ToList();

        SaveSettings();

        _cts = new CancellationTokenSource();
        IsBusy = true;
        HasFinished = false;
        LastRunHadFailures = false;
        SummaryText = string.Empty;
        ProgressValue = 0;
        ProgressDetail = "準備中…";
        StatusMessage = string.Empty;

        var lookup = targets.ToDictionary(t => t.FullPath, StringComparer.OrdinalIgnoreCase);
        var progress = new Progress<ProgressReport>(report =>
        {
            ProgressValue = report.OverallPercent;
            ProgressDetail = report.SlidesTotal > 0
                ? $"{report.Message}（整體 {report.OverallPercent:0}%）"
                : report.Message;
        });

        var service = new BatchExportService(new ISlideConverter[] { _powerPoint, _libreOffice }, _logger);
        var token = _cts.Token;

        BatchExportReport? report;
        try
        {
            // PowerPoint COM 需要 STA，因此在專用執行緒上執行
            report = await StaRunner.RunAsync(() => service.Run(jobs, options, progress, token));
        }
        catch (Exception ex)
        {
            _logger.Error("批次轉檔發生嚴重錯誤。", ex);
            report = null;
            StatusMessage = "轉換過程發生問題：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }

        if (report is null)
        {
            ProgressDetail = string.Empty;
            return;
        }

        foreach (var result in report.Results)
        {
            if (lookup.TryGetValue(result.SourcePath, out var item)) item.Apply(result);
        }

        ProgressValue = report.WasCancelled ? ProgressValue : 100;
        ProgressDetail = string.Empty;
        HasFinished = true;
        LastRunHadFailures = report.FailedCount > 0;
        SummaryText = BuildSummary(report);
        OpenOutputCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// 引導使用者安裝可用的轉換方式。回傳 true 代表使用者堅持繼續。
    /// </summary>
    private bool ShowNoEngineGuidance()
    {
        var message =
            "這台電腦沒有偵測到 Microsoft PowerPoint，也沒有 LibreOffice，因此無法轉換簡報。\n\n" +
            "建議安裝 LibreOffice（免費、可安裝在個人帳號下，通常不需要系統管理員權限）。\n\n" +
            "按「是」開啟下載頁面。安裝完成後回到本程式按「重新偵測」即可，不需要重開程式。\n\n" +
            "如果公司電腦不允許安裝軟體，也可以請 IT 把 LibreOffice 的可攜版放到本程式旁邊的 " +
            "LibreOffice 資料夾，或設定環境變數 LIBREOFFICE_PATH 指向 soffice.exe。";

        var answer = MessageBox.Show(message, "需要先安裝轉換工具",
            MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.Yes);

        if (answer == MessageBoxResult.Yes) Shell.OpenUrl(LibreOfficeDownloadUrl);

        return false;
    }

    private static string BuildSummary(BatchExportReport report)
    {
        var parts = new List<string>();
        if (report.SuccessCount > 0) parts.Add($"成功 {report.SuccessCount} 份，共 {report.TotalImages} 張圖片");
        if (report.FailedCount > 0) parts.Add($"失敗 {report.FailedCount} 份");
        if (report.CancelledCount > 0) parts.Add($"取消 {report.CancelledCount} 份");

        if (parts.Count == 0) return "沒有任何檔案被處理。";

        var prefix = report.WasCancelled ? "已停止：" : "轉換完成：";
        return prefix + string.Join("，", parts) + "。";
    }

    private void Cancel()
    {
        if (_cts is null || _cts.IsCancellationRequested) return;

        _cts.Cancel();
        ProgressDetail = "正在停止，請稍候（會先完成目前這一頁）…";
        CancelCommand.RaiseCanExecuteChanged();
    }

    /// <summary>視窗關閉時呼叫；若還在轉檔會先詢問使用者。</summary>
    public bool ConfirmClose()
    {
        if (!IsBusy) return true;

        var answer = MessageBox.Show(
            "轉換還在進行中，確定要關閉嗎？尚未完成的檔案會被取消。",
            "PPT PNG 匯出工具",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes) return false;

        _cts?.Cancel();
        return true;
    }

    #endregion

    #region 設定存取

    private void RestoreSettings()
    {
        OutputFolder = !string.IsNullOrWhiteSpace(_settings.OutputFolder) ? _settings.OutputFolder! : DefaultOutputFolder();
        PageMode = Enum.IsDefined(typeof(PageMode), _settings.PageMode) ? (PageMode)_settings.PageMode : PageMode.All;
        PageRangeText = _settings.PageRange ?? string.Empty;
        ImageWidthText = string.IsNullOrWhiteSpace(_settings.ImageWidth) ? "1920" : _settings.ImageWidth!;
        FileNamePrefix = _settings.FileNamePrefix ?? "投影片_";
        Numbering = Enum.IsDefined(typeof(FileNumbering), _settings.Numbering) ? (FileNumbering)_settings.Numbering : FileNumbering.Sequential;
        NumberDigits = _settings.NumberDigits is >= 0 and <= 6 ? _settings.NumberDigits : 3;
        EnginePreference = Enum.IsDefined(typeof(EnginePreference), _settings.EnginePreference)
            ? (EnginePreference)_settings.EnginePreference
            : EnginePreference.Auto;

        ValidatePageRange();
        ValidateWidth();
    }

    public void SaveSettings()
    {
        _settings.OutputFolder = OutputFolder;
        _settings.PageMode = (int)PageMode;
        _settings.UseAllPages = UseAllPages;
        _settings.PageRange = PageRangeText;
        _settings.ImageWidth = ImageWidthText;
        _settings.FileNamePrefix = FileNamePrefix;
        _settings.Numbering = (int)Numbering;
        _settings.NumberDigits = NumberDigits;
        _settings.EnginePreference = (int)EnginePreference;
        _settings.Save();
    }

    #endregion

    private void RaiseCommandStates()
    {
        AddFilesCommand.RaiseCanExecuteChanged();
        RemoveSelectedCommand.RaiseCanExecuteChanged();
        ClearAllCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        SelectNoneCommand.RaiseCanExecuteChanged();
        BrowseOutputCommand.RaiseCanExecuteChanged();
        StartCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        OpenOutputCommand.RaiseCanExecuteChanged();
        RecheckEnginesCommand.RaiseCanExecuteChanged();
        OpenPagePickerCommand.RaiseCanExecuteChanged();
    }
}

/// <summary>頁面挑選方式。</summary>
public enum PageMode
{
    All = 0,
    Range = 1,
    Picked = 2
}
