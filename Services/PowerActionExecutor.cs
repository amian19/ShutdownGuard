using System.Diagnostics;

namespace ShutdownGuard.Services;

public static class PowerActionExecutor
{
    public static void Shutdown()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = "/s /t 0",
            CreateNoWindow = true,
            UseShellExecute = false
        });
    }
}
