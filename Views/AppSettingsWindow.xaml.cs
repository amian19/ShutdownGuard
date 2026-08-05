using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Wpf.Ui.Controls;

namespace ShutdownGuard.Views;

public partial class AppSettingsWindow : FluentWindow
{
    private readonly ConfigStore _store;
    private AppConfig _config;

    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ShutdownGuard");
    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
    private static readonly string CrashLogPath = Path.Combine(ConfigDir, "crash.log");

    public event EventHandler<AppConfig>? ConfigSaved;

    public AppSettingsWindow(ConfigStore store, AppConfig config)
    {
        InitializeComponent();
        _store = store;
        _config = config;
        LoadIntoUi();
    }

    private void LoadIntoUi()
    {
        ScanIntervalBox.Value = _config.ScanIntervalSeconds;

        ConfigPathBox.Text = ConfigPath;
        CrashLogPathBox.Text = CrashLogPath;
        UpdateCrashLogStatus();

        var asm = Assembly.GetExecutingAssembly();
        VersionText.Text = asm.GetName().Version?.ToString() ?? "0.0.0";
        RuntimeText.Text = $".NET {Environment.Version} • {RuntimeInformation.OSArchitecture}";
        ExePathText.Text = Environment.ProcessPath ?? "(unknown)";
        ExePathText.ToolTip = ExePathText.Text;
    }

    private void UpdateCrashLogStatus()
    {
        if (File.Exists(CrashLogPath))
        {
            var info = new FileInfo(CrashLogPath);
            CrashLogStatus.Text = $"Last modified {info.LastWriteTime:g} — {FormatBytes(info.Length)}";
            OpenCrashLogButton.IsEnabled = true;
            ClearCrashLogButton.IsEnabled = true;
        }
        else
        {
            CrashLogStatus.Text = "No crashes recorded.";
            OpenCrashLogButton.IsEnabled = false;
            ClearCrashLogButton.IsEnabled = false;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }

    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(ConfigDir);
        Process.Start(new ProcessStartInfo
        {
            FileName = ConfigDir,
            UseShellExecute = true
        });
    }

    private void CopyConfigPath_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(ConfigPath);
    }

    private void OpenCrashLog_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(CrashLogPath)) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = CrashLogPath,
            UseShellExecute = true
        });
    }

    private async void ClearCrashLog_Click(object sender, RoutedEventArgs e)
    {
        var ok = await Dialogs.ConfirmAsync(this,
            "Delete crash log?",
            "The crash log file will be permanently deleted.",
            primaryText: "Delete",
            closeText: "Cancel",
            primaryAppearance: ControlAppearance.Caution);
        if (!ok) return;

        try { File.Delete(CrashLogPath); } catch { }
        UpdateCrashLogStatus();
    }

    private async void ClearStartup_Click(object sender, RoutedEventArgs e)
    {
        var ok = await Dialogs.ConfirmAsync(this,
            "Clear startup entries?",
            "This removes any IdlePulse autostart registry values and Startup folder shortcuts. " +
            "IdlePulse won't launch automatically at sign-in until you re-enable it from Idle Trigger.",
            primaryText: "Clear",
            closeText: "Cancel",
            primaryAppearance: ControlAppearance.Caution);
        if (!ok) return;

        var report = AutostartManager.ClearAll();

        var lines = new List<string>();
        if (report.RegistryRemoved)
            lines.Add("• Removed registry value: HKCU\\...\\Run\\IdlePulse");
        foreach (var s in report.StartupShortcutsRemoved)
            lines.Add($"• Removed shortcut: {s}");
        if (!report.FoundAnything)
            lines.Add("No autostart entries found. Nothing to clear.");
        if (report.Errors.Count > 0)
        {
            lines.Add("");
            lines.Add("Errors:");
            foreach (var err in report.Errors)
                lines.Add($"• {err}");
        }

        await Dialogs.NotifyAsync(this,
            report.Errors.Count > 0 ? "Clear completed with errors" : "Clear complete",
            string.Join("\n", lines));
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var ok = await Dialogs.ConfirmAsync(this,
            "Reset app settings?",
            "Scan interval will return to its default. Your Idle Trigger settings (action, threshold, warning, smart skips) are preserved.",
            primaryText: "Reset",
            closeText: "Cancel",
            primaryAppearance: ControlAppearance.Caution);
        if (!ok) return;

        var defaults = new AppConfig();
        _config.ScanIntervalSeconds = defaults.ScanIntervalSeconds;

        _store.Save(_config);
        ConfigSaved?.Invoke(this, _config);
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.ScanIntervalSeconds = (int)(ScanIntervalBox.Value ?? 5);
        _store.Save(_config);
        ConfigSaved?.Invoke(this, _config);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
