using ShutdownGuard.Core;
using ShutdownGuard.Models;
using Xunit;

namespace ShutdownGuard.Tests;

public class TimezoneTests
{
    private static readonly TimeSpan UtcPlus8 = TimeSpan.FromHours(8);
    private static readonly TimeSpan UtcMinus5 = TimeSpan.FromHours(-5);

    private static DateTimeOffset D8(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, UtcPlus8);

    private static DateTimeOffset Dm5(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, UtcMinus5);

    [Fact]
    public void DailyPolicy_UtcPlus8_Local1800_ReturnsLocalToday()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = policy.GetTodayReminder(plan, D8(2026, 8, 6, 10, 0));

        Assert.Equal(D8(2026, 8, 6, 18, 0), result);
        Assert.Equal(UtcPlus8, result!.Value.Offset);
    }

    [Fact]
    public void DailyPolicy_UtcPlus8_OffsetPreserved()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = policy.GetTodayReminder(plan, D8(2026, 8, 6, 10, 0));

        Assert.Equal(UtcPlus8, result!.Value.Offset);
    }

    [Fact]
    public void DailyPolicy_UtcMinus5_Local1800_ReturnsLocalToday()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = policy.GetTodayReminder(plan, Dm5(2026, 8, 6, 10, 0));

        Assert.Equal(Dm5(2026, 8, 6, 18, 0), result);
        Assert.Equal(UtcMinus5, result!.Value.Offset);
    }

    [Fact]
    public void DailyPolicy_UtcMinus5_OffsetPreserved()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var result = policy.GetTodayReminder(plan, Dm5(2026, 8, 6, 10, 0));

        Assert.Equal(UtcMinus5, result!.Value.Offset);
    }

    [Fact]
    public void SameLocalTime_DifferentOffsets_DifferentUtc()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };

        var result8 = policy.GetTodayReminder(plan, D8(2026, 8, 6, 10, 0));
        var resultMinus5 = policy.GetTodayReminder(plan, Dm5(2026, 8, 6, 10, 0));

        Assert.Equal(18, result8!.Value.Hour);
        Assert.Equal(18, resultMinus5!.Value.Hour);

        var utcDiff = result8.Value.UtcDateTime - resultMinus5.Value.UtcDateTime;
        Assert.Equal(TimeSpan.FromHours(13), utcDiff.Duration());
    }

    [Fact]
    public void ReminderStartTime_IsLocalTimeOnly()
    {
        var plan = new ShutdownPlan { ReminderStartTime = new TimeOnly(18, 0) };
        Assert.Equal(18, plan.ReminderStartTime.Hour);
        Assert.Equal(0, plan.ReminderStartTime.Minute);
    }

    [Fact]
    public void IsWithinReminderWindow_RespectsLocalClockFields()
    {
        var plan = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };

        Assert.True(ShutdownPolicy.IsWithinReminderWindow(plan, D8(2026, 8, 6, 20, 0)));
        Assert.False(ShutdownPolicy.IsWithinReminderWindow(plan, D8(2026, 8, 6, 22, 0)));
        Assert.True(ShutdownPolicy.IsWithinReminderWindow(plan, Dm5(2026, 8, 6, 20, 0)));
    }
}
