namespace ShutdownGuard.Models;

/// <summary>
/// Daily runtime state that must survive process restarts.
/// Separate from long-lived <see cref="AppConfig"/> (config.json).
/// </summary>
public sealed class ReminderRuntimeState
{
    /// <summary>
    /// Local calendar day whose fixed shutdown was cancelled via CancelToday.
    /// Stale dates (not today) are ignored by session logic.
    /// </summary>
    public DateOnly? CancelledShutdownDate { get; set; }
}
