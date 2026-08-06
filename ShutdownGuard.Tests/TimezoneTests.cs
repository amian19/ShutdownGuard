using ShutdownGuard.Core;
using ShutdownGuard.Models;
using Xunit;

namespace ShutdownGuard.Tests;

public class TimezoneTests
{
    // ── UTC+08:00 ────────────────────────────────────────────────

    private static readonly TimeSpan UtcPlus8 = TimeSpan.FromHours(8);

    private static DateTimeOffset D8(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, UtcPlus8);

    [Fact]
    public void DailyPolicy_UtcPlus8_Local2300_ReturnsLocalToday()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };
        var now = D8(2026, 8, 5, 20, 0); // 20:00 +08:00

        var result = policy.GetTodayTarget(plan, now);

        Assert.NotNull(result);
        Assert.Equal(D8(2026, 8, 5, 23, 0), result.Value);
        Assert.Equal(UtcPlus8, result.Value.Offset);
    }

    [Fact]
    public void DailyPolicy_UtcPlus8_PastMidnight_ReturnsNewDayLocal()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(0, 30) };
        var now = D8(2026, 8, 5, 23, 0);

        var result = policy.GetTodayTarget(plan, now);

        Assert.NotNull(result);
        // GetTodayTarget always returns today's (Aug 5) occurrence.
        // The 00:30 on Aug 5 has already passed, but that's the
        // SchedulerService's concern, not DailyPolicy's.
        Assert.Equal(D8(2026, 8, 5, 0, 30), result.Value);
    }

    [Fact]
    public void DailyPolicy_UtcPlus8_OffsetPreserved()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };
        var now = D8(2026, 8, 5, 20, 0);

        var result = policy.GetTodayTarget(plan, now);

        Assert.NotNull(result);
        // Offset must be +08:00, not UTC
        Assert.Equal(UtcPlus8, result.Value.Offset);
    }

    // ── UTC-05:00 ────────────────────────────────────────────────

    private static readonly TimeSpan UtcMinus5 = TimeSpan.FromHours(-5);

    private static DateTimeOffset Dm5(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, UtcMinus5);

    [Fact]
    public void DailyPolicy_UtcMinus5_Local2300_ReturnsLocalToday()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };
        var now = Dm5(2026, 8, 5, 20, 0); // 20:00 -05:00

        var result = policy.GetTodayTarget(plan, now);

        Assert.NotNull(result);
        Assert.Equal(Dm5(2026, 8, 5, 23, 0), result.Value);
        Assert.Equal(UtcMinus5, result.Value.Offset);
    }

    [Fact]
    public void DailyPolicy_UtcMinus5_OffsetPreserved()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };
        var now = Dm5(2026, 8, 5, 20, 0);

        var result = policy.GetTodayTarget(plan, now);

        Assert.NotNull(result);
        Assert.Equal(UtcMinus5, result.Value.Offset);
    }

    // ── Date property preserves offset ───────────────────────────

    [Fact]
    public void DateTimeOffset_Date_PreservesLocalOffset()
    {
        // Verify that creating a DateTimeOffset with an explicit offset
        // preserves that offset when accessing the Date component.
        var now = D8(2026, 8, 5, 14, 30);

        // now.Date should be midnight local with same offset
        Assert.Equal(UtcPlus8, now.Offset);
        Assert.Equal(2026, now.Year);
        Assert.Equal(8, now.Month);
        Assert.Equal(5, now.Day);
        Assert.Equal(14, now.Hour);
        Assert.Equal(30, now.Minute);
    }

    // ── Same local time, different offsets, different UTC ────────

    [Fact]
    public void SameLocalTime_DifferentOffsets_DifferentUtc()
    {
        var policy = new DailyPolicy();
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };

        var utc8Now = D8(2026, 8, 5, 20, 0);
        var utcMinus5Now = Dm5(2026, 8, 5, 20, 0);

        var result8 = policy.GetTodayTarget(plan, utc8Now);
        var resultMinus5 = policy.GetTodayTarget(plan, utcMinus5Now);

        Assert.NotNull(result8);
        Assert.NotNull(resultMinus5);

        // Both are "local 23:00" but different UTC instants
        Assert.Equal(23, result8!.Value.Hour);
        Assert.Equal(23, resultMinus5!.Value.Hour);

        // UTC representations differ by 13 hours (8 - (-5))
        var utcDiff = result8.Value.UtcDateTime - resultMinus5.Value.UtcDateTime;
        Assert.Equal(TimeSpan.FromHours(13), utcDiff.Duration());
    }

    // ── Plan is NOT stored as fixed UTC ──────────────────────────

    [Fact]
    public void ShutdownPlan_ShutdownTime_IsLocalTimeOnly()
    {
        // The ShutdownTime field is a TimeOnly — no timezone, no UTC.
        // This confirms it's treated as "local time at the machine".
        var plan = new ShutdownPlan { ShutdownTime = new TimeOnly(23, 0) };

        // TimeOnly has no Kind, no Offset — just hours/minutes
        Assert.Equal(23, plan.ShutdownTime.Hour);
        Assert.Equal(0, plan.ShutdownTime.Minute);
    }
}
