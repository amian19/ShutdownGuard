using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ShutdownGuard.Models;
using ShutdownGuard.Native;
using ShutdownGuard.Services;
using Wpf.Ui.Controls;

namespace ShutdownGuard.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly ConfigStore _store;
    private AppConfig _savedConfig;
    private bool _suspendChangeEvents;
    private readonly DispatcherTimer _statusTimer;

    private static readonly Color ActiveColor = Color.FromRgb(0x22, 0xC5, 0x5E);
    private static readonly Color DisabledColor = Color.FromRgb(0x9C, 0xA3, 0xAF);
    private static readonly Brush ActiveBrush = new SolidColorBrush(ActiveColor);
    private static readonly Brush DisabledBrush = new SolidColorBrush(DisabledColor);
    private static readonly Brush ActiveBgBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0x22, 0xC5, 0x5E));
    private static readonly Brush DisabledBgBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0x9C, 0xA3, 0xAF));

    public event EventHandler<AppConfig>? ConfigSaved;

    public SettingsWindow(ConfigStore store, AppConfig config)
    {
        InitializeComponent();
        _store = store;
        _savedConfig = config.Clone();
        LoadIntoUi(_savedConfig);

        HoursBox.ValueChanged += OnAnyChanged;
        MinutesBox.ValueChanged += OnAnyChanged;
        SecondsBox.ValueChanged += OnAnyChanged;
        WarningSecondsBox.ValueChanged += OnAnyChanged;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => UpdateLiveBanner();
        _statusTimer.Start();

        // Pause the 1 Hz banner when the user can't see it (minimized or window hidden).
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) _statusTimer.Stop();
            else if (IsVisible) _statusTimer.Start();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && WindowState != WindowState.Minimized) _statusTimer.Start();
            else _statusTimer.Stop();
        };
        Closed += (_, _) => _statusTimer.Stop();

        UpdateAll();
    }

    private void LoadIntoUi(AppConfig cfg)
    {
        _suspendChangeEvents = true;

        WarningSecondsBox.Value = cfg.WarningSeconds;
        StartupToggle.IsChecked = cfg.RunAtStartup;
        SkipFullscreenToggle.IsChecked = cfg.SkipWhenFullscreen;
        SkipPresentingToggle.IsChecked = cfg.SkipWhenPresenting;

        var ts = TimeSpan.FromSeconds(Math.Max(1, cfg.IdleSeconds));
        HoursBox.Value = (int)ts.TotalHours;
        MinutesBox.Value = ts.Minutes;
        SecondsBox.Value = ts.Seconds;

        ActionCombo.SelectedItem = null;
        foreach (ComboBoxItem item in ActionCombo.Items)
        {
            if ((string?)item.Tag == cfg.Action.ToString())
            {
                ActionCombo.SelectedItem = item;
                break;
            }
        }
        if (ActionCombo.SelectedItem == null) ActionCombo.SelectedIndex = 0;

        _suspendChangeEvents = false;
    }

    private AppConfig BuildConfigFromUi()
    {
        return new AppConfig
        {
            Enabled = _savedConfig.Enabled, // not in this UI; toggled separately
            IdleSeconds = GetThresholdSeconds(),
            Action = GetSelectedAction(),
            WarningSeconds = (int)(WarningSecondsBox.Value ?? 30),
            RunAtStartup = StartupToggle.IsChecked ?? false,
            SkipWhenFullscreen = SkipFullscreenToggle.IsChecked ?? true,
            SkipWhenPresenting = SkipPresentingToggle.IsChecked ?? true,
            ScanIntervalSeconds = _savedConfig.ScanIntervalSeconds
        };
    }

    private PowerAction GetSelectedAction()
    {
        var tag = (ActionCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "Shutdown";
        return Enum.TryParse<PowerAction>(tag, out var a) ? a : PowerAction.Shutdown;
    }

    private const int MinThresholdSeconds = 5;
    private const int HighFrequencyScanThresholdSec = 5;

    private int GetThresholdSeconds()
    {
        var h = (int)(HoursBox.Value ?? 0);
        var m = (int)(MinutesBox.Value ?? 0);
        var s = (int)(SecondsBox.Value ?? 0);
        return h * 3600 + m * 60 + s;
    }

    private void OnAnyChanged(object sender, RoutedEventArgs e)
    {
        if (_suspendChangeEvents) return;
        UpdatePendingState();
    }

    private void UpdateAll()
    {
        UpdateBannerForSaved();
        UpdateLiveBanner();
        UpdatePendingState();
    }

    /// <summary>
    /// Banner top section — reflects what is currently SAVED, not what's in the UI.
    /// </summary>
    private void UpdateBannerForSaved()
    {
        var thresholdText = FormatDuration(TimeSpan.FromSeconds(_savedConfig.IdleSeconds));
        var action = _savedConfig.Action;
        var actionLower = action.ToString().ToLowerInvariant();

        // Always-visible scan-frequency line + high-frequency warning chip.
        var scanSec = Math.Clamp(_savedConfig.ScanIntervalSeconds, 1, 3600);
        ScanFrequencyText.Text = $"Scanning every {FormatDuration(TimeSpan.FromSeconds(scanSec))}";
        HighFreqWarning.Visibility = scanSec < HighFrequencyScanThresholdSec
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (_savedConfig.Enabled)
        {
            StatusIcon.Symbol = SymbolRegular.CheckmarkCircle24;
            StatusIcon.Foreground = ActiveBrush;
            StatusIconBg.Background = ActiveBgBrush;
            StatusProgress.Foreground = ActiveBrush;

            StatusHeadline.Text = "Active";
            StatusDescription.Text = $"{action} when idle for {thresholdText}";

            EnableButton.Appearance = ControlAppearance.Caution;
            EnableButtonText.Text = "Disable";
            EnableButtonIcon.Symbol = SymbolRegular.Pause24;
        }
        else
        {
            StatusIcon.Symbol = SymbolRegular.PauseCircle24;
            StatusIcon.Foreground = DisabledBrush;
            StatusIconBg.Background = DisabledBgBrush;
            StatusProgress.Foreground = DisabledBrush;

            StatusHeadline.Text = "Paused";
            StatusDescription.Text = $"Would {actionLower} when idle for {thresholdText}";

            EnableButton.Appearance = ControlAppearance.Primary;
            EnableButtonText.Text = "Enable";
            EnableButtonIcon.Symbol = SymbolRegular.Play24;
        }
    }

    /// <summary>
    /// Per-second live update — only updates the idle progress line, never the headline/description.
    /// </summary>
    private void UpdateLiveBanner()
    {
        if (_savedConfig.Enabled)
        {
            var thresholdSec = _savedConfig.IdleSeconds;
            var idle = NativeMethods.GetIdleTime();
            var idleSec = (int)Math.Min(idle.TotalSeconds, int.MaxValue);
            var remainingSec = Math.Max(0, thresholdSec - idleSec);
            var pct = thresholdSec > 0
                ? Math.Clamp(idleSec * 100.0 / thresholdSec, 0, 100)
                : 0;

            StatusLive.Text =
                $"Idle {FormatDuration(TimeSpan.FromSeconds(idleSec))}  ·  Fires in {FormatDuration(TimeSpan.FromSeconds(remainingSec))}";
            StatusProgress.Value = pct;
        }
        else
        {
            StatusLive.Text = "Idle monitoring is off";
            StatusProgress.Value = 0;
        }
    }

    /// <summary>
    /// Footer pending-changes summary + Save button enable state. Save is gated on both
    /// dirty-vs-saved AND validity (idle threshold must be at least <see cref="MinThresholdSeconds"/>).
    /// </summary>
    private void UpdatePendingState()
    {
        var current = BuildConfigFromUi();
        var dirty = !current.ValueEquals(_savedConfig);
        var validationError = ValidateConfig(current);
        var valid = validationError == null;

        SaveButton.IsEnabled = dirty && valid;

        if (!valid)
        {
            PendingHeadline.Text = "Can't save — fix the highlighted field";
            PendingDetail.Text = validationError!;
            return;
        }

        if (!dirty)
        {
            PendingHeadline.Text = "No pending changes";
            PendingDetail.Text = SummariseConfig(_savedConfig);
            return;
        }

        PendingHeadline.Text = "Click Save to apply";
        PendingDetail.Text = SummariseConfig(current);
    }

    private static string? ValidateConfig(AppConfig cfg)
    {
        if (cfg.IdleSeconds < MinThresholdSeconds)
            return $"Idle threshold must be at least {MinThresholdSeconds} seconds.";
        return null;
    }

    private static string SummariseConfig(AppConfig cfg)
    {
        var threshold = FormatDuration(TimeSpan.FromSeconds(cfg.IdleSeconds));
        var action = cfg.Action.ToString();
        return $"{action} after {threshold} idle, with a {cfg.WarningSeconds}s warning";
    }

    private static string FormatDuration(TimeSpan ts) => Format.Duration(ts);

    private void EnableButton_Click(object sender, RoutedEventArgs e)
    {
        // Toggle the Enabled flag only — don't apply unsaved UI edits.
        _savedConfig.Enabled = !_savedConfig.Enabled;
        _store.Save(_savedConfig);
        ConfigSaved?.Invoke(this, _savedConfig.Clone());
        UpdateBannerForSaved();
        UpdateLiveBanner();
    }

    public void ApplyExternalConfig(AppConfig cfg)
    {
        _savedConfig = cfg.Clone();
        UpdateBannerForSaved();
        UpdateLiveBanner();
        UpdatePendingState();
    }

    private void AppSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var current = _store.Load();
        var win = new AppSettingsWindow(_store, current) { Owner = this };
        win.ConfigSaved += (_, cfg) =>
        {
            _savedConfig = cfg.Clone();
            ConfigSaved?.Invoke(this, _savedConfig.Clone());
            UpdateBannerForSaved();
            UpdateLiveBanner();
            UpdatePendingState();
        };
        win.ShowDialog();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var built = BuildConfigFromUi();
        // Preserve the saved Enabled flag (toggled separately via EnableButton)
        built.Enabled = _savedConfig.Enabled;
        // Preserve ScanIntervalSeconds (App Settings owns it)
        built.ScanIntervalSeconds = _savedConfig.ScanIntervalSeconds;

        _savedConfig = built;
        _store.Save(_savedConfig);
        AutostartManager.SetEnabled(_savedConfig.RunAtStartup);
        ConfigSaved?.Invoke(this, _savedConfig.Clone());

        UpdateBannerForSaved();
        UpdateLiveBanner();
        UpdatePendingState();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
