using System.Runtime.InteropServices;

namespace ShutdownGuard.Native;

internal static partial class NativeMethods
{
    // Reserved for future Win32 API needs (e.g., shutdown-related P/Invoke).
    // Currently shutdown is handled via shutdown.exe in PowerActionExecutor.
}
