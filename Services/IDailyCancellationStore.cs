using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Persists "cancel today's shutdown" across process restarts.
/// Reports facts only — does not decide whether shutdown may proceed.
/// </summary>
public interface IDailyCancellationStore
{
    DailyCancellationReadResult ReadCancellationState(DateOnly day);
    void SaveCancelledDate(DateOnly date);
}
