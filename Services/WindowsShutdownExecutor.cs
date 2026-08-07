using System.Diagnostics;

namespace ShutdownGuard.Services;

/// <summary>
/// Requests a forced Windows system shutdown via <c>shutdown.exe /s /f /t 0</c>.
/// Works for typical interactive user sessions on Windows 10/11.
/// Not guaranteed under: group policy blocking shutdown, missing privilege,
/// or locked-down enterprise images.
/// </summary>
public sealed class WindowsShutdownExecutor : IShutdownExecutor
{
    public const string Arguments = "/s /f /t 0";

    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        AppLogger.Info($"Real shutdown requested at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown.exe",
                Arguments = Arguments,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            if (process is null)
            {
                throw new InvalidOperationException("未能启动 shutdown.exe（Process.Start 返回 null）。");
            }

            AppLogger.Info($"shutdown.exe started (pid={process.Id}, args={Arguments})");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to start shutdown.exe: {ex.Message}");
            throw;
        }

        return Task.CompletedTask;
    }
}
