using System.Windows.Threading;
using ShutdownGuard.Models;
using ShutdownGuard.Native;
using Microsoft.Win32;

namespace ShutdownGuard.Services;

public sealed class IdleMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private AppConfig _config;
    private bool _paused;
    private DateTime _pausedUntil = DateTime.MinValue;

    public event EventHandler? IdleThresholdReached;

    public IdleMonitor(AppConfig config)
    {
        _config = config;
        _timer = new DispatcherTimer { Interval = ClampInterval(config.ScanIntervalSeconds) };
        _timer.Tick += OnTick;

        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        _timer.Interval = ClampInterval(config.ScanIntervalSeconds);
    }

    private static TimeSpan ClampInterval(int seconds)
        => TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 3600));

    public void Start()
    {
        if (_config.Enabled) _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void SuspendFor(TimeSpan duration)
    {
        _pausedUntil = DateTime.UtcNow + duration;
    }

    public void Reset()
    {
        _paused = false;
        _pausedUntil = DateTime.MinValue;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_config.Enabled || _paused || DateTime.UtcNow < _pausedUntil)
            return;

        var idle = NativeMethods.GetIdleTime();
        var threshold = TimeSpan.FromSeconds(_config.IdleSeconds);

        if (idle < threshold) return;

        if (ShouldSkip()) return;

        IdleThresholdReached?.Invoke(this, EventArgs.Empty);
    }

    private bool ShouldSkip()
    {
        if (NativeMethods.SHQueryUserNotificationState(out var state) != 0)
            return false;

        if (_config.SkipWhenPresenting && state == NativeMethods.QueryUserNotificationState.PresentationMode)
            return true;

        if (_config.SkipWhenFullscreen && state == NativeMethods.QueryUserNotificationState.RunningDirect3DFullScreen)
            return true;

        if (state == NativeMethods.QueryUserNotificationState.Busy)
            return true;

        return false;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _pausedUntil = DateTime.UtcNow.AddMinutes(1);
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionLock:
            case SessionSwitchReason.SessionLogoff:
            case SessionSwitchReason.RemoteDisconnect:
            case SessionSwitchReason.ConsoleDisconnect:
                _paused = true;
                break;
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.SessionLogon:
            case SessionSwitchReason.RemoteConnect:
            case SessionSwitchReason.ConsoleConnect:
                _paused = false;
                _pausedUntil = DateTime.UtcNow.AddSeconds(10);
                break;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
    }
}
