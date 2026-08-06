using ShutdownGuard.Core;
using ShutdownGuard.Models;
using Xunit;

namespace ShutdownGuard.Tests;

public class DailyPolicyTests
{
    private readonly DailyPolicy _policy = new();
    private static readonly TimeSpan LocalOffset = DateTimeOffset.Now.Offset;

    private static DateTimeOffset D(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, LocalOffset);

    [Fact]
    public void GetTodayReminder_Disabled_ReturnsNull()
    {
        var plan = new ShutdownPlan { Enabled = false, ReminderStartTime = new TimeOnly(18, 0) };
        Assert.Null(_policy.GetTodayReminder(plan, D(2026, 8, 6, 10, 0)));
    }

    [Fact]
    public void GetTodayReminder_ReturnsTodayOccurrence()
    {
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = _policy.GetTodayReminder(plan, D(2026, 8, 6, 10, 0));

        Assert.Equal(D(2026, 8, 6, 18, 0), result);
    }

    [Fact]
    public void GetTodayReminder_EvenAfterTimePassed_StillReturnsToday()
    {
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = _policy.GetTodayReminder(plan, D(2026, 8, 6, 20, 30));

        Assert.Equal(D(2026, 8, 6, 18, 0), result);
    }

    [Fact]
    public void GetTodayFixedShutdown_Is2200()
    {
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = _policy.GetTodayFixedShutdown(plan, D(2026, 8, 6, 10, 0));

        Assert.Equal(D(2026, 8, 6, 22, 0), result);
    }

    [Fact]
    public void GetNextReminder_TodayInFuture_ReturnsToday()
    {
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = _policy.GetNextReminder(plan, D(2026, 8, 6, 10, 0));

        Assert.Equal(D(2026, 8, 6, 18, 0), result);
    }

    [Fact]
    public void GetNextReminder_TodayAlreadyPassed_ReturnsTomorrow()
    {
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = _policy.GetNextReminder(plan, D(2026, 8, 6, 20, 30));

        Assert.Equal(D(2026, 8, 7, 18, 0), result);
    }

    [Fact]
    public void FixedShutdownTime_IsProductConstant()
    {
        Assert.Equal(new TimeOnly(22, 0), ShutdownPolicy.FixedShutdownTime);
        Assert.Equal(new TimeOnly(18, 0), ShutdownPolicy.DefaultReminderStartTime);
    }
}
