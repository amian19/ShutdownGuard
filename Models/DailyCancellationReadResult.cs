namespace ShutdownGuard.Models;

public sealed class DailyCancellationReadResult
{
    public DailyCancellationStatus Status { get; }
    public string? Error { get; }

    public DailyCancellationReadResult(DailyCancellationStatus status, string? error = null)
    {
        Status = status;
        Error = error;
    }

    public static DailyCancellationReadResult NotCancelled { get; } =
        new(DailyCancellationStatus.NotCancelled);

    public static DailyCancellationReadResult Cancelled { get; } =
        new(DailyCancellationStatus.Cancelled);

    public static DailyCancellationReadResult Unavailable(string error) =>
        new(DailyCancellationStatus.Unavailable, error);
}
