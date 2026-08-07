using System.ComponentModel;
using System.Windows;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using ShutdownGuard.ViewModels;

namespace ShutdownGuard.Views;

/// <summary>
/// Display-only toast shell for today's reminder session (bottom-right).
/// </summary>
public partial class ReminderWindow : Window
{
    private const double ScreenMargin = 16;

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

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PositionBottomRight();
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        Left = workArea.Right - width - ScreenMargin;
        Top = workArea.Bottom - height - ScreenMargin;
    }

    private void OnSessionStateChanged(ReminderSessionSnapshot snapshot)
    {
        Dispatcher.Invoke(() =>
        {
            _viewModel.Apply(snapshot);
            // Cancel / Due / Idle → leave the toast immediately.
            if (snapshot.Phase is ReminderSessionPhase.Cancelled
                or ReminderSessionPhase.Due
                or ReminderSessionPhase.Idle)
            {
                Hide();
            }
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_session.CancelToday())
        {
            Hide();
            return;
        }

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
        PositionBottomRight();
        if (!IsVisible)
            Show();
        Activate();
        Focus();
    }
}
