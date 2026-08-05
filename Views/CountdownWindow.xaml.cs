using System.Windows;
using System.Windows.Threading;
using ShutdownGuard.Models;
using Wpf.Ui.Controls;

namespace ShutdownGuard.Views;

public partial class CountdownWindow : FluentWindow
{
    private readonly DispatcherTimer _timer;
    private readonly int _total;
    private int _remaining;
    private bool _fired;

    public bool Fired => _fired;

    public CountdownWindow(PowerAction action, int seconds)
    {
        InitializeComponent();
        _total = seconds;
        _remaining = seconds;
        ActionText.Text = action switch
        {
            PowerAction.Shutdown => "System will shut down",
            PowerAction.Sleep => "System will sleep",
            PowerAction.Hibernate => "System will hibernate",
            PowerAction.Lock => "System will lock",
            _ => "Action will run"
        };
        UpdateUi();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        if (_remaining <= 0)
        {
            _timer.Stop();
            _fired = true;
            Close();
            return;
        }
        UpdateUi();
    }

    private void UpdateUi()
    {
        CountdownText.Text = _remaining.ToString();
        ProgressRing.Progress = (double)_remaining / _total * 100.0;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Close();
    }
}
