using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Hardcodet.Wpf.TaskbarNotification;
using Wpf.Ui.Controls;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;
using StackPanel = System.Windows.Controls.StackPanel;
using Orientation = System.Windows.Controls.Orientation;

namespace ShutdownGuard;

public sealed class TrayApp : IAsyncDisposable
{
    private readonly ConfigStore _store = new();
    private readonly SchedulerService _scheduler;
    private TaskbarIcon? _trayIcon;
    private AppConfig _config = new();
    private bool _disposed;

    private static readonly BitmapImage _iconSource =
        new(new Uri("pack://application:,,,/Assets/ShutdownGuard.ico", UriKind.Absolute));

    public TrayApp()
    {
        var executor = CreateExecutor(_config.Shutdown.DryRun);
        _scheduler = new SchedulerService(new SystemClock(), executor);
        _scheduler.NextShutdownChanged += OnNextShutdownChanged;
    }

    public void Start()
    {
        _config = _store.Load();
        AppLogger.Init();
        AppLogger.Info("Application started");

        var executor = CreateExecutor(_config.Shutdown.DryRun);
        _scheduler.UpdatePlan(_config.Shutdown);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = BuildTooltip(),
            IconSource = _iconSource,
            ContextMenu = BuildContextMenu()
        };

        AppLogger.Info("Config loaded");
        AppLogger.Info("Scheduler started");
        _scheduler.Start();
    }

    private string BuildTooltip()
    {
        var next = _scheduler.NextShutdown;
        if (!_config.Shutdown.Enabled)
            return "ShutdownGuard — Disabled";

        if (next is null)
            return "ShutdownGuard — Disabled";

        var dryLabel = _config.Shutdown.DryRun ? " [DryRun]" : "";
        return $"ShutdownGuard — Next: {next:HH:mm}{dryLabel}";
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        // Header
        var header = new TextBlock
        {
            Text = "ShutdownGuard",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        };
        var headerItem = new MenuItem
        {
            Header = header,
            IsEnabled = false,
            StaysOpenOnClick = true
        };
        menu.Items.Add(headerItem);

        menu.Items.Add(new Separator());

        // Status
        var statusText = _config.Shutdown.Enabled ? "Enabled" : "Disabled";
        var nextText = _scheduler.NextShutdown is { } n
            ? $"Next: {n:yyyy-MM-dd HH:mm}"
            : "No schedule";

        var statusItem = new MenuItem
        {
            Header = $"Status: {statusText} — {nextText}",
            IsEnabled = false
        };
        menu.Items.Add(statusItem);

        menu.Items.Add(new Separator());

        // Toggle Enable/Disable
        var toggleItem = new MenuItem
        {
            Header = _config.Shutdown.Enabled ? "Disable scheduled shutdown" : "Enable scheduled shutdown"
        };
        toggleItem.Click += (_, _) => ToggleEnabled();
        menu.Items.Add(toggleItem);

        // Dry Run indicator
        var dryRunItem = new MenuItem
        {
            Header = _config.Shutdown.DryRun ? "Dry Run: On" : "Dry Run: Off",
            IsEnabled = false
        };
        menu.Items.Add(dryRunItem);

        menu.Items.Add(new Separator());

        // Exit
        var exitItem = new MenuItem
        {
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new SymbolIcon { Symbol = SymbolRegular.SignOut24, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) },
                    new TextBlock { Text = "Exit", VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        exitItem.Click += async (_, _) => await ShutdownAppAsync();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ToggleEnabled()
    {
        _config.Shutdown.Enabled = !_config.Shutdown.Enabled;
        _store.Save(_config);
        _scheduler.UpdatePlan(_config.Shutdown);

        var status = _config.Shutdown.Enabled ? "Enabled" : "Disabled";
        AppLogger.Info($"Scheduler {status}");

        RefreshTray();
    }

    /// <summary>
    /// Called when the user toggles DryRun mode.
    /// Updates the executor in-place without restarting the scheduler.
    /// </summary>
    public void SetDryRun(bool dryRun)
    {
        _config.Shutdown.DryRun = dryRun;
        _store.Save(_config);
        _scheduler.UpdateExecutor(CreateExecutor(dryRun));
        AppLogger.Info($"DryRun set to {dryRun}");
        RefreshTray();
    }

    private void OnNextShutdownChanged(DateTimeOffset? next)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _trayIcon!.ToolTipText = BuildTooltip();
            _trayIcon.ContextMenu = BuildContextMenu();
        });
    }

    private void RefreshTray()
    {
        if (_trayIcon is null) return;
        _trayIcon.ToolTipText = BuildTooltip();
        _trayIcon.ContextMenu = BuildContextMenu();
    }

    private async Task ShutdownAppAsync()
    {
        AppLogger.Info("Scheduler stopped");
        await _scheduler.StopAsync();
        Application.Current.Shutdown();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _scheduler.NextShutdownChanged -= OnNextShutdownChanged;
        await _scheduler.DisposeAsync();
        _trayIcon?.Dispose();
    }

    private static IShutdownExecutor CreateExecutor(bool dryRun)
    {
        return dryRun ? new DryRunShutdownExecutor() : new WindowsShutdownExecutor();
    }
}
