namespace ShutdownGuard.Models;

/// <summary>
/// Read-only execution status for Tray/UI. Not derived from log text.
/// </summary>
public sealed class ShutdownExecutionSnapshot
{
    public static ShutdownExecutionSnapshot Idle { get; } = new(
        ShutdownExecutionPhase.Idle,
        occurrenceDate: null,
        message: "",
        error: null,
        realShutdownWouldBeBlocked: false);

    public ShutdownExecutionPhase Phase { get; }
    public DateOnly? OccurrenceDate { get; }
    public string Message { get; }
    public string? Error { get; }

    /// <summary>
    /// True when DryRun executed but a real shutdown would have been blocked
    /// (e.g. cancellation state unavailable).
    /// </summary>
    public bool RealShutdownWouldBeBlocked { get; }

    public ShutdownExecutionSnapshot(
        ShutdownExecutionPhase phase,
        DateOnly? occurrenceDate,
        string message,
        string? error,
        bool realShutdownWouldBeBlocked)
    {
        Phase = phase;
        OccurrenceDate = occurrenceDate;
        Message = message;
        Error = error;
        RealShutdownWouldBeBlocked = realShutdownWouldBeBlocked;
    }
}
