namespace ShutdownGuard.Models;

public sealed class ShutdownPlan
{
    public bool Enabled { get; set; } = false;
    public TimeOnly ShutdownTime { get; set; } = new(23, 0);
    public bool DryRun { get; set; } = true;
}
