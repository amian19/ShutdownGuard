using ShutdownGuard.Core;
using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class DebugShutdownSimulationTests : ShutdownPolicyTestCleanup
{
    private static readonly TimeSpan LocalOffset = DateTimeOffset.Now.Offset;

    [Fact]
    public void CreateTodayFixedDue_UsesProductFixed2200_NotClockTime()
    {
        ShutdownPolicy.ResetDebugOverrideForTests();
        var now = new DateTimeOffset(2026, 8, 7, 10, 30, 0, LocalOffset);
        var due = DebugShutdownSimulation.CreateTodayFixedDue(now);

        Assert.Equal(new DateOnly(2026, 8, 7), due.OccurrenceDate);
        Assert.Equal(new TimeOnly(22, 0), TimeOnly.FromDateTime(due.ShutdownAt.DateTime));
        Assert.Equal(ShutdownPolicy.FixedShutdownTime, TimeOnly.FromDateTime(due.ShutdownAt.DateTime));
        Assert.Equal(new TimeOnly(18, 0), TimeOnly.FromDateTime(due.ReminderStartedAt.DateTime));
    }

    [Fact]
    public void CreateTodayFixedDue_RespectsDebugOverride()
    {
        try
        {
            ShutdownPolicy.ApplyDebugFixedShutdownOverride(new TimeOnly(10, 35));
            var now = new DateTimeOffset(2026, 8, 7, 10, 30, 0, LocalOffset);
            var due = DebugShutdownSimulation.CreateTodayFixedDue(now);

            Assert.Equal(new TimeOnly(10, 35), TimeOnly.FromDateTime(due.ShutdownAt.DateTime));
            Assert.Equal(new TimeOnly(10, 35), ShutdownPolicy.EffectiveFixedShutdownTime);
        }
        finally
        {
            ShutdownPolicy.ResetDebugOverrideForTests();
        }
    }

    [Fact]
    public void CreateTodayFixedDue_PreservesReminderStartedAtWhenProvided()
    {
        var now = new DateTimeOffset(2026, 8, 7, 20, 0, 0, LocalOffset);
        var reminder = new DateTimeOffset(2026, 8, 7, 19, 15, 0, LocalOffset);
        var due = DebugShutdownSimulation.CreateTodayFixedDue(now, reminder);

        Assert.Equal(reminder, due.ReminderStartedAt);
        Assert.Equal(new DateTimeOffset(2026, 8, 7, 22, 0, 0, LocalOffset), due.ShutdownAt);
    }
}
