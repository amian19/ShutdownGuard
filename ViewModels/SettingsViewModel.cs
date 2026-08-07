using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.ViewModels;

/// <summary>
/// Lightweight ViewModel for the Settings window.
/// Edits a working copy — no side effects until Save is called.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private int _reminderHour;
    private int _reminderMinute;
    private bool _useTestShutdownTime;
    private int _testShutdownHour = 22;
    private int _testShutdownMinute;
    private string _nextReminderText = "无";
    private string _scheduledShutdownText = "无";
    private bool _todayWillShutdown = true;
    private string? _validationError;

    /// <summary>Always show test-shutdown controls in settings (for trial builds).</summary>
    public bool ShowTestShutdownSettings => true;

    /// <summary>Product rule: auto-shutdown is always on.</summary>
    public bool Enabled
    {
        get => true;
        set { /* Removed: use TodayWillShutdown instead. */ }
    }

    /// <summary>ON = shut down today; OFF = skip today (restores tomorrow).</summary>
    public bool TodayWillShutdown
    {
        get => _todayWillShutdown;
        set { _todayWillShutdown = value; OnPropertyChanged(); }
    }

    public int ReminderHour
    {
        get => _reminderHour;
        set { _reminderHour = Clamp(value, 0, 23); OnPropertyChanged(); }
    }

    public int ReminderMinute
    {
        get => _reminderMinute;
        set { _reminderMinute = Clamp(value, 0, 59); OnPropertyChanged(); }
    }

    public bool DryRun
    {
        get => false;
        set { /* Removed: always real shutdown. */ }
    }

    public bool RunAtStartup
    {
        get => true;
        set { /* Product rule: always on. */ }
    }

    /// <summary>Use custom test shutdown clock instead of 22:00.</summary>
    public bool UseTestShutdownTime
    {
        get => _useTestShutdownTime;
        set { _useTestShutdownTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(FixedShutdownDisplay)); }
    }

    public int TestShutdownHour
    {
        get => _testShutdownHour;
        set { _testShutdownHour = Clamp(value, 0, 23); OnPropertyChanged(); OnPropertyChanged(nameof(FixedShutdownDisplay)); }
    }

    public int TestShutdownMinute
    {
        get => _testShutdownMinute;
        set { _testShutdownMinute = Clamp(value, 0, 59); OnPropertyChanged(); OnPropertyChanged(nameof(FixedShutdownDisplay)); }
    }

    public string NextReminderText
    {
        get => _nextReminderText;
        set { _nextReminderText = value; OnPropertyChanged(); }
    }

    public string ScheduledShutdownText
    {
        get => _scheduledShutdownText;
        set { _scheduledShutdownText = value; OnPropertyChanged(); }
    }

    public string FixedShutdownDisplay
    {
        get
        {
            var t = ResolveShutdownTimeForEdit();
            return _useTestShutdownTime
                ? $"{t:HH:mm}（测试）"
                : $"{ShutdownPolicy.FixedShutdownTime:HH:mm}";
        }
    }

    public string? ValidationError
    {
        get => _validationError;
        private set { _validationError = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Load(AppConfig config, DateTimeOffset? nextReminder, bool todayCancelled = false)
    {
        _reminderHour = config.Shutdown.ReminderStartTime.Hour;
        _reminderMinute = config.Shutdown.ReminderStartTime.Minute;

        if (config.Shutdown.DebugFixedShutdownTime is { } debug)
        {
            _useTestShutdownTime = true;
            _testShutdownHour = debug.Hour;
            _testShutdownMinute = debug.Minute;
        }
        else
        {
            _useTestShutdownTime = false;
            _testShutdownHour = ShutdownPolicy.FixedShutdownTime.Hour;
            _testShutdownMinute = ShutdownPolicy.FixedShutdownTime.Minute;
        }

        _validationError = null;
        _todayWillShutdown = !todayCancelled;
        _nextReminderText = FormatNextReminder(nextReminder, enabled: true, todayCancelled);
        _scheduledShutdownText = FormatScheduledShutdown(
            nextReminder, enabled: true, todayCancelled, ResolveShutdownTimeForEdit());

        OnPropertyChanged(nameof(Enabled));
        OnPropertyChanged(nameof(TodayWillShutdown));
        OnPropertyChanged(nameof(ReminderHour));
        OnPropertyChanged(nameof(ReminderMinute));
        OnPropertyChanged(nameof(UseTestShutdownTime));
        OnPropertyChanged(nameof(TestShutdownHour));
        OnPropertyChanged(nameof(TestShutdownMinute));
        OnPropertyChanged(nameof(FixedShutdownDisplay));
        OnPropertyChanged(nameof(NextReminderText));
        OnPropertyChanged(nameof(ScheduledShutdownText));
    }

    public ShutdownPlan ToShutdownPlan()
    {
        return new ShutdownPlan
        {
            Enabled = true,
            ReminderStartTime = new TimeOnly(_reminderHour, _reminderMinute),
            DryRun = false,
            DebugFixedShutdownTime = _useTestShutdownTime
                ? new TimeOnly(_testShutdownHour, _testShutdownMinute)
                : null
        };
    }

    public AppConfig ToAppConfig()
    {
        return new AppConfig
        {
            RunAtStartup = true,
            Shutdown = ToShutdownPlan()
        };
    }

    public bool TryValidate(out string? error)
    {
        var reminder = new TimeOnly(_reminderHour, _reminderMinute);
        var shutdown = ResolveShutdownTimeForEdit();

        if (!ShutdownPolicy.IsValidReminderStartTime(reminder, shutdown))
        {
            error = $"提醒开始时间必须早于关机时间 {shutdown:HH:mm}。";
            ValidationError = error;
            return false;
        }

        error = null;
        ValidationError = null;
        return true;
    }

    private TimeOnly ResolveShutdownTimeForEdit()
    {
        if (_useTestShutdownTime)
            return new TimeOnly(_testShutdownHour, _testShutdownMinute);
        return ShutdownPolicy.FixedShutdownTime;
    }

    public static string FormatScheduledShutdown(
        DateTimeOffset? nextReminder,
        bool enabled,
        bool todayCancelled = false,
        TimeOnly? shutdownClock = null)
    {
        if (!enabled)
            return "无";

        var clock = shutdownClock ?? ShutdownPolicy.EffectiveFixedShutdownTime;
        var now = DateTimeOffset.Now;
        var todayShutdown = new DateTimeOffset(
            now.Year, now.Month, now.Day,
            clock.Hour, clock.Minute, 0, now.Offset);

        if (todayCancelled)
        {
            if (nextReminder is { } n && n.Date > now.Date)
                return $"明天 {clock:HH:mm}（今天已取消）";

            return "今天已取消";
        }

        // Still before today's shutdown → this session is today (even if NextReminder already advanced).
        if (now < todayShutdown)
            return FormatRelativeDateTime(todayShutdown);

        if (nextReminder is null)
            return "无";

        var shutdown = new DateTimeOffset(
            nextReminder.Value.Year,
            nextReminder.Value.Month,
            nextReminder.Value.Day,
            clock.Hour,
            clock.Minute,
            0,
            nextReminder.Value.Offset);

        return FormatRelativeDateTime(shutdown);
    }

    public static string FormatNextReminder(
        DateTimeOffset? next,
        bool enabled,
        bool todayCancelled = false)
    {
        if (!enabled || next is null)
            return "无";

        var text = FormatRelativeDateTime(next.Value);
        if (todayCancelled && next.Value.Date == DateTimeOffset.Now.Date)
            return "今天已取消（不应再提醒）";

        return text;
    }

    public static string FormatRelativeDateTime(DateTimeOffset value)
    {
        var now = DateTimeOffset.Now;

        if (value.Date == now.Date)
            return $"今天 {value:HH:mm}";

        if (value.Date == now.Date.AddDays(1))
            return $"明天 {value:HH:mm}";

        return $"{value:yyyy年M月d日 HH:mm}";
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
