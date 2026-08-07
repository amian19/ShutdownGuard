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
    private readonly ShutdownExecutionCoordinator _execution;
    private readonly MutableAppConfigProvider _configProvider;
    private readonly FileDailyCancellationStore _cancellationStore = new();
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
        _session = new ReminderSessionController(_clock, _cancellationStore);
        _configProvider = new MutableAppConfigProvider(new AppConfig());
        _execution = new ShutdownExecutionCoordinator(
            _clock,
            _configProvider,
            _cancellationStore,
            dryRunExecutor: new DryRunShutdownExecutor(),
            realExecutor: new WindowsShutdownExecutor());

        _scheduler.NextReminderChanged += OnNextReminderChanged;
        _scheduler.ReminderWindowStarted += OnReminderWindowStarted;
        _session.StateChanged += OnSessionStateChanged;
        _session.ShutdownDue += OnShutdownDue;
        _execution.StateChanged += OnExecutionStateChanged;
    }

    public void Start()
    {
        _config = _store.Load();
        _configProvider.Update(_config);
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
        ShutdownPolicy.ApplyDebugFixedShutdownOverride(_config.Shutdown.DebugFixedShutdownTime);
        _scheduler.UpdatePlan(_config.Shutdown);

        AppLogger.Info("Config loaded");
        AppLogger.Info("Scheduler started");

        // Product rule: auto-shutdown is always on; skip-today uses state.json.
        if (!_config.Shutdown.Enabled)
        {
            _config.Shutdown.Enabled = true;
            try { _store.Save(_config); } catch { /* best-effort */ }
            _configProvider.Update(_config);
        }

        _scheduler.Start();

        // Restart with today already cancelled → no catch-up; NextReminder = tomorrow.
        if (IsTodayCancelled())
            _scheduler.MarkTodayReminderHandled();

        EnsureForcedAutostart();
        RefreshTray();
    }

    private void EnsureForcedAutostart()
    {
        try
        {
            AutostartManager.SetEnabled(true);
            if (!_config.RunAtStartup)
            {
                _config.RunAtStartup = true;
                _store.Save(_config);
                _configProvider.Update(_config);
            }

            AppLogger.Info("Autostart forced on (HKCU\\...\\Run\\ShutdownGuard)");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to force autostart: {ex.Message}");
        }
    }

    private void ApplyConfig(AppConfig newConfig)
    {
        _store.Save(newConfig);
        _config = newConfig;
        _configProvider.Update(newConfig);

        _session.UpdateDryRunDisplay(newConfig.Shutdown.DryRun);
        ShutdownPolicy.ApplyDebugFixedShutdownOverride(newConfig.Shutdown.DebugFixedShutdownTime);
        _scheduler.UpdatePlan(newConfig.Shutdown);

        // Enabled is always forced on; skip-today is handled via CancelToday / RestoreToday.
        RefreshTray();
    }

    private string BuildTooltip()
    {
        var exec = _execution.Current;
        if (exec.Phase is ShutdownExecutionPhase.DryRunCompleted
            or ShutdownExecutionPhase.Blocked
            or ShutdownExecutionPhase.Failed
            or ShutdownExecutionPhase.RealShutdownRequested)
        {
            return $"ShutdownGuard — {exec.Message}";
        }

        if (IsTodayCancelled())
            return "ShutdownGuard — 今日不关机";

        var session = _session.Current;
        if (session.Phase == ReminderSessionPhase.Active)
            return $"ShutdownGuard — 提醒中，{session.FixedShutdownAt:HH:mm} 关机";

        if (session.Phase == ReminderSessionPhase.Cancelled)
            return "ShutdownGuard — 今日不关机";

        if (session.Phase == ReminderSessionPhase.Due)
            return "ShutdownGuard — 已到达关机时间";

        var next = _scheduler.NextReminder;
        if (next is null)
            return "ShutdownGuard — 等待计划";

        return $"ShutdownGuard — 下次提醒: {next:HH:mm}";
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(new MenuItem
        {
            Header = new TextBlock
            {
                Text = "ShutdownGuard",
                FontWeight = FontWeights.SemiBold,
                FontSize = 13
            },
            IsEnabled = false,
            StaysOpenOnClick = true
        });

        menu.Items.Add(new Separator());

        var statusText = IsTodayCancelled() ? "今日不关机" : "今日将关机";
        var session = _session.Current;
        var exec = _execution.Current;

        string detail;
        if (exec.Phase is ShutdownExecutionPhase.DryRunCompleted
            or ShutdownExecutionPhase.Blocked
            or ShutdownExecutionPhase.Failed
            or ShutdownExecutionPhase.RealShutdownRequested
            or ShutdownExecutionPhase.Executing)
        {
            detail = exec.Message;
        }
        else
        {
            detail = session.Phase switch
            {
                ReminderSessionPhase.Active => $"提醒中 → {session.FixedShutdownAt:HH:mm}",
                ReminderSessionPhase.Cancelled => "已取消今日",
                ReminderSessionPhase.Due => "已到关机时间",
                _ when IsTodayCancelled() => "已取消今日",
                _ => SettingsViewModel.FormatNextReminder(
                    _scheduler.NextReminder, enabled: true)
            };
        }

        menu.Items.Add(new MenuItem
        {
            Header = $"状态：{statusText} — {detail}",
            IsEnabled = false
        });

        menu.Items.Add(new MenuItem
        {
            Header = $"固定关机：{ShutdownPolicy.EffectiveFixedShutdownTime:HH:mm}",
            IsEnabled = false
        });

        menu.Items.Add(new Separator());

        if (session.Phase == ReminderSessionPhase.Active)
        {
            var showReminder = new MenuItem { Header = "显示提醒窗口" };
            showReminder.Click += (_, _) => ShowReminderWindow();
            menu.Items.Add(showReminder);
        }

        var settingsItem = new MenuItem { Header = "打开设置" };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        if (IsTodayCancelled())
        {
            var restoreItem = new MenuItem { Header = "恢复今日关机" };
            restoreItem.Click += (_, _) => RestoreTodayShutdown();
            menu.Items.Add(restoreItem);
        }
        else
        {
            var skipItem = new MenuItem { Header = "今日不关机" };
            skipItem.Click += (_, _) => SkipTodayShutdown();
            menu.Items.Add(skipItem);
        }

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

        // Align NextReminder with cancel before opening settings (shows 明天, not 今天).
        if (IsTodayCancelled())
            _scheduler.MarkTodayReminderHandled();

        _settingsWindow = new SettingsWindow(
            _scheduler,
            _store,
            _config,
            todayCancelled: IsTodayCancelled(),
            isTodayCancelled: IsTodayCancelled,
            applyTodayShutdown: ApplyTodayShutdownFromSettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Saved += (_, _) =>
        {
            _config = _store.Load();
            _configProvider.Update(_config);
            _session.UpdateDryRunDisplay(_config.Shutdown.DryRun);
            ShutdownPolicy.ApplyDebugFixedShutdownOverride(_config.Shutdown.DebugFixedShutdownTime);
            RefreshTray();
        };
        _settingsWindow.Show();
    }

    private void ApplyTodayShutdownFromSettings(bool todayWillShutdown)
    {
        if (todayWillShutdown)
        {
            if (IsTodayCancelled())
                RestoreTodayShutdown();
        }
        else if (!IsTodayCancelled())
        {
            SkipTodayShutdown();
        }
    }

    private bool IsTodayCancelled()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return _cancellationStore.ReadCancellationState(today).Status
            == DailyCancellationStatus.Cancelled;
    }

    private void SkipTodayShutdown()
    {
        // Works before or during the reminder window — persists cancel for the local day.
        _session.SuppressTodayAndDismiss();
        _scheduler.MarkTodayReminderHandled();
        _reminderWindow?.Hide();
        AppLogger.Info("Today shutdown skipped via tray (今日不关机)");
        RefreshTray();
    }

    private void RestoreTodayShutdown()
    {
        try
        {
            _cancellationStore.ClearCancelledDate();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to restore today shutdown: {ex.Message}");
            return;
        }

        // Drop in-memory cancelled/idle session so a re-entry can begin cleanly.
        _session.Dismiss();
        _scheduler.ClearTodayReminderHandled();
        AppLogger.Info("Today shutdown restored via tray (恢复今日关机)");
        RefreshTray();
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

    private void OnReminderWindowStarted(DateTimeOffset occurrence)
    {
        if (_disposed) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;

            _session.BeginSession(occurrence, _config.Shutdown.DryRun);
            // Only Active pops the toast. Cancelled (today already cancelled/disabled) stays silent.
            if (_session.Current.Phase == ReminderSessionPhase.Active)
                ShowReminderWindow();
            RefreshTray();
        });
    }

    private void OnShutdownDue(ShutdownDueInfo due)
    {
        // Wiring only — all safety/executor decisions live in the coordinator.
        _execution.HandleShutdownDue(due);
    }

    private void OnExecutionStateChanged(ShutdownExecutionSnapshot snapshot)
    {
        if (_disposed) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;

            if (snapshot.Phase is ShutdownExecutionPhase.Executing
                or ShutdownExecutionPhase.DryRunCompleted
                or ShutdownExecutionPhase.Blocked
                or ShutdownExecutionPhase.Failed
                or ShutdownExecutionPhase.RealShutdownRequested)
            {
                // Hide reminder window once execution layer owns the outcome.
                _reminderWindow?.Hide();
            }

            RefreshTray();
        });
    }

    private void OnSessionStateChanged(ReminderSessionSnapshot snapshot)
    {
        if (_disposed) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_disposed) return;

            if (snapshot.Phase == ReminderSessionPhase.Due
                || snapshot.Phase == ReminderSessionPhase.Idle)
            {
                _reminderWindow?.Hide();
            }

            if (snapshot.Phase == ReminderSessionPhase.Cancelled)
            {
                // Skip the rest of today's scheduler catch-up; tray/settings show tomorrow.
                _scheduler.MarkTodayReminderHandled();
                _reminderWindow?.Hide();
            }

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
        await _execution.DisposeAsync();
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
        _execution.StateChanged -= OnExecutionStateChanged;

        await _session.DisposeAsync();
        await _scheduler.DisposeAsync();
        await _execution.DisposeAsync();
        _trayIcon?.Dispose();
    }
}
