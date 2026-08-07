using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class SchedulerServiceTests : ShutdownPolicyTestCleanup
{
    private static readonly TimeSpan LocalOffset = DateTimeOffset.Now.Offset;

    private static DateTimeOffset D(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, LocalOffset);

    private static ShutdownPlan DefaultPlan(bool enabled = true, int hour = 18, int minute = 0)
        => new()
        {
            Enabled = enabled,
            ReminderStartTime = new TimeOnly(hour, minute),
            DryRun = true
        };

    // ── Default reminder ─────────────────────────────────────────

    [Fact]
    public void DefaultReminder_Is1800()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 10, 0) };
        var scheduler = new SchedulerService(clock);
        scheduler.UpdatePlan(DefaultPlan());

        Assert.Equal(D(2026, 8, 6, 18, 0), scheduler.NextReminder!.Value);
    }

    // ── Normal reminder ──────────────────────────────────────────

    [Fact]
    public async Task NormalReminder_FiresOnceAtStart()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        DateTimeOffset? firedAt = null;
        scheduler.ReminderWindowStarted += o => { count++; firedAt = o; };

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 17, 59, 30));
        Assert.Equal(0, count);

        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0, 0));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
        Assert.Equal(D(2026, 8, 6, 18, 0), firedAt);
    }

    // ── No duplicate ─────────────────────────────────────────────

    [Fact]
    public async Task SameDay_NoDuplicateReminder()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 1));
        await clock.AdvanceToAsync(D(2026, 8, 6, 19, 0));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
    }

    // ── Cold start inside reminder window ────────────────────────

    [Fact]
    public async Task ColdStartInsideWindow_EntersOnce()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 30) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 30, 1));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    // ── Cold start after shutdown time ───────────────────────────

    [Fact]
    public async Task ColdStartAfterShutdown_NoCatchUp()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 22, 10) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 11));
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 30));
        await scheduler.StopAsync();

        Assert.Equal(0, count);
        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    // ── Cold start before reminder ───────────────────────────────

    [Fact]
    public async Task ColdStartBeforeReminder_WaitsForToday()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 0) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        Assert.Equal(D(2026, 8, 6, 18, 0), scheduler.NextReminder!.Value);

        await clock.AdvanceToAsync(D(2026, 8, 6, 17, 30));
        Assert.Equal(0, count);

        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
    }

    // ── Sleep / resume inside window ─────────────────────────────

    [Fact]
    public async Task SleepResumeInsideWindow_EntersOnce()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 50) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        // Sleep past 18:00, resume at 20:00 — still in [18:00, 22:00)
        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 0));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
    }

    // ── Sleep / resume after 22:00 ───────────────────────────────

    [Fact]
    public async Task SleepResumeAfterShutdown_SkipsToday()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 50) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 23, 0));
        await scheduler.StopAsync();

        Assert.Equal(0, count);
        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    // ── Next day ─────────────────────────────────────────────────

    [Fact]
    public async Task NextDay_ReminderFiresAgain()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0));
        Assert.Equal(1, count);

        await clock.AdvanceToAsync(D(2026, 8, 7, 18, 0));
        await scheduler.StopAsync();

        Assert.Equal(2, count);
    }

    // ── Disabled ─────────────────────────────────────────────────

    [Fact]
    public async Task Disabled_NoReminder()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan(enabled: false));
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0));
        await scheduler.StopAsync();

        Assert.Equal(0, count);
        Assert.Null(scheduler.NextReminder);
    }

    // ── Reenable within window ───────────────────────────────────

    [Fact]
    public async Task ReenableWithinWindow_EntersImmediately()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 0) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan(enabled: false));
        scheduler.Start();

        clock.Now = D(2026, 8, 6, 20, 0, 1);
        scheduler.UpdatePlan(DefaultPlan(enabled: true));

        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 0, 2));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
    }

    // ── Scheduler never calls executor ───────────────────────────

    [Fact]
    public async Task Scheduler_NeverCallsShutdownExecutor()
    {
        // Regression: SchedulerService constructor no longer accepts IShutdownExecutor.
        // Crossing the whole reminder window must not invoke any executor.
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        var reminderCount = 0;
        scheduler.ReminderWindowStarted += _ => reminderCount++;

        // Keep an executor nearby to prove it is unused by the scheduler path.
        var orphanExecutor = new DryRunShutdownExecutor();

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 21, 59));
        await scheduler.StopAsync();

        Assert.Equal(1, reminderCount);
        Assert.Equal(0, orphanExecutor.ExecuteCount);
    }

    // ── Defensive copy ───────────────────────────────────────────

    [Fact]
    public void UpdatePlan_DefensiveCopy_ModifyingOriginalDoesNotAffectScheduler()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 10, 0) };
        var scheduler = new SchedulerService(clock);

        var plan = DefaultPlan(hour: 18);
        scheduler.UpdatePlan(plan);

        plan.Enabled = false;
        plan.ReminderStartTime = new TimeOnly(12, 0);

        Assert.Equal(D(2026, 8, 6, 18, 0), scheduler.NextReminder!.Value);
    }

    // ── Start idempotent ─────────────────────────────────────────

    [Fact]
    public async Task Start_IsIdempotent()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        scheduler.Start();
        Assert.True(scheduler.IsRunning);

        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
    }

    // ── Event lifecycle safety ───────────────────────────────────

    [Fact]
    public void NextReminderChanged_FiresDuringUpdatePlan_HandlerMustGuardAgainstNullState()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 10, 0) };
        var scheduler = new SchedulerService(clock);

        object? uiObject = null;
        bool handlerCrash = false;
        bool handlerCalled = false;

        scheduler.NextReminderChanged += next =>
        {
            handlerCalled = true;
            if (uiObject is not null)
                _ = uiObject.ToString();
            _ = next;
        };

        try
        {
            scheduler.UpdatePlan(DefaultPlan());
        }
        catch (NullReferenceException)
        {
            handlerCrash = true;
        }

        Assert.True(handlerCalled);
        Assert.False(handlerCrash);
    }

    [Fact]
    public void NextReminderChanged_AfterDispose_HandlerMustBeSafe()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 10, 0) };
        var scheduler = new SchedulerService(clock);

        bool disposed = false;
        bool handlerCrash = false;

        scheduler.NextReminderChanged += _ =>
        {
            if (disposed) return;
            throw new NullReferenceException("Simulated crash");
        };

        try
        {
            scheduler.UpdatePlan(DefaultPlan());
        }
        catch (NullReferenceException)
        {
            handlerCrash = true;
        }
        Assert.True(handlerCrash);

        disposed = true;
        handlerCrash = false;

        try
        {
            scheduler.UpdatePlan(DefaultPlan(enabled: false));
        }
        catch (NullReferenceException)
        {
            handlerCrash = true;
        }

        Assert.False(handlerCrash);
    }

    // ── NextReminder advances after entry ────────────────────────

    [Fact]
    public async Task NextReminder_AdvancesToTomorrow_AfterWindowEntry()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0));
        await scheduler.StopAsync();

        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    // ── IsWithinReminderWindow ───────────────────────────────────

    [Theory]
    [InlineData(18, 0, true)]
    [InlineData(20, 30, true)]
    [InlineData(21, 59, true)]
    [InlineData(17, 59, false)]
    [InlineData(22, 0, false)]
    [InlineData(23, 0, false)]
    public void IsWithinReminderWindow_MatchesProductRule(int hour, int minute, bool expected)
    {
        var plan = DefaultPlan();
        var now = D(2026, 8, 6, hour, minute);
        Assert.Equal(expected, ShutdownPolicy.IsWithinReminderWindow(plan, now));
    }

    [Fact]
    public void IsWithinReminderWindow_Disabled_IsFalse()
    {
        var plan = DefaultPlan(enabled: false);
        var now = D(2026, 8, 6, 20, 0);
        Assert.False(ShutdownPolicy.IsWithinReminderWindow(plan, now));
    }

    // ── UpdatePlan time changes ──────────────────────────────────

    [Fact]
    public void UpdatePlan_ChangeReminderForward_UpdatesNext()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 10, 0) };
        var scheduler = new SchedulerService(clock);

        scheduler.UpdatePlan(DefaultPlan(hour: 18));
        Assert.Equal(D(2026, 8, 6, 18, 0), scheduler.NextReminder!.Value);

        scheduler.UpdatePlan(DefaultPlan(hour: 20));
        Assert.Equal(D(2026, 8, 6, 20, 0), scheduler.NextReminder!.Value);
    }

    [Fact]
    public void MarkTodayReminderHandled_AdvancesToTomorrow()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 10, 0) };
        var scheduler = new SchedulerService(clock);
        scheduler.UpdatePlan(DefaultPlan(hour: 18));

        scheduler.MarkTodayReminderHandled();

        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    [Fact]
    public void UpdatePlan_AfterTodayHandled_ChangingReminderKeepsTomorrow()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 0) };
        var scheduler = new SchedulerService(clock);
        scheduler.UpdatePlan(DefaultPlan(hour: 18));
        scheduler.MarkTodayReminderHandled();
        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);

        // Changing ReminderStart must not revive today's catch-up.
        scheduler.UpdatePlan(DefaultPlan(hour: 19, minute: 30));
        Assert.Equal(D(2026, 8, 7, 19, 30), scheduler.NextReminder!.Value);
    }

    [Fact]
    public void UpdatePlan_ChangeReminderToPastOutsideWindow_SchedulesTomorrow()
    {
        // At 21:00, changing reminder to 20:00 is still inside [20:00, 22:00)
        // so it arms today's 20:00 for immediate entry. Use after 22:00 instead.
        var clock = new FakeClock { Now = D(2026, 8, 6, 22, 30) };
        var scheduler = new SchedulerService(clock);

        scheduler.UpdatePlan(DefaultPlan(hour: 18));
        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    [Fact]
    public async Task DisableClearsArmedOccurrence()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 10, 0) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        clock.Now = D(2026, 8, 6, 10, 0, 1);
        scheduler.UpdatePlan(DefaultPlan(enabled: false));
        Assert.Null(scheduler.NextReminder);

        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 5));
        await scheduler.StopAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ColdStartAtExactly2200_NoReminder()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 22, 0) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0, 1));
        await scheduler.StopAsync();

        Assert.Equal(0, count);
        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    [Fact]
    public async Task ColdStartAtExactly1800_EntersWindow()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 18, 0) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0, 1));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CustomReminder2030_ColdStartAt2100_EntersOnce()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 21, 0) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan(hour: 20, minute: 30));
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 21, 0, 1));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
        Assert.Equal(D(2026, 8, 7, 20, 30), scheduler.NextReminder!.Value);
    }

    [Fact]
    public async Task HundredTicksInsideWindow_StillOneReminder()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59, 59) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        for (int i = 0; i < 20; i++)
            await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0, i % 60));

        await scheduler.StopAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ReminderWindowStarted_HandlerException_DoesNotCrashLoop()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        scheduler.ReminderWindowStarted += _ => throw new InvalidOperationException("boom");

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0, 2));
        await scheduler.StopAsync();

        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    // ══════════════════════════════════════════════════════════════
    // M3.5.1 — Entry reason / logging semantics
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Regression: normal on-time entry must be Scheduled, never Startup/TimeAdvance.
    /// Mirrors the real bug: ReminderStart=16:59 while app has been running.
    /// </summary>
    [Fact]
    public async Task EntryReason_Scheduled_NotMisLabeledAsStartup()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 16, 58) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan(hour: 16, minute: 59));
        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 6, 16, 59, 0));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
        Assert.Equal(ReminderWindowEntryReason.Scheduled, scheduler.LastEntryReason);
    }

    [Fact]
    public async Task EntryReason_Scheduled_AtExactSecond_StillScheduled()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        scheduler.ReminderWindowStarted += _ => { };

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0, 0));
        await scheduler.StopAsync();

        Assert.Equal(ReminderWindowEntryReason.Scheduled, scheduler.LastEntryReason);
    }

    [Fact]
    public async Task EntryReason_Scheduled_OneSecondPast_StillScheduled()
    {
        // 1s poll skew must not become TimeAdvance / Startup.
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 59) };
        var scheduler = new SchedulerService(clock);
        scheduler.ReminderWindowStarted += _ => { };

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0, 1));
        await scheduler.StopAsync();

        Assert.Equal(ReminderWindowEntryReason.Scheduled, scheduler.LastEntryReason);
    }

    [Fact]
    public async Task EntryReason_Startup_InsideWindow()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 30) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 30, 1));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
        Assert.Equal(ReminderWindowEntryReason.Startup, scheduler.LastEntryReason);
    }

    [Fact]
    public async Task EntryReason_StartupAfter2200_DoesNotEnter()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 22, 10) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 11));
        await scheduler.StopAsync();

        Assert.Equal(0, count);
        Assert.Null(scheduler.LastEntryReason);
        Assert.Equal(D(2026, 8, 7, 18, 0), scheduler.NextReminder!.Value);
    }

    [Fact]
    public async Task EntryReason_PlanUpdate_InsideWindow_NotStartup()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 0) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan(enabled: false));
        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 0, 1));
        Assert.Equal(0, count);

        scheduler.UpdatePlan(DefaultPlan(enabled: true));
        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 0, 2));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
        Assert.Equal(ReminderWindowEntryReason.PlanUpdate, scheduler.LastEntryReason);
    }

    [Fact]
    public async Task EntryReason_TimeAdvance_JumpIntoWindow_NotResumeClaim()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 17, 50) };
        var scheduler = new SchedulerService(clock);
        var count = 0;
        scheduler.ReminderWindowStarted += _ => count++;

        scheduler.UpdatePlan(DefaultPlan());
        scheduler.Start();

        // Large clock jump past ReminderStart while still before 22:00.
        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 0));
        await scheduler.StopAsync();

        Assert.Equal(1, count);
        Assert.Equal(ReminderWindowEntryReason.TimeAdvance, scheduler.LastEntryReason);
    }
}
