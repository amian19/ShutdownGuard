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
    private readonly SettingsViewModel _viewModel = new();

    /// <summary>
    /// Raised after a successful Save operation.
    /// The caller (TrayApp) should reload config and refresh the tray.
    /// </summary>
    public event EventHandler? Saved;

    public SettingsWindow(SchedulerService scheduler, ConfigStore store, AppConfig config)
    {
        InitializeComponent();

        _scheduler = scheduler;
        _store = store;
        _originalConfig = config;

        DataContext = _viewModel;

        // Populate ComboBox items
        for (int h = 0; h < 24; h++)
            HourComboBox.Items.Add(h);
        for (int m = 0; m < 60; m++)
            MinuteComboBox.Items.Add(m);

        // Load working copy from current config
        _viewModel.Load(config, scheduler.NextShutdown);

        // Subscribe to DryRun changes to show/hide warning
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.DryRun))
            {
                DryRunWarningText.Visibility = _viewModel.DryRun
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Step 1: Create new ShutdownPlan and AppConfig snapshots
        var plan = _viewModel.ToShutdownPlan();
        var desiredConfig = _viewModel.ToAppConfig();

        // Step 2: If RunAtStartup changed, update registry FIRST.
        // If registry fails, abort the entire save — do not persist anything.
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
                return; // Abort — nothing persisted
            }
        }

        // Step 3: Persist config to disk.
        // If this fails and we changed the registry, try to roll back.
        try
        {
            _store.Save(desiredConfig);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Config save failed: {ex.Message}");

            // Best-effort rollback: restore original RunAtStartup in registry
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
            return; // Abort — nothing persisted (registry rolled back if needed)
        }

        // Step 4: Persistence succeeded — now update runtime state.
        // Scheduler only receives config that has been successfully saved to disk.

        _scheduler.UpdatePlan(plan);

        // Step 5: Update executor based on DryRun
        IShutdownExecutor executor = plan.DryRun
            ? new DryRunShutdownExecutor()
            : new WindowsShutdownExecutor();
        _scheduler.UpdateExecutor(executor);

        // Step 6: Refresh Next shutdown display
        _viewModel.NextShutdownText = SettingsViewModel.FormatNextShutdown(
            _scheduler.NextShutdown, plan.Enabled);

        AppLogger.Info($"Settings saved: Enabled={plan.Enabled}, Time={plan.ShutdownTime}, DryRun={plan.DryRun}, RunAtStartup={desiredConfig.RunAtStartup}");

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Close without saving — working copy is discarded.
        // Original config, scheduler, and registry are untouched.
        Close();
    }
}
