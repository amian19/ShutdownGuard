using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// DEBUG helpers for building a ShutdownDueInfo at the effective fixed shutdown clock.
/// </summary>
public static class DebugShutdownSimulation
{
    public static ShutdownDueInfo CreateTodayFixedDue(
        DateTimeOffset now,
        DateTimeOffset? reminderStartedAt = null)
    {
        var day = DateOnly.FromDateTime(now.DateTime);
        var shut = ShutdownPolicy.EffectiveFixedShutdownTime;
        var shutdownAt = new DateTimeOffset(
            day.Year, day.Month, day.Day,
            shut.Hour, shut.Minute, 0,
            now.Offset);

        var reminderAt = reminderStartedAt
            ?? new DateTimeOffset(
                day.Year, day.Month, day.Day,
                ShutdownPolicy.DefaultReminderStartTime.Hour,
                ShutdownPolicy.DefaultReminderStartTime.Minute,
                0,
                now.Offset);

        return new ShutdownDueInfo(day, shutdownAt, reminderAt);
    }
}
