using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Thread-safe in-memory provider of the latest applied AppConfig flags.
/// </summary>
public sealed class MutableAppConfigProvider : IAppConfigProvider
{
    private readonly object _lock = new();
    private AppConfigSnapshot _snapshot;
    private bool _unavailable;
    private string? _unavailableError;

    public MutableAppConfigProvider(AppConfig initial)
    {
        _snapshot = new AppConfigSnapshot(initial.Shutdown.Enabled, initial.Shutdown.DryRun);
    }

    public void Update(AppConfig config)
    {
        lock (_lock)
        {
            _unavailable = false;
            _unavailableError = null;
            _snapshot = new AppConfigSnapshot(config.Shutdown.Enabled, config.Shutdown.DryRun);
        }
    }

    /// <summary>Test helper — forces config unavailable.</summary>
    internal void MarkUnavailable(string error)
    {
        lock (_lock)
        {
            _unavailable = true;
            _unavailableError = error;
        }
    }

    public bool TryGetSnapshot(out AppConfigSnapshot snapshot, out string? error)
    {
        lock (_lock)
        {
            if (_unavailable)
            {
                snapshot = new AppConfigSnapshot(enabled: false, dryRun: true);
                error = _unavailableError ?? "Config unavailable";
                return false;
            }

            snapshot = _snapshot;
            error = null;
            return true;
        }
    }
}
