namespace ShutdownGuard.Models;

/// <summary>
/// M4 reminder-round lifecycle. Shutdown execution (M4.5) is out of scope.
/// </summary>
public enum ReminderSessionPhase
{
    /// <summary>No reminder round in memory.</summary>
    Idle,

    /// <summary>Inside today's window; shutdown still planned for 22:00.</summary>
    Active,

    /// <summary>User cancelled today's shutdown; no ShutdownDue today.</summary>
    Cancelled,

    /// <summary>22:00 reached while Active; ShutdownDue raised (execution is M4.5).</summary>
    Due
}
