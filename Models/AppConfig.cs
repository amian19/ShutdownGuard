namespace ShutdownGuard.Models;

public sealed class AppConfig
{
    public bool RunAtStartup { get; set; } = false;
    public bool DryRun { get; set; } = true;
}

