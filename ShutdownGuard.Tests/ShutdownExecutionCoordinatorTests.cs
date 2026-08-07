using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class ShutdownExecutionCoordinatorTests : ShutdownPolicyTestCleanup
{
    private static readonly TimeSpan LocalOffset = DateTimeOffset.Now.Offset;

    private static DateTimeOffset D(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, LocalOffset);

    private static ShutdownDueInfo Due(int y, int m, int d) =>
        new(
            new DateOnly(y, m, d),
            D(y, m, d, 22, 0),
            D(y, m, d, 18, 0));

    private static (ShutdownExecutionCoordinator coord, SpyExecutor dry, SpyExecutor real, MutableAppConfigProvider cfg, MemoryDailyCancellationStore cancel, FakeClock clock)
        Create(
            bool enabled = true,
            bool dryRun = true,
            DateTimeOffset? now = null)
    {
        var clock = new FakeClock { Now = now ?? D(2026, 8, 6, 22, 0) };
        var cfg = new MutableAppConfigProvider(new AppConfig
        {
            Shutdown = new ShutdownPlan { Enabled = enabled, DryRun = dryRun, ReminderStartTime = new TimeOnly(18, 0) }
        });
        var cancel = new MemoryDailyCancellationStore();
        var dry = new SpyExecutor();
        var real = new SpyExecutor();
        var coord = new ShutdownExecutionCoordinator(clock, cfg, cancel, dry, real);
        return (coord, dry, real, cfg, cancel, clock);
    }

    [Fact]
    public async Task DryRun_ExecutesDryRunOnce()
    {
        var (coord, dry, real, _, _, _) = Create(dryRun: true);
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(1, dry.ExecuteCount);
        Assert.Equal(0, real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.DryRunCompleted, coord.Current.Phase);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task Real_SelectsRealExecutor_NotWindows()
    {
        var (coord, dry, real, _, _, _) = Create(dryRun: false);
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(0, dry.ExecuteCount);
        Assert.Equal(1, real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.RealShutdownRequested, coord.Current.Phase);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task Disabled_Blocks()
    {
        var (coord, dry, real, _, _, _) = Create(enabled: false, dryRun: true);
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(0, dry.ExecuteCount);
        Assert.Equal(0, real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.Blocked, coord.Current.Phase);
        Assert.Contains("停用", coord.Current.Message);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task Cancelled_Blocks()
    {
        var (coord, dry, real, _, cancel, _) = Create();
        cancel.SaveCancelledDate(new DateOnly(2026, 8, 6));
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(0, dry.ExecuteCount + real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.Blocked, coord.Current.Phase);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task CorruptState_BlocksReal()
    {
        var (coord, dry, real, _, cancel, _) = Create(dryRun: false);
        cancel.CorruptOnRead = true;
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(0, dry.ExecuteCount);
        Assert.Equal(0, real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.Blocked, coord.Current.Phase);
        Assert.Contains("取消状态", coord.Current.Message);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task CorruptState_DryRun_StillSimulates_ButFlagsWouldBlock()
    {
        var (coord, dry, real, _, cancel, _) = Create(dryRun: true);
        cancel.CorruptOnRead = true;
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(1, dry.ExecuteCount);
        Assert.Equal(0, real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.DryRunCompleted, coord.Current.Phase);
        Assert.True(coord.Current.RealShutdownWouldBeBlocked);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task ConfigUnavailable_Blocks()
    {
        var (coord, dry, real, cfg, _, _) = Create(dryRun: false);
        cfg.MarkUnavailable("boom");
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(0, dry.ExecuteCount + real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.Blocked, coord.Current.Phase);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task ExecutorException_Failed_NoRetry()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 22, 0) };
        var cfg = new MutableAppConfigProvider(new AppConfig
        {
            Shutdown = new ShutdownPlan { Enabled = true, DryRun = true }
        });
        var dry = new SpyExecutor { ThrowOnExecute = true };
        var real = new SpyExecutor();
        var coord = new ShutdownExecutionCoordinator(
            clock, cfg, new MemoryDailyCancellationStore(), dry, real);

        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(1, dry.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.Failed, coord.Current.Phase);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateShutdownDue_SameOccurrence_ExactlyOnce()
    {
        var (coord, dry, real, _, _, _) = Create();
        var due = Due(2026, 8, 6);
        await coord.HandleShutdownDueAsync(due);
        await coord.HandleShutdownDueAsync(due);
        await coord.HandleShutdownDueAsync(due);

        Assert.Equal(1, dry.ExecuteCount);
        Assert.Equal(0, real.ExecuteCount);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task NextDayOccurrence_AllowedAgain()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 22, 0) };
        var cfg = new MutableAppConfigProvider(new AppConfig
        {
            Shutdown = new ShutdownPlan { Enabled = true, DryRun = true }
        });
        var dry = new SpyExecutor();
        var coord = new ShutdownExecutionCoordinator(
            clock, cfg, new MemoryDailyCancellationStore(), dry, new SpyExecutor());

        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));
        clock.Now = D(2026, 8, 7, 22, 0);
        await coord.HandleShutdownDueAsync(Due(2026, 8, 7));

        Assert.Equal(2, dry.ExecuteCount);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task CancelThenDue_Race_Blocks()
    {
        var (coord, dry, real, _, cancel, _) = Create();
        cancel.SaveCancelledDate(new DateOnly(2026, 8, 6));
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(0, dry.ExecuteCount + real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.Blocked, coord.Current.Phase);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task DisableThenDue_Race_Blocks()
    {
        var (coord, dry, real, cfg, _, _) = Create();
        cfg.Update(new AppConfig
        {
            Shutdown = new ShutdownPlan { Enabled = false, DryRun = true }
        });
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(0, dry.ExecuteCount + real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.Blocked, coord.Current.Phase);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task DryRunFlipToFalse_UsesLatestConfig()
    {
        var (coord, dry, real, cfg, _, _) = Create(dryRun: true);
        cfg.Update(new AppConfig
        {
            Shutdown = new ShutdownPlan { Enabled = true, DryRun = false }
        });
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(0, dry.ExecuteCount);
        Assert.Equal(1, real.ExecuteCount);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task DryRunFlipToTrue_UsesLatestConfig()
    {
        var (coord, dry, real, cfg, _, _) = Create(dryRun: false);
        cfg.Update(new AppConfig
        {
            Shutdown = new ShutdownPlan { Enabled = true, DryRun = true }
        });
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(1, dry.ExecuteCount);
        Assert.Equal(0, real.ExecuteCount);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task StateChangedHandlerException_DoesNotCrashOrRetry()
    {
        var (coord, dry, _, _, _, _) = Create();
        coord.StateChanged += _ => throw new InvalidOperationException("ui boom");
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));
        await coord.HandleShutdownDueAsync(Due(2026, 8, 6));

        Assert.Equal(1, dry.ExecuteCount);
        await coord.DisposeAsync();
    }

    [Fact]
    public async Task InvalidShutdownTime_Blocks()
    {
        var (coord, dry, real, _, _, _) = Create();
        var bad = new ShutdownDueInfo(
            new DateOnly(2026, 8, 6),
            D(2026, 8, 6, 21, 0),
            D(2026, 8, 6, 18, 0));
        await coord.HandleShutdownDueAsync(bad);

        Assert.Equal(0, dry.ExecuteCount + real.ExecuteCount);
        Assert.Equal(ShutdownExecutionPhase.Blocked, coord.Current.Phase);
        await coord.DisposeAsync();
    }

    [Fact]
    public void DefaultAppConfig_DryRunIsFalse()
    {
        Assert.False(new AppConfig().Shutdown.DryRun);
        Assert.False(new ShutdownPlan().DryRun);
    }

    private sealed class SpyExecutor : IShutdownExecutor
    {
        public int ExecuteCount { get; private set; }
        public bool ThrowOnExecute { get; set; }

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            if (ThrowOnExecute)
                throw new InvalidOperationException("Simulated executor failure");
            return Task.CompletedTask;
        }
    }
}
