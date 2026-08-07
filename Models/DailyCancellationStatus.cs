namespace ShutdownGuard.Models;

/// <summary>
/// Fact reported by the cancellation store for a given local day.
/// Store reports facts only — Session/Coordinator decide policy.
/// </summary>
public enum DailyCancellationStatus
{
    /// <summary>No persisted cancel for the requested day (including missing state.json).</summary>
    NotCancelled,

    /// <summary>Persisted CancelledShutdownDate equals the requested day.</summary>
    Cancelled,

    /// <summary>State unreadable/corrupt/IO failure — cannot confirm cancellation.</summary>
    Unavailable
}
