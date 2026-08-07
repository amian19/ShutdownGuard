using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class WindowsShutdownExecutorTests
{
    [Fact]
    public void Arguments_AreForcedImmediateShutdown()
    {
        Assert.Equal("/s /f /t 0", WindowsShutdownExecutor.Arguments);
        Assert.Contains("/f", WindowsShutdownExecutor.Arguments);
        Assert.DoesNotContain("/r", WindowsShutdownExecutor.Arguments);
    }
}
