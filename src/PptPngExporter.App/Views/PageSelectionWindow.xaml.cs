using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PptPngExporter.App.ViewModels;

namespace PptPngExporter.App.Views;

public partial class PageSelectionWindow : Window
{
    public PageSelectionWindow(PageSelectionViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;

        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }

    public PageSelectionViewModel ViewModel { get; }

    /// <summary>整張縮圖卡片都可以點擊切換勾選，不必精準點到小方框。</summary>
    private void OnThumbnailClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SlideThumbnail slide })
        {
            slide.IsSelected = !slide.IsSelected;
            e.Handled = true;
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        ViewModel.ApplySelection();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Close();
        base.OnClosed(e);
    }
}
