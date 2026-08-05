using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShutdownGuard.Models;
using Microsoft.Win32;

namespace ShutdownGuard.Services;

public sealed class ConfigStore
{
    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string ConfigDir = Path.Combine(AppData, "ShutdownGuard");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly string LegacyConfigDir = Path.Combine(AppData, "AutoShutdown");
    private static readonly string LegacyConfigPath = Path.Combine(LegacyConfigDir, "config.json");
    private const string LegacyRegistryValue = "AutoShutdown";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppConfig Load()
    {
        MigrateLegacyDataOnce();

        try
        {
            if (!File.Exists(ConfigPath))
                return new AppConfig();

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }

    /// <summary>
    /// One-shot migration from the previous app name (AutoShutdown):
    ///   - If no IdlePulse config exists but a legacy AutoShutdown config does, copy it over.
    ///   - Always remove any leftover AutoShutdown autostart registry value.
    /// </summary>
    private static void MigrateLegacyDataOnce()
    {
        try
        {
            if (!File.Exists(ConfigPath) && File.Exists(LegacyConfigPath))
            {
                Directory.CreateDirectory(ConfigDir);
                File.Copy(LegacyConfigPath, ConfigPath, overwrite: false);
            }
        }
        catch { }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(LegacyRegistryValue) != null)
            {
                key.DeleteValue(LegacyRegistryValue, throwOnMissingValue: false);
            }
        }
        catch { }
    }
}
