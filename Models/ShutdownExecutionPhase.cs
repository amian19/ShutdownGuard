namespace ShutdownGuard.Models;

public enum ShutdownExecutionPhase
{
    Idle,
    Executing,
    DryRunCompleted,
    RealShutdownRequested,
    Blocked,
    Failed
}
