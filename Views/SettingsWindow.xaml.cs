using System.Windows;
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
        Func<bool>? isTodayCancelled = null)
    {
        InitializeComponent();

        _scheduler = scheduler;
        _store = store;
        _originalConfig = config;
        _isTodayCancelled = isTodayCancelled ?? (() => todayCancelled);

        DataContext = _viewModel;

        // Hours 00..21 (must be < fixed shutdown 22:00); minutes 00..59
        for (int h = 0; h <= 21; h++)
            HourComboBox.Items.Add(h);
        for (int m = 0; m < 60; m++)
            MinuteComboBox.Items.Add(m);

        _viewModel.Load(config, scheduler.NextReminder, todayCancelled);

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.DryRun))
            {
                DryRunWarningText.Visibility = _viewModel.DryRun
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        };

        DryRunWarningText.Visibility = _viewModel.DryRun
            ? Visibility.Collapsed
            : Visibility.Visible;
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

        bool runAtStartupChanged = desiredConfig.RunAtStartup != _originalConfig.RunAtStartup;
        if (runAtStartupChanged)
        {
            try
            {
                AutostartManager.SetEnabled(desiredConfig.RunAtStartup);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Autostart update failed: {ex.Message}");
                MessageBox.Show(
                    $"更新 Windows 开机启动设置失败：\n{ex.Message}\n\n设置未保存。",
                    "ShutdownGuard — 错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        try
        {
            _store.Save(desiredConfig);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Config save failed: {ex.Message}");

            if (runAtStartupChanged)
            {
                try
                {
                    AutostartManager.SetEnabled(_originalConfig.RunAtStartup);
                }
                catch (Exception rollbackEx)
                {
                    AppLogger.Error($"Autostart rollback also failed: {rollbackEx.Message}");
                    MessageBox.Show(
                        $"保存设置失败。\n\n" +
                        $"配置文件保存错误：{ex.Message}\n" +
                        $"开机启动设置可能与程序配置不一致。\n" +
                        $"预期：{(_originalConfig.RunAtStartup ? "已启用" : "已停用")}",
                        "ShutdownGuard — 错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            MessageBox.Show(
                $"保存设置失败：\n{ex.Message}",
                "ShutdownGuard — 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Persistence succeeded — update runtime scheduler once.
        _scheduler.UpdatePlan(plan);

        // Today already cancelled: keep "明天" — do not let a ReminderStart change revive today.
        if (_isTodayCancelled())
            _scheduler.MarkTodayReminderHandled();

        var cancelled = _isTodayCancelled();
        _viewModel.NextReminderText = SettingsViewModel.FormatNextReminder(
            _scheduler.NextReminder, plan.Enabled, cancelled);
        _viewModel.ScheduledShutdownText = SettingsViewModel.FormatScheduledShutdown(
            _scheduler.NextReminder, plan.Enabled, cancelled);

        AppLogger.Info(
            $"Settings saved: Enabled={plan.Enabled}, " +
            $"ReminderStart={plan.ReminderStartTime:HH:mm}, " +
            $"DryRun={plan.DryRun}, RunAtStartup={desiredConfig.RunAtStartup}");

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
