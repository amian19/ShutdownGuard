using ShutdownGuard.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ShutdownGuard.Tests;

/// <summary>
/// Clears DEBUG shutdown-time override before each test class that may schedule against 22:00.
/// </summary>
public abstract class ShutdownPolicyTestCleanup
{
    protected ShutdownPolicyTestCleanup()
    {
        ShutdownPolicy.ResetDebugOverrideForTests();
    }
}
