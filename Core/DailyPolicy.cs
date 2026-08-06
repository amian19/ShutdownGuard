using ShutdownGuard.Models;

namespace ShutdownGuard.Core;

/// <summary>
/// Computes the target occurrence for a given day based on the plan.
/// Always returns today's target (in local time), even if it has already passed.
/// The caller (SchedulerService) decides whether to execute a missed occurrence
/// or skip to tomorrow.
/// </summary>
public sealed class DailyPolicy
{
    /// <summary>
    /// Returns today's shutdown target occurrence, or null if the plan is disabled.
    /// The returned value is always today's date + plan.ShutdownTime, regardless of
    /// whether that time has already passed.
    /// </summary>
    public DateTimeOffset? GetTodayTarget(ShutdownPlan plan, DateTimeOffset now)
    {
        if (!plan.Enabled)
            return null;

        // Explicitly construct midnight with the same offset to avoid
        // any ambiguity with DateTimeOffset.Date across .NET versions.
        var midnight = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
        return midnight + plan.ShutdownTime.ToTimeSpan();
    }

    /// <summary>
    /// Returns the next upcoming occurrence (today if still in the future, otherwise tomorrow).
    /// Used for display purposes (UI tooltip).
    /// </summary>
    public DateTimeOffset? GetNextOccurrence(ShutdownPlan plan, DateTimeOffset now)
    {
        if (!plan.Enabled)
            return null;

        var today = now.Date;
        var targetToday = today + plan.ShutdownTime.ToTimeSpan();

        if (targetToday >= now)
            return targetToday;

        // Already passed today — schedule for tomorrow
        return today.AddDays(1) + plan.ShutdownTime.ToTimeSpan();
    }
}
