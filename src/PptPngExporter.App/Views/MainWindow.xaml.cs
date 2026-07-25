using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PptPngExporter.App.ViewModels;

namespace PptPngExporter.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        viewModel.PagePickerRequested += OnPagePickerRequested;
    }

    /// <summary>開啟縮圖挑選視窗。</summary>
    private void OnPagePickerRequested(object? sender, IReadOnlyList<PresentationItem> items)
    {
        var pickerViewModel = new PageSelectionViewModel(
            items, ViewModel.Converters, ViewModel.EnginePreference, ViewModel.Logger);

        var window = new PageSelectionWindow(pickerViewModel) { Owner = this };

        if (window.ShowDialog() == true) ViewModel.OnPageSelectionApplied();
    }

    public MainViewModel ViewModel { get; }

    /// <summary>拖曳檔案或資料夾到視窗上時顯示「複製」游標。</summary>
    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = hasFiles && !ViewModel.IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        ViewModel.AddPaths(paths);
        e.Handled = true;
    }

    /// <summary>點選寬度預設值。</summary>
    private void OnWidthPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string width }) ViewModel.ImageWidthText = width;
    }

    /// <summary>點選編號位數預設值。</summary>
    private void OnDigitsPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var digits))
            ViewModel.NumberDigits = digits;
    }

    /// <summary>在清單上按兩下：轉換完成的項目會開啟它的輸出資料夾。</summary>
    private void OnFileListDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not PresentationItem item) return;

        if (item.HasOutput) Infrastructure.Shell.OpenFolder(item.OutputDirectory);
        else Infrastructure.Shell.RevealFile(item.FullPath);
    }

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!ViewModel.ConfirmClose())
        {
            e.Cancel = true;
            return;
        }

        ViewModel.SaveSettings();
    }
}
