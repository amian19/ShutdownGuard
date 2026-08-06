using System.Diagnostics;

namespace ShutdownGuard.Services;

public sealed class WindowsShutdownExecutor : IShutdownExecutor
{
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        AppLogger.Info($"Real shutdown requested at {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");

        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/s /t 0",
            CreateNoWindow = true,
            UseShellExecute = false
        });

        return Task.CompletedTask;
    }
}
