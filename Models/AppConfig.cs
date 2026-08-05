namespace ShutdownGuard.Models;

public enum PowerAction
{
    Sleep,
    Shutdown,
    Hibernate,
    Lock
}

public sealed class AppConfig
{
    public bool Enabled { get; set; } = true;
    public int IdleSeconds { get; set; } = 1800; // 30 minutes
    public PowerAction Action { get; set; } = PowerAction.Shutdown;
    public int WarningSeconds { get; set; } = 30;
    public bool RunAtStartup { get; set; } = false;
    public bool SkipWhenFullscreen { get; set; } = true;
    public bool SkipWhenPresenting { get; set; } = true;

    // App-level (advanced) settings
    public int ScanIntervalSeconds { get; set; } = 5;

    public AppConfig Clone() => new()
    {
        Enabled = Enabled,
        IdleSeconds = IdleSeconds,
        Action = Action,
        WarningSeconds = WarningSeconds,
        RunAtStartup = RunAtStartup,
        SkipWhenFullscreen = SkipWhenFullscreen,
        SkipWhenPresenting = SkipWhenPresenting,
        ScanIntervalSeconds = ScanIntervalSeconds
    };

    public bool ValueEquals(AppConfig other) =>
        other != null
        && Enabled == other.Enabled
        && IdleSeconds == other.IdleSeconds
        && Action == other.Action
        && WarningSeconds == other.WarningSeconds
        && RunAtStartup == other.RunAtStartup
        && SkipWhenFullscreen == other.SkipWhenFullscreen
        && SkipWhenPresenting == other.SkipWhenPresenting
        && ScanIntervalSeconds == other.ScanIntervalSeconds;
}
