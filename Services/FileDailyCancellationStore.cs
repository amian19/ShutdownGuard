using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Persists daily cancellation in an independent runtime state file
/// under LocalAppData — never in config.json.
/// </summary>
public sealed class FileDailyCancellationStore : IDailyCancellationStore
{
    private static readonly string DefaultStateDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShutdownGuard");

    private static readonly string DefaultStatePath = Path.Combine(DefaultStateDir, "state.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _stateDir;
    private readonly string _statePath;
    private readonly object _lock = new();

    public FileDailyCancellationStore()
    {
        _stateDir = DefaultStateDir;
        _statePath = DefaultStatePath;
    }

    /// <summary>Test constructor — writes to a custom directory.</summary>
    public FileDailyCancellationStore(string stateDir)
    {
        _stateDir = stateDir;
        _statePath = Path.Combine(stateDir, "state.json");
    }

    internal string StatePath => _statePath;

    public DateOnly? LoadCancelledDate()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_statePath))
                    return null;

                var json = File.ReadAllText(_statePath);
                ReminderRuntimeState? state;
                try
                {
                    state = JsonSerializer.Deserialize<ReminderRuntimeState>(json, JsonOptions);
                }
                catch (JsonException ex)
                {
                    AppLogger.Error($"Runtime state corrupt (treating as not cancelled): {ex.Message}");
                    return null;
                }

                if (state is null)
                    return null;

                // Best-effort: leave stale dates in place; callers ignore non-today.
                return state.CancelledShutdownDate;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to load runtime state: {ex.Message}");
                return null;
            }
        }
    }

    public void SaveCancelledDate(DateOnly date)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(_stateDir);

            var state = new ReminderRuntimeState
            {
                CancelledShutdownDate = date
            };

            var json = JsonSerializer.Serialize(state, JsonOptions);
            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _statePath, overwrite: true);
        }
    }
}
