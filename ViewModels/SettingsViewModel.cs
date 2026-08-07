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
    private bool _enabled;
    private int _reminderHour;
    private int _reminderMinute;
    private bool _dryRun;
    private bool _runAtStartup;
    private string _nextReminderText = "无";
    private string _scheduledShutdownText = "无";
    private string? _validationError;

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    public int ReminderHour
    {
        get => _reminderHour;
        set { _reminderHour = Clamp(value, 0, 21); OnPropertyChanged(); }
    }

    public int ReminderMinute
    {
        get => _reminderMinute;
        set { _reminderMinute = Clamp(value, 0, 59); OnPropertyChanged(); }
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

    public string FixedShutdownDisplay =>
        $"{ShutdownPolicy.FixedShutdownTime.Hour:D2} : {ShutdownPolicy.FixedShutdownTime.Minute:D2}";

    public string? ValidationError
    {
        get => _validationError;
        private set { _validationError = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Populates the ViewModel from a loaded config and scheduler state.
    /// Creates a working copy — the original config is not mutated.
    /// </summary>
    public void Load(AppConfig config, DateTimeOffset? nextReminder, bool todayCancelled = false)
    {
        _enabled = config.Shutdown.Enabled;
        _reminderHour = config.Shutdown.ReminderStartTime.Hour;
        _reminderMinute = config.Shutdown.ReminderStartTime.Minute;
        _dryRun = config.Shutdown.DryRun;
        _runAtStartup = config.RunAtStartup;
        _validationError = null;
        _nextReminderText = FormatNextReminder(nextReminder, config.Shutdown.Enabled, todayCancelled);
        _scheduledShutdownText = FormatScheduledShutdown(
            nextReminder, config.Shutdown.Enabled, todayCancelled);
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
            ReminderStartTime = new TimeOnly(_reminderHour, _reminderMinute),
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
    /// Validates ReminderStartTime &lt; fixed shutdown (22:00). Does not silent-correct.
    /// </summary>
    public bool TryValidate(out string? error)
    {
        var reminder = new TimeOnly(_reminderHour, _reminderMinute);
        if (!ShutdownPolicy.IsValidReminderStartTime(reminder))
        {
            error = $"提醒开始时间必须早于固定关机时间 {ShutdownPolicy.FixedShutdownTime:HH:mm}。";
            ValidationError = error;
            return false;
        }

        error = null;
        ValidationError = null;
        return true;
    }

    /// <summary>
    /// Formats the planned fixed shutdown (22:00 on the same day as NextReminder).
    /// When today is already cancelled, shows that explicitly instead of "今天 22:00".
    /// Presentation only — does not participate in shutdown scheduling.
    /// </summary>
    public static string FormatScheduledShutdown(
        DateTimeOffset? nextReminder,
        bool enabled,
        bool todayCancelled = false)
    {
        if (!enabled || nextReminder is null)
            return "无";

        var now = DateTimeOffset.Now;
        if (todayCancelled)
        {
            // Next reminder should already be tomorrow after MarkTodayReminderHandled;
            // still label clearly when the next shutdown day is tomorrow.
            if (nextReminder.Value.Date > now.Date)
                return $"明天 {ShutdownPolicy.FixedShutdownTime:HH:mm}（今天已取消）";

            return "今天已取消";
        }

        var shutdown = new DateTimeOffset(
            nextReminder.Value.Year,
            nextReminder.Value.Month,
            nextReminder.Value.Day,
            ShutdownPolicy.FixedShutdownTime.Hour,
            ShutdownPolicy.FixedShutdownTime.Minute,
            0,
            nextReminder.Value.Offset);

        return FormatRelativeDateTime(shutdown);
    }

    /// <summary>
    /// Formats NextReminder for display. When today is cancelled, still shows the next
    /// scheduled reminder (typically tomorrow) — cancellation is shown on ScheduledShutdown.
    /// </summary>
    public static string FormatNextReminder(
        DateTimeOffset? next,
        bool enabled,
        bool todayCancelled = false)
    {
        if (!enabled || next is null)
            return "无";

        var text = FormatRelativeDateTime(next.Value);
        if (todayCancelled && next.Value.Date == DateTimeOffset.Now.Date)
            return $"今天已取消（不应再提醒）";

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
