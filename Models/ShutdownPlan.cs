namespace ShutdownGuard.Models;

public sealed class ShutdownPlan
{
    public bool Enabled { get; set; } = true;
    public TimeOnly ReminderStartTime { get; set; } = new(18, 0);
    public bool DryRun { get; set; } = true;
}
