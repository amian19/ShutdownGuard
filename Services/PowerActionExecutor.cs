using System.Diagnostics;
using ShutdownGuard.Models;
using ShutdownGuard.Native;

namespace ShutdownGuard.Services;

public static class PowerActionExecutor
{
    public static void Execute(PowerAction action)
    {
        switch (action)
        {
            case PowerAction.Sleep:
                NativeMethods.SetSuspendState(false, false, false);
                break;
            case PowerAction.Hibernate:
                NativeMethods.SetSuspendState(true, false, false);
                break;
            case PowerAction.Lock:
                NativeMethods.LockWorkStation();
                break;
            case PowerAction.Shutdown:
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/s /t 0",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                break;
        }
    }
}
