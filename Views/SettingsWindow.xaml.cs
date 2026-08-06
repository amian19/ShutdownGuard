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
        // Step 1: Create new ShutdownPlan snapshot (immutable-ish)
        var plan = _viewModel.ToShutdownPlan();

        // Step 2: Build AppConfig
        var config = _viewModel.ToAppConfig();

        // Step 3: Save to disk
        _store.Save(config);

        // Step 4: Update Windows Autostart
        try
        {
            AutostartManager.SetEnabled(config.RunAtStartup);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to update Windows startup setting:\n{ex.Message}",
                "ShutdownGuard — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            // Don't crash — continue with other save steps
        }

        // Step 5: Notify running scheduler
        _scheduler.UpdatePlan(plan);

        // Step 6: Update executor based on DryRun
        IShutdownExecutor executor = plan.DryRun
            ? new DryRunShutdownExecutor()
            : new WindowsShutdownExecutor();
        _scheduler.UpdateExecutor(executor);

        // Step 7: Refresh Next shutdown display
        _viewModel.NextShutdownText = SettingsViewModel.FormatNextShutdown(
            _scheduler.NextShutdown, plan.Enabled);

        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // Close without saving — working copy is discarded.
        // Original config, scheduler, and registry are untouched.
        Close();
    }
}
