namespace ShutdownGuard.Models;

/// <summary>
/// Immutable view of the current reminder session for UI / tray binding.
/// </summary>
public sealed class ReminderSessionSnapshot
{
    public static ReminderSessionSnapshot Idle { get; } = new(
        ReminderSessionPhase.Idle,
        reminderStartedAt: null,
        fixedShutdownAt: null,
        remainingUntilShutdown: null,
        dryRun: true);

    public ReminderSessionPhase Phase { get; }
    public DateTimeOffset? ReminderStartedAt { get; }
    public DateTimeOffset? FixedShutdownAt { get; }
    public TimeSpan? RemainingUntilShutdown { get; }

    /// <summary>Display-only. Does not affect M4 session transitions.</summary>
    public bool DryRun { get; }

    public bool IsCancelled => Phase == ReminderSessionPhase.Cancelled;

    public ReminderSessionSnapshot(
        ReminderSessionPhase phase,
        DateTimeOffset? reminderStartedAt,
        DateTimeOffset? fixedShutdownAt,
        TimeSpan? remainingUntilShutdown,
        bool dryRun)
    {
        Phase = phase;
        ReminderStartedAt = reminderStartedAt;
        FixedShutdownAt = fixedShutdownAt;
        RemainingUntilShutdown = remainingUntilShutdown;
        DryRun = dryRun;
    }
}
