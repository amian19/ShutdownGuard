using System.Windows;
using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using ShutdownGuard.ViewModels;

namespace ShutdownGuard.Views;

public partial class SettingsWindow : Window
{
    private readonly SchedulerService _scheduler;
    private readonly ConfigStore _store;
    private readonly AppConfig _originalConfig;
    private readonly Func<bool> _isTodayCancelled;
    private readonly Action<bool> _applyTodayShutdown;
    private readonly SettingsViewModel _viewModel = new();
    private bool _isSaving;

    /// <summary>
    /// Raised after a successful Save operation.
    /// The caller (TrayApp) should reload config and refresh the tray.
    /// Must NOT trigger another ConfigStore.Save.
    /// </summary>
    public event EventHandler? Saved;

    public SettingsWindow(
        SchedulerService scheduler,
        ConfigStore store,
        AppConfig config,
        bool todayCancelled = false,
        Func<bool>? isTodayCancelled = null,
        Action<bool>? applyTodayShutdown = null)
    {
        InitializeComponent();

        _scheduler = scheduler;
        _store = store;
        _originalConfig = config;
        _isTodayCancelled = isTodayCancelled ?? (() => todayCancelled);
        _applyTodayShutdown = applyTodayShutdown ?? (_ => { });

        DataContext = _viewModel;

        // Reminder hours 00..23 (validated against fixed 22:00); minutes 00..59
        for (int h = 0; h <= 23; h++)
            HourComboBox.Items.Add(h);
        for (int m = 0; m < 60; m++)
            MinuteComboBox.Items.Add(m);

        _viewModel.Load(config, scheduler.NextReminder, todayCancelled);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Guard: one click → one save transaction.
        if (_isSaving)
            return;

        if (!_viewModel.TryValidate(out var validationError))
        {
            MessageBox.Show(
                validationError ?? "提醒开始时间无效。",
                "ShutdownGuard — 验证失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _isSaving = true;
        SaveButton.IsEnabled = false;

        try
        {
            PersistOnce();
        }
        finally
        {
            _isSaving = false;
            SaveButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Single save transaction:
    /// 1 ConfigStore.Save + 1 Scheduler.UpdatePlan + 1 Settings saved log.
    /// </summary>
    private void PersistOnce()
    {
        var plan = _viewModel.ToShutdownPlan();
        var desiredConfig = _viewModel.ToAppConfig();

        // Product rule: autostart is mandatory — always (re)apply HKCU Run.
        try
        {
            AutostartManager.SetEnabled(true);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Autostart force-enable failed: {ex.Message}");
            MessageBox.Show(
                $"强制开启开机启动失败：\n{ex.Message}\n\n设置未保存。",
                "ShutdownGuard — 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            _store.Save(desiredConfig);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Config save failed: {ex.Message}");
            MessageBox.Show(
                $"保存设置失败：\n{ex.Message}",
                "ShutdownGuard — 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Persistence succeeded — update runtime scheduler once.
        _scheduler.UpdatePlan(plan);

        // Apply today ON/OFF after plan update (skip or restore for local calendar day).
        _applyTodayShutdown(_viewModel.TodayWillShutdown);

        var cancelled = _isTodayCancelled();
        if (cancelled)
            _scheduler.MarkTodayReminderHandled();

        _viewModel.NextReminderText = SettingsViewModel.FormatNextReminder(
            _scheduler.NextReminder, plan.Enabled, cancelled);
        _viewModel.ScheduledShutdownText = SettingsViewModel.FormatScheduledShutdown(
            _scheduler.NextReminder, plan.Enabled, cancelled);

        AppLogger.Info(
            $"Settings saved: Enabled={plan.Enabled}, " +
            $"ReminderStart={plan.ReminderStartTime:HH:mm}, " +
            $"TodayWillShutdown={_viewModel.TodayWillShutdown}, RunAtStartup=true");

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
