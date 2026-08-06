using ShutdownGuard.Core;
using ShutdownGuard.Models;
using Xunit;

namespace ShutdownGuard.Tests;

public class ShutdownPolicyTests
{
    private static readonly TimeSpan LocalOffset = DateTimeOffset.Now.Offset;

    private static DateTimeOffset D(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, LocalOffset);

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(17, 59, true)]
    [InlineData(18, 0, true)]
    [InlineData(21, 59, true)]
    [InlineData(22, 0, false)]
    [InlineData(22, 30, false)]
    [InlineData(23, 0, false)]
    public void IsValidReminderStartTime(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, ShutdownPolicy.IsValidReminderStartTime(new TimeOnly(hour, minute)));
    }

    [Fact]
    public void ReminderWindow_DynamicLength_DependsOnReminderStart()
    {
        var plan1800 = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(18, 0) };
        var plan2030 = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(20, 30) };
        var plan2155 = new ShutdownPlan { Enabled = true, ReminderStartTime = new TimeOnly(21, 55) };

        Assert.True(ShutdownPolicy.IsWithinReminderWindow(plan1800, D(2026, 8, 6, 18, 0)));
        Assert.True(ShutdownPolicy.IsWithinReminderWindow(plan1800, D(2026, 8, 6, 21, 59)));
        Assert.False(ShutdownPolicy.IsWithinReminderWindow(plan2030, D(2026, 8, 6, 20, 29)));
        Assert.True(ShutdownPolicy.IsWithinReminderWindow(plan2030, D(2026, 8, 6, 20, 30)));
        Assert.True(ShutdownPolicy.IsWithinReminderWindow(plan2155, D(2026, 8, 6, 21, 55)));
        Assert.False(ShutdownPolicy.IsWithinReminderWindow(plan2155, D(2026, 8, 6, 22, 0)));
    }
}
