using System.Diagnostics;
using System.IO;
using System.Text;

namespace ShutdownGuard.Services;

/// <summary>
/// Applies an already-downloaded exe by spawning a helper script that waits for
/// this process to exit, replaces the file, then restarts.
/// </summary>
public static class UpdateApplier
{
    public static string PrepareDownloadedPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShutdownGuard",
            "updates");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "ShutdownGuard.exe.new");
    }

    /// <summary>
    /// Starts a cmd helper and returns immediately. Caller should shut down the app.
    /// </summary>
    public static void LaunchReplaceAndRestart(string downloadedExePath)
    {
        var target = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前程序路径。");

        if (!File.Exists(downloadedExePath))
            throw new FileNotFoundException("更新包不存在。", downloadedExePath);

        var pid = Environment.ProcessId;
        var scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"ShutdownGuard-update-{pid}.cmd");

        // Wait for PID to exit, copy over target, relaunch, clean up.
        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("setlocal");
        script.AppendLine($":wait");
        script.AppendLine($"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >NUL");
        script.AppendLine("if not errorlevel 1 (");
        script.AppendLine("  timeout /t 1 /nobreak >NUL");
        script.AppendLine("  goto wait");
        script.AppendLine(")");
        script.AppendLine($"copy /Y \"{downloadedExePath}\" \"{target}\" >NUL");
        if (Directory.Exists(Path.GetDirectoryName(downloadedExePath)))
            script.AppendLine($"del /F /Q \"{downloadedExePath}\" >NUL 2>&1");
        script.AppendLine($"start \"\" \"{target}\"");
        script.AppendLine($"del /F /Q \"%~f0\" >NUL 2>&1");

        File.WriteAllText(scriptPath, script.ToString(), Encoding.ASCII);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/C \"\"{scriptPath}\"\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };

        Process.Start(psi);
        AppLogger.Info($"Update helper started; will replace {target} after exit");
    }
}
