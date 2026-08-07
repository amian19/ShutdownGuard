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

    public FileDailyCancellationStore(string stateDir)
    {
        _stateDir = stateDir;
        _statePath = Path.Combine(stateDir, "state.json");
    }

    internal string StatePath => _statePath;

    public DailyCancellationReadResult ReadCancellationState(DateOnly day)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_statePath))
                    return DailyCancellationReadResult.NotCancelled;

                string json;
                try
                {
                    json = File.ReadAllText(_statePath);
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Runtime state unreadable: {ex.Message}");
                    return DailyCancellationReadResult.Unavailable(ex.Message);
                }

                ReminderRuntimeState? state;
                try
                {
                    state = JsonSerializer.Deserialize<ReminderRuntimeState>(json, JsonOptions);
                }
                catch (JsonException ex)
                {
                    AppLogger.Error($"Runtime state corrupt: {ex.Message}");
                    return DailyCancellationReadResult.Unavailable($"Corrupt state.json: {ex.Message}");
                }

                if (state is null)
                    return DailyCancellationReadResult.NotCancelled;

                if (state.CancelledShutdownDate == day)
                    return DailyCancellationReadResult.Cancelled;

                return DailyCancellationReadResult.NotCancelled;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to read runtime state: {ex.Message}");
                return DailyCancellationReadResult.Unavailable(ex.Message);
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

    public void ClearCancelledDate()
    {
        lock (_lock)
        {
            if (!File.Exists(_statePath))
                return;

            try
            {
                File.Delete(_statePath);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to clear runtime cancel state: {ex.Message}");
                throw;
            }
        }
    }
}
