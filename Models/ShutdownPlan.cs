namespace ShutdownGuard.Models;

public sealed class ShutdownPlan
{
    public bool Enabled { get; set; } = true;
    public TimeOnly ReminderStartTime { get; set; } = new(18, 0);
    public bool DryRun { get; set; } = false;

    /// <summary>
    /// Optional test shutdown clock. When set, runtime uses this instead of 22:00.
    /// </summary>
    public TimeOnly? DebugFixedShutdownTime { get; set; }
}
