using ShutdownGuard.Models;

namespace ShutdownGuard.Core;

/// <summary>
/// Product-level domain rules.
/// Production fixed shutdown is always 22:00.
/// DEBUG builds may temporarily override via <see cref="ApplyDebugFixedShutdownOverride"/>.
/// </summary>
public static class ShutdownPolicy
{
    public static readonly TimeOnly FixedShutdownTime = new(22, 0);
    public static readonly TimeOnly DefaultReminderStartTime = new(18, 0);

    private static TimeOnly? _debugFixedShutdownOverride;

    /// <summary>
    /// Runtime shutdown clock used by Scheduler / Session / Coordinator.
    /// Release builds always return <see cref="FixedShutdownTime"/> (22:00).
    /// </summary>
    public static TimeOnly EffectiveFixedShutdownTime
    {
        get
        {
#if DEBUG
            return _debugFixedShutdownOverride ?? FixedShutdownTime;
#else
            return FixedShutdownTime;
#endif
        }
    }

    /// <summary>
    /// DEBUG-only: set or clear the test shutdown time override.
    /// Pass null to restore product 22:00.
    /// </summary>
    public static void ApplyDebugFixedShutdownOverride(TimeOnly? overrideTime)
    {
#if DEBUG
        _debugFixedShutdownOverride = overrideTime;
#else
        _ = overrideTime;
        _debugFixedShutdownOverride = null;
#endif
    }

    /// <summary>Test helper — clears any DEBUG override.</summary>
    internal static void ResetDebugOverrideForTests()
        => _debugFixedShutdownOverride = null;

    /// <summary>
    /// Reminder must be strictly before the effective fixed shutdown time.
    /// </summary>
    public static bool IsValidReminderStartTime(TimeOnly reminder, TimeOnly? shutdownAt = null)
    {
        var limit = shutdownAt ?? EffectiveFixedShutdownTime;
        return reminder < limit;
    }

    /// <summary>
    /// True when the plan is enabled and local time is in [ReminderStartTime, EffectiveFixedShutdownTime).
    /// </summary>
    public static bool IsWithinReminderWindow(ShutdownPlan plan, DateTimeOffset now)
    {
        if (!plan.Enabled)
            return false;

        var local = new TimeOnly(now.Hour, now.Minute, now.Second, now.Millisecond);
        return local >= plan.ReminderStartTime && local < EffectiveFixedShutdownTime;
    }
}
