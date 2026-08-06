using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

public sealed class ConfigStore
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string DefaultConfigDir = Path.Combine(AppData, "ShutdownGuard");
    private static readonly string DefaultConfigPath = Path.Combine(DefaultConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _configDir;
    private readonly string _configPath;

    public ConfigStore()
    {
        _configDir = DefaultConfigDir;
        _configPath = DefaultConfigPath;
    }

    /// <summary>
    /// Creates a ConfigStore that reads/writes to a custom directory.
    /// Intended for testing.
    /// </summary>
    public ConfigStore(string configDir)
    {
        _configDir = configDir;
        _configPath = Path.Combine(configDir, "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_configPath))
                return CreateFreshDefault();

            var json = File.ReadAllText(_configPath);
            return ParseAndMigrate(json);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Config load failed (using safe disabled fallback): {ex.Message}");
            return CreateCorruptFallback();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(_configDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        var tmp = _configPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _configPath, overwrite: true);
    }

    /// <summary>
    /// Fresh install / missing config — auto-shutdown enabled with product defaults.
    /// </summary>
    internal static AppConfig CreateFreshDefault()
    {
        return new AppConfig
        {
            RunAtStartup = false,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ReminderStartTime = ShutdownPolicy.DefaultReminderStartTime,
                DryRun = true
            }
        };
    }

    /// <summary>
    /// Corrupt config — safety first: do not suddenly enable auto-shutdown.
    /// </summary>
    internal static AppConfig CreateCorruptFallback()
    {
        return new AppConfig
        {
            RunAtStartup = false,
            Shutdown = new ShutdownPlan
            {
                Enabled = false,
                ReminderStartTime = ShutdownPolicy.DefaultReminderStartTime,
                DryRun = true
            }
        };
    }

    private static AppConfig ParseAndMigrate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var config = new AppConfig
        {
            RunAtStartup = root.TryGetProperty("runAtStartup", out var ras) && ras.ValueKind == JsonValueKind.True,
            Shutdown = new ShutdownPlan
            {
                Enabled = true,
                ReminderStartTime = ShutdownPolicy.DefaultReminderStartTime,
                DryRun = true
            }
        };

        if (!root.TryGetProperty("shutdown", out var shutdownEl) || shutdownEl.ValueKind != JsonValueKind.Object)
            return config;

        if (shutdownEl.TryGetProperty("enabled", out var enabledEl))
        {
            if (enabledEl.ValueKind == JsonValueKind.True) config.Shutdown.Enabled = true;
            else if (enabledEl.ValueKind == JsonValueKind.False) config.Shutdown.Enabled = false;
        }

        if (shutdownEl.TryGetProperty("dryRun", out var dryRunEl))
        {
            if (dryRunEl.ValueKind == JsonValueKind.True) config.Shutdown.DryRun = true;
            else if (dryRunEl.ValueKind == JsonValueKind.False) config.Shutdown.DryRun = false;
        }

        // Prefer new field; fall back to legacy shutdownTime → ReminderStartTime migration.
        if (TryReadTimeOnly(shutdownEl, "reminderStartTime", out var reminder))
        {
            if (ShutdownPolicy.IsValidReminderStartTime(reminder))
            {
                config.Shutdown.ReminderStartTime = reminder;
            }
            else
            {
                AppLogger.Info(
                    $"Invalid reminderStartTime {reminder:HH:mm:ss} (>= {ShutdownPolicy.FixedShutdownTime:HH:mm}); " +
                    $"falling back to {ShutdownPolicy.DefaultReminderStartTime:HH:mm}");
                config.Shutdown.ReminderStartTime = ShutdownPolicy.DefaultReminderStartTime;
            }
        }
        else if (TryReadTimeOnly(shutdownEl, "shutdownTime", out var legacy))
        {
            if (ShutdownPolicy.IsValidReminderStartTime(legacy))
            {
                config.Shutdown.ReminderStartTime = legacy;
                AppLogger.Info(
                    $"Migrated legacy shutdownTime {legacy:HH:mm:ss} → ReminderStartTime");
            }
            else
            {
                AppLogger.Info(
                    $"Legacy shutdownTime {legacy:HH:mm:ss} >= {ShutdownPolicy.FixedShutdownTime:HH:mm}; " +
                    $"migrated ReminderStartTime to default {ShutdownPolicy.DefaultReminderStartTime:HH:mm}");
                config.Shutdown.ReminderStartTime = ShutdownPolicy.DefaultReminderStartTime;
            }
        }

        return config;
    }

    private static bool TryReadTimeOnly(JsonElement parent, string propertyName, out TimeOnly time)
    {
        time = default;
        if (!parent.TryGetProperty(propertyName, out var el))
            return false;

        if (el.ValueKind != JsonValueKind.String)
            return false;

        var text = el.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return TimeOnly.TryParse(text, out time);
    }
}
