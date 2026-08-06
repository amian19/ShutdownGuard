using System.Runtime.CompilerServices;
using ShutdownGuard.Services;

namespace ShutdownGuard.Tests;

/// <summary>
/// Redirects AppLogger to a unique temp directory for the entire test process
/// so automated tests never pollute %LOCALAPPDATA%\ShutdownGuard\logs.
/// </summary>
internal static class TestLoggingBootstrap
{
    internal static readonly string RootDirectory = Path.Combine(
        Path.GetTempPath(),
        "ShutdownGuard.Tests",
        Guid.NewGuid().ToString("N"));

    internal static readonly string LogDirectory = Path.Combine(RootDirectory, "logs");

    [ModuleInitializer]
    internal static void Initialize()
    {
        Directory.CreateDirectory(LogDirectory);
        AppLogger.ConfigureLogDirectory(LogDirectory);
        AppLogger.Init();
    }
}
