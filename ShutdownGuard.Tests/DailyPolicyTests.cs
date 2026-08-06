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

    // ── GetTodayTarget ──────────────────────────────────────────

    [Fact]
    public void GetTodayTarget_Disabled_ReturnsNull()
    {
        var plan = new ShutdownPlan { Enabled = false, ShutdownTime = new TimeOnly(23, 0) };
        var now = D(2026, 8, 5, 20, 0);

        var result = _policy.GetTodayTarget(plan, now);

        Assert.Null(result);
    }

    [Fact]
    public void GetTodayTarget_ReturnsTodayOccurrence()
    {
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };
        var now = D(2026, 8, 5, 20, 0);

        var result = _policy.GetTodayTarget(plan, now);

        Assert.NotNull(result);
        Assert.Equal(D(2026, 8, 5, 23, 0), result.Value);
    }

    [Fact]
    public void GetTodayTarget_EvenAfterTimePassed_StillReturnsToday()
    {
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };
        var now = D(2026, 8, 5, 23, 30);

        var result = _policy.GetTodayTarget(plan, now);

        Assert.NotNull(result);
        Assert.Equal(D(2026, 8, 5, 23, 0), result.Value);
    }

    [Fact]
    public void GetTodayTarget_AcrossMidnight()
    {
        // At 23:59, "today" is still the current calendar day.
        // GetTodayTarget always returns today's date + plan time.
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(0, 1) };
        var now = D(2026, 8, 5, 23, 59);

        var result = _policy.GetTodayTarget(plan, now);

        Assert.NotNull(result);
        // Today (Aug 5) at 00:01 — this has already passed,
        // but GetTodayTarget always returns today's occurrence.
        // The Scheduler handles the "already passed" logic.
        Assert.Equal(D(2026, 8, 5, 0, 1), result.Value);
    }

    // ── GetNextOccurrence (display) ──────────────────────────────

    [Fact]
    public void GetNextOccurrence_TodayInFuture_ReturnsToday()
    {
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };
        var now = D(2026, 8, 5, 20, 0);

        var result = _policy.GetNextOccurrence(plan, now);

        Assert.NotNull(result);
        Assert.Equal(D(2026, 8, 5, 23, 0), result.Value);
    }

    [Fact]
    public void GetNextOccurrence_TodayAlreadyPassed_ReturnsTomorrow()
    {
        var plan = new ShutdownPlan { Enabled = true, ShutdownTime = new TimeOnly(23, 0) };
        var now = D(2026, 8, 5, 23, 30);

        var result = _policy.GetNextOccurrence(plan, now);

        Assert.NotNull(result);
        Assert.Equal(D(2026, 8, 6, 23, 0), result.Value);
    }
}
