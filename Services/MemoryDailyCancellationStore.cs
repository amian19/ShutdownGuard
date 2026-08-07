using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>In-memory cancellation store for unit tests.</summary>
public sealed class MemoryDailyCancellationStore : IDailyCancellationStore
{
    public DateOnly? CancelledDate { get; private set; }
    public bool ThrowOnSave { get; set; }
    public bool ThrowOnRead { get; set; }
    public bool CorruptOnRead { get; set; }

    public DailyCancellationReadResult ReadCancellationState(DateOnly day)
    {
        if (ThrowOnRead)
            return DailyCancellationReadResult.Unavailable("Simulated IO failure");

        if (CorruptOnRead)
            return DailyCancellationReadResult.Unavailable("Simulated corrupt state");

        if (CancelledDate == day)
            return DailyCancellationReadResult.Cancelled;

        return DailyCancellationReadResult.NotCancelled;
    }

    public void SaveCancelledDate(DateOnly date)
    {
        if (ThrowOnSave)
            throw new InvalidOperationException("Simulated state save failure");

        CancelledDate = date;
    }

    public void ClearCancelledDate() => CancelledDate = null;

    public void Clear() => CancelledDate = null;
}
