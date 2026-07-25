using System.Windows;
using System.Windows.Threading;
using PptPngExporter.App.ViewModels;
using PptPngExporter.App.Views;
using PptPngExporter.Core.Converters;
using PptPngExporter.Core.Services;

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

        var window = new MainWindow(new MainViewModel(_logger));
        MainWindow = window;

        // 支援用「以此程式開啟」或拖到執行檔上啟動
        if (e.Args.Length > 0) window.ViewModel.AddPaths(e.Args);

        window.Show();
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
