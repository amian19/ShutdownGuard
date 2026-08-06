using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace ShutdownGuard;

public partial class App : Application
{
    private const string MutexName = "Global\\ShutdownGuard_SingleInstance_8F3C2A";
    private Mutex? _mutex;
    private TrayApp? _trayApp;

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ShutdownGuard", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("AppDomain", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("Dispatcher", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("Task", args.Exception);
            args.SetObserved();
        };

        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            ShowAlreadyRunningDialog();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _trayApp = new TrayApp();

        try
        {
            _trayApp.Start();
        }
        catch (Exception ex)
        {
            LogCrash("Startup", ex);
            ShowStartupFailureDialog(ex);

            // Clean up: don't leave a half-initialized background process.
            try { _trayApp.DisposeAsync().GetAwaiter().GetResult(); } catch { }
            _trayApp = null;

            Shutdown();
        }
    }

    private static void ShowAlreadyRunningDialog()
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "ShutdownGuard",
            Content = "ShutdownGuard 已在运行，请检查系统托盘。",
            CloseButtonText = "确定",
            IsPrimaryButtonEnabled = false,
            IsSecondaryButtonEnabled = false
        };
        var task = box.ShowDialogAsync();
        var frame = new DispatcherFrame();
        task.GetAwaiter().OnCompleted(() => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    private static void ShowStartupFailureDialog(Exception ex)
    {
        try
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = "ShutdownGuard — 启动错误",
                Content = $"启动 ShutdownGuard 失败：\n\n{ex.Message}\n\n详细信息已写入：\n{LogPath}",
                CloseButtonText = "确定",
                IsPrimaryButtonEnabled = false,
                IsSecondaryButtonEnabled = false
            };
            var task = box.ShowDialogAsync();
            var frame = new DispatcherFrame();
            task.GetAwaiter().OnCompleted(() => frame.Continue = false);
            Dispatcher.PushFrame(frame);
        }
        catch
        {
            MessageBox.Show($"ShutdownGuard 启动错误：\n\n{ex.Message}\n\n详细信息已写入：\n{LogPath}",
                "ShutdownGuard — 启动错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            var msg = $"=== {DateTime.Now:O} [{source}] ===\n{ex}\n\n";
            File.AppendAllText(LogPath, msg);

            try
            {
                var box = new Wpf.Ui.Controls.MessageBox
                {
                    Title = $"ShutdownGuard — 错误（{source}）",
                    Content = $"{ex?.Message}\n\n详细信息已写入：\n{LogPath}",
                    CloseButtonText = "确定",
                    IsPrimaryButtonEnabled = false,
                    IsSecondaryButtonEnabled = false
                };
                var task = box.ShowDialogAsync();
                var frame = new DispatcherFrame();
                task.GetAwaiter().OnCompleted(() => frame.Continue = false);
                Dispatcher.PushFrame(frame);
            }
            catch
            {
                MessageBox.Show($"ShutdownGuard 错误（{source}）：\n\n{ex?.Message}\n\n详细信息已写入：\n{LogPath}",
                    "ShutdownGuard — 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_trayApp is not null)
        {
            _trayApp.DisposeAsync().GetAwaiter().GetResult();
        }
        if (_mutex != null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
