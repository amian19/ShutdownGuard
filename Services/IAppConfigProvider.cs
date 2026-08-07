using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Provides the latest successfully applied config for execution decisions.
/// </summary>
public interface IAppConfigProvider
{
    /// <summary>
    /// Returns false when the current config cannot be read reliably.
    /// </summary>
    bool TryGetSnapshot(out AppConfigSnapshot snapshot, out string? error);
}
