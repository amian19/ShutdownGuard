using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
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
        var executor = CreateExecutor(_config.Shutdown.DryRun);
        _scheduler = new SchedulerService(new SystemClock(), executor);
        // Event subscribed in constructor but guarded by _trayInitialized / _disposed
        _scheduler.NextShutdownChanged += OnNextShutdownChanged;
    }

    public void Start()
    {
        // Step 1: Load config from disk
        _config = _store.Load();
        AppLogger.Init();
        AppLogger.Info("Application started");

        // Step 2: Initialize tray UI FIRST — before any scheduler event can fire.
        // This guarantees that OnNextShutdownChanged will find _trayIcon non-null.
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = BuildTooltip(),
            IconSource = _iconSource,
            ContextMenu = BuildContextMenu()
        };
        _trayInitialized = true;

        // Step 3: Now safe to update the scheduler — events will find UI ready.
        var executor = CreateExecutor(_config.Shutdown.DryRun);
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
    /// Applies a new config snapshot: persists to disk, updates scheduler,
    /// replaces the in-memory config reference, and refreshes the tray.
    /// This is the single entry point for config changes — both from
    /// SettingsWindow and from tray menu actions.
    /// </summary>
    private void ApplyConfig(AppConfig newConfig)
    {
        _store.Save(newConfig);
        _config = newConfig;

        _scheduler.UpdatePlan(newConfig.Shutdown);

        var executor = CreateExecutor(newConfig.Shutdown.DryRun);
        _scheduler.UpdateExecutor(executor);

        RefreshTray();
    }

    // ── Tooltip / ContextMenu ────────────────────────────────────

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

        // Dry Run indicator
        var dryRunItem = new MenuItem
        {
            Header = _config.Shutdown.DryRun ? "Dry Run: On" : "Dry Run: Off",
            IsEnabled = false
        };
        menu.Items.Add(dryRunItem);

        menu.Items.Add(new Separator());

        // Open Settings
        var settingsItem = new MenuItem
        {
            Header = "Open Settings"
        };
        settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(settingsItem);

        // Toggle Enable/Disable
        var toggleItem = new MenuItem
        {
            Header = _config.Shutdown.Enabled ? "Disable scheduled shutdown" : "Enable scheduled shutdown"
        };
        toggleItem.Click += (_, _) => ToggleEnabled();
        menu.Items.Add(toggleItem);

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

    // ── Settings window ──────────────────────────────────────────

    private void OpenSettings()
    {
        // Single-instance: if window is already open, activate it
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
            // Reload config from disk so TrayApp's _config is fresh
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
            ShutdownTime = _config.Shutdown.ShutdownTime,
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

    public void SetDryRun(bool dryRun)
    {
        var updatedPlan = new ShutdownPlan
        {
            Enabled = _config.Shutdown.Enabled,
            ShutdownTime = _config.Shutdown.ShutdownTime,
            DryRun = dryRun
        };

        var updatedConfig = new AppConfig
        {
            RunAtStartup = _config.RunAtStartup,
            Shutdown = updatedPlan
        };

        ApplyConfig(updatedConfig);

        AppLogger.Info($"DryRun set to {dryRun} (via tray)");
    }

    // ── Events / Refresh ─────────────────────────────────────────

    /// <summary>
    /// Scheduler event handler. Guarded against firing before tray is ready
    /// and after disposal. Delegates all UI updates to RefreshTray().
    /// </summary>
    private void OnNextShutdownChanged(DateTimeOffset? next)
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

    /// <summary>
    /// The single entry point for refreshing all tray UI state.
    /// Safe to call at any point in the lifecycle — returns early if
    /// the tray is not yet initialized or already disposed.
    /// </summary>
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

        // Unsubscribe FIRST so no more events arrive during disposal.
        _scheduler.NextShutdownChanged -= OnNextShutdownChanged;
        await _scheduler.DisposeAsync();
        _trayIcon?.Dispose();
    }

    private static IShutdownExecutor CreateExecutor(bool dryRun)
    {
        return dryRun ? new DryRunShutdownExecutor() : new WindowsShutdownExecutor();
    }
}
