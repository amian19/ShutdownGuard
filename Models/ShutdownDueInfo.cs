namespace ShutdownGuard.Models;

/// <summary>
/// Payload for ReminderSessionController.ShutdownDue — identifies the occurrence.
/// </summary>
public sealed class ShutdownDueInfo
{
    public DateOnly OccurrenceDate { get; }
    public DateTimeOffset ShutdownAt { get; }
    public DateTimeOffset ReminderStartedAt { get; }

    public ShutdownDueInfo(
        DateOnly occurrenceDate,
        DateTimeOffset shutdownAt,
        DateTimeOffset reminderStartedAt)
    {
        OccurrenceDate = occurrenceDate;
        ShutdownAt = shutdownAt;
        ReminderStartedAt = reminderStartedAt;
    }
}
