using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
                return CreateDefault();

            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);

            if (config is null)
                return CreateDefault();

            // Ensure Shutdown is never null for backward compatibility
            config.Shutdown ??= new ShutdownPlan();

            return config;
        }
        catch
        {
            return CreateDefault();
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

    private static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            RunAtStartup = false,
            Shutdown = new ShutdownPlan
            {
                Enabled = false,
                ShutdownTime = new TimeOnly(23, 0),
                DryRun = true
            }
        };
    }
}
