using ShutdownGuard.Models;

namespace ShutdownGuard.Core;

/// <summary>
/// Computes daily reminder-start occurrences from the plan.
/// Always returns today's reminder target (in local time), even if it has already passed.
/// The caller (SchedulerService) decides whether to enter the reminder window or skip to tomorrow.
/// </summary>
public sealed class DailyPolicy
{
    /// <summary>
    /// Returns today's reminder-start occurrence, or null if the plan is disabled.
    /// </summary>
    public DateTimeOffset? GetTodayReminder(ShutdownPlan plan, DateTimeOffset now)
    {
        if (!plan.Enabled)
            return null;

        var midnight = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return midnight + plan.ReminderStartTime.ToTimeSpan();
    }

    /// <summary>
    /// Returns today's fixed shutdown instant (22:00), or null if disabled.
    /// Presentation / M4 use only — SchedulerService does not execute shutdown in M3.5.
    /// </summary>
    public DateTimeOffset? GetTodayFixedShutdown(ShutdownPlan plan, DateTimeOffset now)
    {
        if (!plan.Enabled)
            return null;

        var midnight = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return midnight + ShutdownPolicy.EffectiveFixedShutdownTime.ToTimeSpan();
    }

    /// <summary>
    /// Returns the next upcoming reminder start (today if still in the future, otherwise tomorrow).
    /// Used for display when not currently inside an active reminder window.
    /// </summary>
    public DateTimeOffset? GetNextReminder(ShutdownPlan plan, DateTimeOffset now)
    {
        if (!plan.Enabled)
            return null;

        var today = GetTodayReminder(plan, now);
        if (today is null)
            return null;

        if (today.Value >= now)
            return today;

        return today.Value.AddDays(1);
    }
}
