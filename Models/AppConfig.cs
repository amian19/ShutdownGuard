namespace ShutdownGuard.Models;

public sealed class AppConfig
{
    public bool RunAtStartup { get; set; } = false;
    public ShutdownPlan Shutdown { get; set; } = new();
}
