using System.Diagnostics;

namespace ShutdownGuard.Services;

public sealed class WindowsShutdownExecutor : IShutdownExecutor
{
    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
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
