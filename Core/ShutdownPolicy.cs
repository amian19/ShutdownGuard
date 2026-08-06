using ShutdownGuard.Models;

namespace ShutdownGuard.Core;

/// <summary>
/// Product-level domain rules that are not user-configurable.
/// Fixed shutdown time is 22:00 — reminder window is [ReminderStartTime, 22:00).
/// </summary>
public static class ShutdownPolicy
{
    public static readonly TimeOnly FixedShutdownTime = new(22, 0);
    public static readonly TimeOnly DefaultReminderStartTime = new(18, 0);

    /// <summary>
    /// ReminderStartTime must be strictly before the fixed shutdown time.
    /// Allowed range: 00:00 .. 21:59.
    /// </summary>
    public static bool IsValidReminderStartTime(TimeOnly time)
        => time < FixedShutdownTime;

    /// <summary>
    /// True when the plan is enabled and local time is in [ReminderStartTime, FixedShutdownTime).
    /// </summary>
    public static bool IsWithinReminderWindow(ShutdownPlan plan, DateTimeOffset now)
    {
        if (!plan.Enabled)
            return false;

        var local = new TimeOnly(now.Hour, now.Minute, now.Second, now.Millisecond);
        return local >= plan.ReminderStartTime && local < FixedShutdownTime;
    }
}
