using ShutdownGuard.Models;

namespace ShutdownGuard.Core;

/// <summary>
/// Product-level domain rules.
/// Default fixed shutdown is 22:00; optional test override via
/// <see cref="ApplyDebugFixedShutdownOverride"/>.
/// </summary>
public static class ShutdownPolicy
{
    public static readonly TimeOnly FixedShutdownTime = new(22, 0);
    public static readonly TimeOnly DefaultReminderStartTime = new(18, 0);

    private static TimeOnly? _debugFixedShutdownOverride;

    /// <summary>
    /// Runtime shutdown clock used by Scheduler / Session / Coordinator.
    /// </summary>
    public static TimeOnly EffectiveFixedShutdownTime
        => _debugFixedShutdownOverride ?? FixedShutdownTime;

    /// <summary>
    /// Optional test shutdown clock (used by the "测试关机时间" setting).
    /// Pass null to restore product 22:00.
    /// </summary>
    public static void ApplyDebugFixedShutdownOverride(TimeOnly? overrideTime)
        => _debugFixedShutdownOverride = overrideTime;

    /// <summary>Test helper — clears any override.</summary>
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
