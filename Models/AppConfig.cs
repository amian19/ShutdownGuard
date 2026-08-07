namespace ShutdownGuard.Models;

public sealed class AppConfig
{
    /// <summary>Product rule: always true — autostart is mandatory.</summary>
    public bool RunAtStartup { get; set; } = true;
    public ShutdownPlan Shutdown { get; set; } = new();
}
