using System.Windows;
using System.Windows.Threading;
using PptPngExporter.App.ViewModels;
using PptPngExporter.App.Views;
using PptPngExporter.Core.Converters;
using PptPngExporter.Core.Services;
using PptPngExporter.Core.Updates;

namespace PptPngExporter.App;

public partial class App : Application
{
    private IAppLogger _logger = NullLogger.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = new FileLogger();
        _logger.Info("程式啟動。");

        // 未處理的例外不要讓程式直接消失，改用可讀的訊息告知使用者
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger.Error("發生未處理的錯誤。", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger.Error("背景工作發生未處理的錯誤。", args.Exception);
            args.SetObserved();
        };

        // 清掉上一次更新留下的備份與暫存
        UpdateService.CleanUpAfterUpdate(_logger);

        var viewModel = new MainViewModel(_logger);
        var window = new MainWindow(viewModel);
        MainWindow = window;

        // 更新完成後由新版接手，本程式結束
        viewModel.ExitRequested += (_, _) => Dispatcher.BeginInvoke(Shutdown);

        // 支援用「以此程式開啟」或拖到執行檔上啟動
        if (e.Args.Length > 0) _ = window.ViewModel.AddPathsAsync(e.Args);

        window.Show();

        // 啟動檢查放在視窗顯示之後，不拖慢開啟速度
        _ = viewModel.CheckForUpdatesOnStartupAsync();

        // 縮圖快取只增不減：簡報每改一次、程式每更新一版都會多出一整套，
        // 舊的永遠不會再被命中。在背景清掉過期與超量的部分。
        _ = Task.Run(() => SlidePreviewService.SweepCache(_logger));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.Error("介面發生未處理的錯誤。", e.Exception);

        MessageBox.Show(
            "程式遇到未預期的問題，但已經記錄下來了。\n\n" + e.Exception.Message,
            "PPT PNG 匯出工具",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger.Info("程式結束。");
        base.OnExit(e);
    }
}
