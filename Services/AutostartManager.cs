using System.IO;
using Microsoft.Win32;

namespace ShutdownGuard.Services;

public sealed class StartupClearReport
{
    public bool RegistryRemoved { get; set; }
    public List<string> StartupShortcutsRemoved { get; } = new();
    public List<string> Errors { get; } = new();

    public bool FoundAnything => RegistryRemoved || StartupShortcutsRemoved.Count > 0;
}

public static class AutostartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ShutdownGuard";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) != null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath ?? AppContext.BaseDirectory;
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Scans known autostart locations and removes any IdlePulse entries.
    /// Only touches per-user locations: HKCU\...\Run and the user's Startup folder.
    /// Never touches HKLM, scheduled tasks, or anything we didn't create.
    /// </summary>
    public static StartupClearReport ClearAll()
    {
        var report = new StartupClearReport();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                report.RegistryRemoved = true;
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add($"Registry: {ex.Message}");
        }

        try
        {
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var lnk in Directory.GetFiles(startupFolder, "*ShutdownGuard*.lnk"))
                {
                    try
                    {
                        File.Delete(lnk);
                        report.StartupShortcutsRemoved.Add(Path.GetFileName(lnk));
                    }
                    catch (Exception ex)
                    {
                        report.Errors.Add($"{Path.GetFileName(lnk)}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            report.Errors.Add($"Startup folder: {ex.Message}");
        }

        return report;
    }
}
