using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShutdownGuard.Models;

namespace ShutdownGuard.ViewModels;

/// <summary>
/// Lightweight ViewModel for the Settings window.
/// Edits a working copy — no side effects until Save is called.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private bool _enabled;
    private int _hour;
    private int _minute;
    private bool _dryRun;
    private bool _runAtStartup;
    private string _nextShutdownText = "Disabled";

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    public int Hour
    {
        get => _hour;
        set { _hour = Clamp(value, 0, 23); OnPropertyChanged(); }
    }

    public int Minute
    {
        get => _minute;
        set { _minute = Clamp(value, 0, 59); OnPropertyChanged(); }
    }

    public bool DryRun
    {
        get => _dryRun;
        set { _dryRun = value; OnPropertyChanged(); }
    }

    public bool RunAtStartup
    {
        get => _runAtStartup;
        set { _runAtStartup = value; OnPropertyChanged(); }
    }

    public string NextShutdownText
    {
        get => _nextShutdownText;
        set { _nextShutdownText = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Populates the ViewModel from a loaded config and scheduler state.
    /// Creates a working copy — the original config is not mutated.
    /// </summary>
    public void Load(AppConfig config, DateTimeOffset? nextShutdown)
    {
        _enabled = config.Shutdown.Enabled;
        _hour = config.Shutdown.ShutdownTime.Hour;
        _minute = config.Shutdown.ShutdownTime.Minute;
        _dryRun = config.Shutdown.DryRun;
        _runAtStartup = config.RunAtStartup;
        _nextShutdownText = FormatNextShutdown(nextShutdown, config.Shutdown.Enabled);
    }

    /// <summary>
    /// Creates a new ShutdownPlan snapshot from the current ViewModel state.
    /// Does not mutate any running plan reference.
    /// </summary>
    public ShutdownPlan ToShutdownPlan()
    {
        return new ShutdownPlan
        {
            Enabled = _enabled,
            ShutdownTime = new TimeOnly(_hour, _minute),
            DryRun = _dryRun
        };
    }

    /// <summary>
    /// Creates a new AppConfig snapshot from the current ViewModel state.
    /// </summary>
    public AppConfig ToAppConfig()
    {
        return new AppConfig
        {
            RunAtStartup = _runAtStartup,
            Shutdown = ToShutdownPlan()
        };
    }

    /// <summary>
    /// Formats the NextShutdown value for display. Only does presentation —
    /// does not recalculate scheduling logic (that's SchedulerService's job).
    /// </summary>
    public static string FormatNextShutdown(DateTimeOffset? next, bool enabled)
    {
        if (!enabled || next is null)
            return "Disabled";

        var now = DateTimeOffset.Now;
        var nextVal = next.Value;

        if (nextVal.Date == now.Date)
            return $"Today, {nextVal:HH:mm}";

        if (nextVal.Date == now.Date.AddDays(1))
            return $"Tomorrow, {nextVal:HH:mm}";

        return $"{nextVal:yyyy-MM-dd HH:mm}";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
