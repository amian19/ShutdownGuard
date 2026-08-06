using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

/// <summary>
/// Executor classes remain for M4. M3.5 only verifies they still work standalone
/// and are no longer driven by SchedulerService.
/// </summary>
public class DryRunShutdownExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_IncrementsCount_AndFiresEvent()
    {
        var executor = new DryRunShutdownExecutor();
        DateTimeOffset? triggered = null;
        executor.ShutdownTriggered += dt => triggered = dt;

        await executor.ExecuteAsync();

        Assert.Equal(1, executor.ExecuteCount);
        Assert.NotNull(executor.LastTriggeredAt);
        Assert.NotNull(triggered);
    }

    [Fact]
    public async Task ExecuteAsync_CanRunMultipleTimes_WhenCalledDirectly()
    {
        var executor = new DryRunShutdownExecutor();
        await executor.ExecuteAsync();
        await executor.ExecuteAsync();
        Assert.Equal(2, executor.ExecuteCount);
    }
}
