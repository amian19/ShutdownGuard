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
    private readonly ReminderSessionController _session;
    private readonly SystemClock _clock = new();
    private TaskbarIcon? _trayIcon;
    private AppConfig _config = new();
    private SettingsWindow? _settingsWindow;
    private ReminderWindow? _reminderWindow;
    private bool _trayInitialized;
    private bool _disposed;

    private static readonly BitmapImage _iconSource =
        new(new Uri("pack://application:,,,/Assets/ShutdownGuard.ico", UriKind.Absolute));

    public TrayApp()
    {
        _scheduler = new SchedulerService(_clock);
        _session = new ReminderSessionController(_clock, new FileDailyCancellationStore());

        _scheduler.NextReminderChanged += OnNextReminderChanged;
        _scheduler.ReminderWindowStarted += OnReminderWindowStarted;
        _session.StateChanged += OnSessionStateChanged;
        _session.ShutdownDue += OnShutdownDue;
    }

    public void Start()
    {
        _config = _store.Load();
        AppLogger.Init();
        AppLogger.Info("Application started");

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = BuildTooltip(),
            IconSource = _iconSource,
            ContextMenu = BuildContextMenu()
        };
        _trayInitialized = true;

        _session.UpdateDryRunDisplay(_config.Shutdown.DryRun);
        _scheduler.UpdatePlan(_config.Shutdown);

        AppLogger.Info("Config loaded");
        AppLogger.Info("Scheduler started");

        _scheduler.Start();
        RefreshTray();
    }

    private void ApplyConfig(AppConfig newConfig)
    {
        _store.Save(newConfig);
        _config = newConfig;

        _session.UpdateDryRunDisplay(newConfig.Shutdown.DryRun);
        _scheduler.UpdatePlan(newConfig.Shutdown);

        if (!newConfig.Shutdown.Enabled)
            _session.Dismiss();

        RefreshTray();
    }

    private string BuildTooltip()
    {
        if (!_config.Shutdown.Enabled)
            return "ShutdownGuard — 已停用";

        var session = _session.Current;
        if (session.Phase == ReminderSessionPhase.Active)
        {
            var dry = _config.Shutdown.DryRun ? " [安全测试]" : "";
            return $"ShutdownGuard — 提醒中，{session.FixedShutdownAt:HH:mm} 关机{dry}";
        }

        if (session.Phase == ReminderSessionPhase.Cancelled)
            return "ShutdownGuard — 已取消本次关机";

        if (session.Phase == ReminderSessionPhase.Due)
            return "ShutdownGuard — 已到达关机时间";

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
        var session = _session.Current;
        string detail = session.Phase switch
        {
            ReminderSessionPhase.Active => $"提醒中 → {session.FixedShutdownAt:HH:mm}",
            ReminderSessionPhase.Cancelled => "已取消本次",
            ReminderSessionPhase.Due => "已到关机时间",
            _ => SettingsViewModel.FormatNextReminder(
                _scheduler.NextReminder, _config.Shutdown.Enabled)
        };

        menu.Items.Add(new MenuItem
        {
            Header = $"状态：{statusText} — {detail}",
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

        if (session.Phase is ReminderSessionPhase.Active
            or ReminderSessionPhase.Cancelled
            or ReminderSessionPhase.Due)
        {
            var showReminder = new MenuItem { Header = "显示提醒窗口" };
            showReminder.Click += (_, _) => ShowReminderWindow();
            menu.Items.Add(showReminder);
        }

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
        _settingsWindow.Saved += (_, _) =>
        {
            _config = _store.Load();
            _session.UpdateDryRunDisplay(_config.Shutdown.DryRun);
            if (!_config.Shutdown.Enabled)
                _session.Dismiss();
            RefreshTray();
        };
        _settingsWindow.Show();
    }

    private void ShowReminderWindow()
    {
        if (_reminderWindow is { } existing)
        {
            existing.Reveal();
            return;
        }

        _reminderWindow = new ReminderWindow(_session);
        _reminderWindow.Show();
    }

    private void ToggleEnabled()
    {
        var newEnabled = !_config.Shutdown.Enabled;

        var updatedConfig = new AppConfig
        {
            RunAtStartup = _config.RunAtStartup,
            Shutdown = new ShutdownPlan
            {
                Enabled = newEnabled,
                ReminderStartTime = _config.Shutdown.ReminderStartTime,
                DryRun = _config.Shutdown.DryRun
            }
        };

        ApplyConfig(updatedConfig);
        AppLogger.Info($"Scheduler {(newEnabled ? "Enabled" : "Disabled")} (via tray)");
    }

    private void OnReminderWindowStarted(DateTimeOffset occurrence)
    {
        if (_disposed) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;

            _session.BeginSession(occurrence, _config.Shutdown.DryRun);
            ShowReminderWindow();
            RefreshTray();
        });
    }

    private void OnShutdownDue(DateTimeOffset dueAt)
    {
        // M4: signal only. M4.5 will run IShutdownExecutor based on DryRun.
        AppLogger.Info($"Tray received ShutdownDue at {dueAt:yyyy-MM-dd HH:mm} (no executor in M4)");
    }

    private void OnSessionStateChanged(ReminderSessionSnapshot snapshot)
    {
        if (_disposed) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;
            RefreshTray();
        });
    }

    private void OnNextReminderChanged(DateTimeOffset? next)
    {
        if (_disposed) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;
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
        await _session.StopAsync();
        await _scheduler.StopAsync();
        Application.Current.Shutdown();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _scheduler.NextReminderChanged -= OnNextReminderChanged;
        _scheduler.ReminderWindowStarted -= OnReminderWindowStarted;
        _session.StateChanged -= OnSessionStateChanged;
        _session.ShutdownDue -= OnShutdownDue;

        await _session.DisposeAsync();
        await _scheduler.DisposeAsync();
        _trayIcon?.Dispose();
    }
}
