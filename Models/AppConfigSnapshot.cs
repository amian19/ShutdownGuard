namespace ShutdownGuard.Models;

/// <summary>Latest applied long-lived plan flags used at execution time.</summary>
public sealed class AppConfigSnapshot
{
    public bool Enabled { get; }
    public bool DryRun { get; }

    public AppConfigSnapshot(bool enabled, bool dryRun)
    {
        Enabled = enabled;
        DryRun = dryRun;
    }
}
