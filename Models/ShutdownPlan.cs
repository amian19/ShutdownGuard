namespace ShutdownGuard.Models;

public sealed class ShutdownPlan
{
    public bool Enabled { get; set; } = true;
    public TimeOnly ReminderStartTime { get; set; } = new(18, 0);
    public bool DryRun { get; set; } = true;

    /// <summary>
    /// DEBUG-only test shutdown clock. When set, runtime uses this instead of 22:00.
    /// Release builds ignore this field.
    /// </summary>
    public TimeOnly? DebugFixedShutdownTime { get; set; }
}
