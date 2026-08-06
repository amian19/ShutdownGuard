namespace ShutdownGuard.Services;

/// <summary>
/// Persists "cancel today's shutdown" across process restarts.
/// Independent from Enabled — Disable must not write cancellation.
/// </summary>
public interface IDailyCancellationStore
{
    DateOnly? LoadCancelledDate();
    void SaveCancelledDate(DateOnly date);
}
