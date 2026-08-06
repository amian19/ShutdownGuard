using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using ShutdownGuard.ViewModels;
using ShutdownGuard.Views;
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
    private SettingsWindow? _settingsWindow;
    private bool _trayInitialized;
    private bool _disposed;

    private static readonly BitmapImage _iconSource =
        new(new Uri("pack://application:,,,/Assets/ShutdownGuard.ico", UriKind.Absolute));

    public TrayApp()
    {
        _scheduler = new SchedulerService(new SystemClock());
        // Event subscribed in constructor but guarded by _trayInitialized / _disposed
        _scheduler.NextReminderChanged += OnNextReminderChanged;
    }

    public void Start()
    {
        // Step 1: Load config from disk
        _config = _store.Load();
        AppLogger.Init();
        AppLogger.Info("Application started");

        // Step 2: Initialize tray UI FIRST — before any scheduler event can fire.
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = BuildTooltip(),
            IconSource = _iconSource,
            ContextMenu = BuildContextMenu()
        };
        _trayInitialized = true;

        // Step 3: Now safe to update the scheduler — events will find UI ready.
        _scheduler.UpdatePlan(_config.Shutdown);

        AppLogger.Info("Config loaded");
        AppLogger.Info("Scheduler started");

        // Step 4: Start the scheduling loop.
        _scheduler.Start();

        // Step 5: Final tray refresh to ensure consistency.
        RefreshTray();
    }

    // ── Config application ───────────────────────────────────────

    /// <summary>
    /// Applies a new config snapshot from tray menu actions (not Settings Save).
    /// SettingsWindow performs its own single save transaction.
    /// </summary>
    private void ApplyConfig(AppConfig newConfig)
    {
        _store.Save(newConfig);
        _config = newConfig;

        _scheduler.UpdatePlan(newConfig.Shutdown);

        RefreshTray();
    }

    // ── Tooltip / ContextMenu ────────────────────────────────────

    private string BuildTooltip()
    {
        if (!_config.Shutdown.Enabled)
            return "ShutdownGuard — 已停用";

        var next = _scheduler.NextReminder;
        if (next is null)
            return "ShutdownGuard — 已停用";

        var dryLabel = _config.Shutdown.DryRun ? " [安全测试]" : "";
        return $"ShutdownGuard — 下次提醒: {next:HH:mm}{dryLabel}";
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var header = new TextBlock
        {
            Text = "ShutdownGuard",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13
        };
        menu.Items.Add(new MenuItem
        {
            Header = header,
            IsEnabled = false,
            StaysOpenOnClick = true
        });

        menu.Items.Add(new Separator());

        var statusText = _config.Shutdown.Enabled ? "已启用" : "已停用";
        menu.Items.Add(new MenuItem
        {
            Header = $"状态：{statusText}",
            IsEnabled = false
        });

        var nextText = SettingsViewModel.FormatNextReminder(
            _scheduler.NextReminder, _config.Shutdown.Enabled);
        menu.Items.Add(new MenuItem
        {
            Header = $"下次提醒：{nextText}",
            IsEnabled = false
        });

        menu.Items.Add(new MenuItem
        {
            Header = $"固定关机：{ShutdownPolicy.FixedShutdownTime:HH:mm}",
            IsEnabled = false
        });

        menu.Items.Add(new MenuItem
        {
            Header = _config.Shutdown.DryRun ? "安全测试：开启" : "安全测试：关闭",
            IsEnabled = false
        });

        menu.Items.Add(new Separator());

        var settingsItem = new MenuItem { Header = "打开设置" };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        var toggleItem = new MenuItem
        {
            Header = _config.Shutdown.Enabled ? "停用自动关机" : "启用自动关机"
        };
        toggleItem.Click += (_, _) => ToggleEnabled();
        menu.Items.Add(toggleItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem
        {
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new SymbolIcon { Symbol = SymbolRegular.SignOut24, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) },
                    new TextBlock { Text = "退出", VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        exitItem.Click += async (_, _) => await ShutdownAppAsync();
        menu.Items.Add(exitItem);

        return menu;
    }

    // ── Settings window ──────────────────────────────────────────

    private void OpenSettings()
    {
        if (_settingsWindow is { } existing)
        {
            existing.Activate();
            existing.Focus();
            return;
        }

        _settingsWindow = new SettingsWindow(_scheduler, _store, _config);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;

        // Saved: reload in-memory config + refresh tray ONLY.
        // Do NOT call ConfigStore.Save or Scheduler.UpdatePlan again
        // (SettingsWindow already completed the single save transaction).
        _settingsWindow.Saved += (_, _) =>
        {
            _config = _store.Load();
            RefreshTray();
        };
        _settingsWindow.Show();
    }

    // ── Tray actions ─────────────────────────────────────────────

    private void ToggleEnabled()
    {
        var newEnabled = !_config.Shutdown.Enabled;

        var updatedPlan = new ShutdownPlan
        {
            Enabled = newEnabled,
            ReminderStartTime = _config.Shutdown.ReminderStartTime,
            DryRun = _config.Shutdown.DryRun
        };

        var updatedConfig = new AppConfig
        {
            RunAtStartup = _config.RunAtStartup,
            Shutdown = updatedPlan
        };

        ApplyConfig(updatedConfig);

        var status = newEnabled ? "Enabled" : "Disabled";
        AppLogger.Info($"Scheduler {status} (via tray)");
    }

    // ── Events / Refresh ─────────────────────────────────────────

    private void OnNextReminderChanged(DateTimeOffset? next)
    {
        if (_disposed)
            return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed)
                return;

            RefreshTray();
        });
    }

    private void RefreshTray()
    {
        if (!_trayInitialized || _disposed || _trayIcon is null)
            return;

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

        _scheduler.NextReminderChanged -= OnNextReminderChanged;
        await _scheduler.DisposeAsync();
        _trayIcon?.Dispose();
    }
}
