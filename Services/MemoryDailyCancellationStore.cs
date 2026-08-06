namespace ShutdownGuard.Services;

/// <summary>In-memory cancellation store for unit tests.</summary>
public sealed class MemoryDailyCancellationStore : IDailyCancellationStore
{
    public DateOnly? CancelledDate { get; private set; }
    public bool ThrowOnSave { get; set; }

    public DateOnly? LoadCancelledDate() => CancelledDate;

    public void SaveCancelledDate(DateOnly date)
    {
        if (ThrowOnSave)
            throw new InvalidOperationException("Simulated state save failure");

        CancelledDate = date;
    }

    public void Clear() => CancelledDate = null;
}
