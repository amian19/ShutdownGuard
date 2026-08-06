using System.ComponentModel;
using System.Windows;
using ShutdownGuard.Services;
using ShutdownGuard.ViewModels;

namespace ShutdownGuard.Views;

/// <summary>
/// Display-only shell for today's reminder session.
/// </summary>
public partial class ReminderWindow : Window
{
    private readonly ReminderSessionController _session;
    private readonly ReminderViewModel _viewModel = new();

    public ReminderWindow(ReminderSessionController session)
    {
        InitializeComponent();
        _session = session;
        DataContext = _viewModel;

        _viewModel.Apply(session.Current);
        _session.StateChanged += OnSessionStateChanged;
        Closed += (_, _) => _session.StateChanged -= OnSessionStateChanged;
    }

    private void OnSessionStateChanged(Models.ReminderSessionSnapshot snapshot)
    {
        Dispatcher.Invoke(() => _viewModel.Apply(snapshot));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_session.CancelToday())
            return;

        MessageBox.Show(
            "取消本次关机失败：无法保存今日取消状态。\n请稍后重试。",
            "ShutdownGuard — 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void HideToTray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // X = hide to tray, not cancel, and not destroy the session.
        e.Cancel = true;
        Hide();
    }

    public void Reveal()
    {
        if (!IsVisible)
            Show();
        Activate();
        Focus();
    }
}
