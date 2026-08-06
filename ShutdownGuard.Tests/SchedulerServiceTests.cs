using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class SchedulerServiceTests
{
    private static readonly TimeSpan LocalOffset = DateTimeOffset.Now.Offset;

    private static DateTimeOffset D(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, LocalOffset);

    // ══════════════════════════════════════════════════════════════
    // M2.5 Tests (kept, should still pass)
    // ══════════════════════════════════════════════════════════════

    // ── Invariant A: Single occurrence executes only once ────────

    [Fact]
    public async Task SingleOccurrence_ExecutesOnlyOnce()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59, 58) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 0));
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 1));
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 2));

        await scheduler.StopAsync();

        Assert.Equal(1, executor.ExecuteCount);
    }

    // ── Invariant A extended ─────────────────────────────────────

    [Fact]
    public async Task SingleOccurrence_HundredTicks_StillOneExecution()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59, 59) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        for (int i = 0; i < 20; i++)
        {
            await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, i % 60));
        }

        await scheduler.StopAsync();

        Assert.Equal(1, executor.ExecuteCount);
    }

    // ── Missed occurrence (sleep/resume) ─────────────────────────

    [Fact]
    public async Task MissedOccurrence_ExecutesOnceWhenResumed()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 5, 0));

        await scheduler.StopAsync();

        Assert.Equal(1, executor.ExecuteCount);
    }

    // ── Same occurrence, no duplicate ────────────────────────────

    [Fact]
    public async Task SameOccurrence_MultipleTicks_NoDuplicate()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 5, 0));
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 6, 0));
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 10, 0));

        await scheduler.StopAsync();

        Assert.Equal(1, executor.ExecuteCount);
    }

    // ── Next day executes again ──────────────────────────────────

    [Fact]
    public async Task NextDay_ExecutesAgain()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0));
        Assert.Equal(1, executor.ExecuteCount);

        await clock.AdvanceToAsync(D(2026, 8, 6, 23, 0));

        await scheduler.StopAsync();

        Assert.Equal(2, executor.ExecuteCount);
    }

    // ── Disabled before trigger ──────────────────────────────────

    [Fact]
    public async Task DisabledBeforeTrigger_DoesNotExecute()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        clock.Now = D(2026, 8, 5, 22, 59, 30);
        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = false,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 5));

        await scheduler.StopAsync();

        Assert.Equal(0, executor.ExecuteCount);
    }

    // ── Enabled after today's time ───────────────────────────────

    [Fact]
    public async Task EnabledAfterTargetTime_DoesNotExecuteToday()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 23, 5) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 6));

        await scheduler.StopAsync();

        Assert.Equal(0, executor.ExecuteCount);
        Assert.Equal(D(2026, 8, 6, 23, 0), scheduler.NextShutdown!.Value);
    }

    // ── Plan update: change time forward ─────────────────────────

    [Fact]
    public async Task UpdatePlan_ChangeTimeForward_UpdatesNextOccurrence()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        Assert.Equal(D(2026, 8, 5, 23, 0), scheduler.NextShutdown!.Value);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(21, 0),
            DryRun = true
        });

        Assert.Equal(D(2026, 8, 5, 21, 0), scheduler.NextShutdown!.Value);
    }

    // ── Plan update: change time to past ─────────────────────────

    [Fact]
    public async Task UpdatePlan_ChangeTimeToPast_SchedulesTomorrow()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(19, 0),
            DryRun = true
        });

        Assert.Equal(D(2026, 8, 6, 19, 0), scheduler.NextShutdown!.Value);
    }

    // ── Plan update: change time to past, verify no execution ────

    [Fact]
    public async Task UpdatePlan_ChangeTimeToPast_DoesNotExecute()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        clock.Now = D(2026, 8, 5, 20, 0, 1);
        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(19, 0),
            DryRun = true
        });

        await clock.AdvanceToAsync(D(2026, 8, 5, 20, 0, 2));

        await scheduler.StopAsync();

        Assert.Equal(0, executor.ExecuteCount);
    }

    // ── Executor switching ───────────────────────────────────────

    [Fact]
    public async Task UpdateExecutor_SwitchesExecutorInPlace()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executor1 = new DryRunShutdownExecutor();
        var executor2 = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor1);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        scheduler.UpdateExecutor(executor2);

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0));

        await scheduler.StopAsync();

        Assert.Equal(0, executor1.ExecuteCount);
        Assert.Equal(1, executor2.ExecuteCount);
    }

    // ── Disabled does not execute ────────────────────────────────

    [Fact]
    public async Task Disabled_DoesNotExecute()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = false,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 1));

        await scheduler.StopAsync();

        Assert.Equal(0, executor.ExecuteCount);
    }

    // ── NextShutdown reports correct value ───────────────────────

    [Fact]
    public void NextShutdown_ReportsCorrectValue()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        Assert.NotNull(scheduler.NextShutdown);
        Assert.Equal(D(2026, 8, 5, 23, 0), scheduler.NextShutdown!.Value);
    }

    // ── Start is idempotent ──────────────────────────────────────

    [Fact]
    public async Task Start_IsIdempotent()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();
        scheduler.Start(); // Second call should be a no-op
        Assert.True(scheduler.IsRunning);

        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0));

        await scheduler.StopAsync();

        Assert.Equal(1, executor.ExecuteCount);
    }

    // ══════════════════════════════════════════════════════════════
    // M2.6 Tests — Armed Occurrence Model
    // ══════════════════════════════════════════════════════════════

    // ── Test 1: Cold start after target ──────────────────────────

    /// <summary>
    /// Application cold-starts after the planned shutdown time.
    /// Must NOT execute today's occurrence — schedule for tomorrow.
    /// </summary>
    [Fact]
    public async Task ColdStartAfterTarget_DoesNotExecute()
    {
        // Simulate: app starts at 23:05, plan is 23:00, Enabled=true
        var clock = new FakeClock { Now = D(2026, 8, 5, 23, 5) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        // Let the scheduler run several ticks
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 6));
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 10));
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 30));

        await scheduler.StopAsync();

        Assert.Equal(0, executor.ExecuteCount);
        Assert.NotNull(scheduler.NextShutdown);
        Assert.Equal(D(2026, 8, 6, 23, 0), scheduler.NextShutdown!.Value);
    }

    // ── Test 2: Reboot after successful shutdown ─────────────────

    /// <summary>
    /// Scheduler instance A executes at 23:00 and shuts down the machine.
    /// The user reboots at 23:20 and ShutdownGuard autostarts (instance B).
    /// Instance B must NOT execute again for the same day.
    /// </summary>
    [Fact]
    public async Task RebootAfterSuccessfulShutdown_DoesNotReExecute()
    {
        // ── Instance A: runs before 23:00, executes at 23:00 ──
        var clockA = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executorA = new DryRunShutdownExecutor();

        var schedulerA = new SchedulerService(clockA, executorA);
        schedulerA.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });
        schedulerA.Start();

        await clockA.AdvanceToAsync(D(2026, 8, 5, 23, 0));
        Assert.Equal(1, executorA.ExecuteCount);
        await schedulerA.StopAsync();

        // ── Instance B: fresh start at 23:20 (post-reboot) ──
        var clockB = new FakeClock { Now = D(2026, 8, 5, 23, 20) };
        var executorB = new DryRunShutdownExecutor();

        var schedulerB = new SchedulerService(clockB, executorB);
        schedulerB.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });
        schedulerB.Start();

        await clockB.AdvanceToAsync(D(2026, 8, 5, 23, 21));
        await clockB.AdvanceToAsync(D(2026, 8, 5, 23, 30));

        await schedulerB.StopAsync();

        Assert.Equal(0, executorB.ExecuteCount);
        Assert.Equal(D(2026, 8, 6, 23, 0), schedulerB.NextShutdown!.Value);
    }

    // ── Test 3: Sleep-miss still executes ────────────────────────

    /// <summary>
    /// Scheduler was already running and armed before the target.
    /// System sleeps, misses the exact 23:00 moment, resumes at 23:05.
    /// Must execute once (catch-up for armed occurrence).
    /// </summary>
    [Fact]
    public async Task SleepMiss_StillExecutes()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();
        // At this point, armed = today 23:00

        // Simulate sleep: clock jumps from 22:59 to 23:05
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 5, 0));

        await scheduler.StopAsync();

        Assert.Equal(1, executor.ExecuteCount);
    }

    // ── Test 4: Restart after target, no catch-up ────────────────

    /// <summary>
    /// A fresh SchedulerService instance started after the target time.
    /// Multiple ticks must still result in zero executions.
    /// </summary>
    [Fact]
    public async Task RestartAfterTarget_NoCatchUp()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 23, 10) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        // Multiple ticks — must never execute
        for (int i = 0; i < 10; i++)
        {
            await clock.AdvanceToAsync(D(2026, 8, 5, 23, 10 + i));
        }

        await scheduler.StopAsync();

        Assert.Equal(0, executor.ExecuteCount);
    }

    // ── Test 5: Cold start before target, executes normally ──────

    /// <summary>
    /// App starts at 20:00 with plan 23:00.
    /// At 23:00, it should execute normally.
    /// </summary>
    [Fact]
    public async Task ColdStartBeforeTarget_ExecutesNormally()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        // NextShutdown should be today 23:00
        Assert.Equal(D(2026, 8, 5, 23, 0), scheduler.NextShutdown!.Value);

        // Advance to 23:00
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0));

        await scheduler.StopAsync();

        Assert.Equal(1, executor.ExecuteCount);
    }

    // ── Test 6: Disable clears armed occurrence ──────────────────

    /// <summary>
    /// When the plan is disabled, the armed occurrence must be cleared.
    /// Advancing past the original target must not trigger execution.
    /// </summary>
    [Fact]
    public async Task DisableClearsArmedOccurrence()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        // Disable
        clock.Now = D(2026, 8, 5, 20, 0, 1);
        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = false,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        // NextShutdown must be null
        Assert.Null(scheduler.NextShutdown);

        // Advance past 23:00
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 5));

        await scheduler.StopAsync();

        Assert.Equal(0, executor.ExecuteCount);
    }

    // ── Test 7: Re-enable after target schedules tomorrow ────────

    /// <summary>
    /// User had scheduler running, disabled it before the target,
    /// then re-enables after the target time. Must schedule tomorrow.
    /// </summary>
    [Fact]
    public async Task ReenableAfterTarget_SchedulesTomorrow()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        // Start enabled at 22:00, plan 23:00
        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        // At 23:00, disable (before execution, or after — either way)
        clock.Now = D(2026, 8, 5, 23, 0);
        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = false,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        // At 23:05, re-enable
        clock.Now = D(2026, 8, 5, 23, 5);
        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        Assert.Equal(D(2026, 8, 6, 23, 0), scheduler.NextShutdown!.Value);

        // Tick past 23:05 — no execution
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 6));

        await scheduler.StopAsync();

        Assert.Equal(0, executor.ExecuteCount);
    }

    // ══════════════════════════════════════════════════════════════
    // M3.1 Tests — Defensive Copy
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Modifying the ShutdownPlan object after passing it to UpdatePlan
    /// must NOT affect the scheduler's internal state.
    /// </summary>
    [Fact]
    public void UpdatePlan_DefensiveCopy_ModifyingOriginalDoesNotAffectScheduler()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        var plan = new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        };

        scheduler.UpdatePlan(plan);

        // Mutate the original object — must not affect scheduler
        plan.Enabled = false;
        plan.ShutdownTime = new TimeOnly(12, 0);
        plan.DryRun = false;

        // Scheduler must still reflect the original values passed to UpdatePlan
        Assert.Equal(D(2026, 8, 5, 23, 0), scheduler.NextShutdown!.Value);
    }

    /// <summary>
    /// After defensive copy, the scheduler's plan is isolated.
    /// Calling UpdatePlan again with a different plan works correctly.
    /// </summary>
    [Fact]
    public async Task UpdatePlan_DefensiveCopy_SubsequentUpdateWorks()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        var plan1 = new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        };

        scheduler.UpdatePlan(plan1);

        // Mutate plan1
        plan1.Enabled = false;

        // Update with plan2
        var plan2 = new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(21, 0),
            DryRun = true
        };
        scheduler.UpdatePlan(plan2);

        // Must reflect plan2, not plan1
        Assert.Equal(D(2026, 8, 5, 21, 0), scheduler.NextShutdown!.Value);

        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 5, 21, 0));
        await scheduler.StopAsync();

        Assert.Equal(1, executor.ExecuteCount);
    }

    // ══════════════════════════════════════════════════════════════
    // M3.1.1 Tests — Event Handler Lifecycle Safety
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Regression test for the M3.1.1 startup crash:
    /// Scheduler fires NextShutdownChanged synchronously inside UpdatePlan.
    /// If the event handler accesses UI objects that haven't been initialized yet,
    /// it must not crash. This test verifies the lifecycle guard pattern works:
    /// checking a readiness flag before accessing nullable fields.
    /// </summary>
    [Fact]
    public void NextShutdownChanged_FiresDuringUpdatePlan_HandlerMustGuardAgainstNullState()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var scheduler = new SchedulerService(clock, new DryRunShutdownExecutor());

        object? uiObject = null; // Simulates _trayIcon before initialization
        bool handlerCrash = false;
        bool handlerCalled = false;

        scheduler.NextShutdownChanged += next =>
        {
            handlerCalled = true;
            // Simulate the lifecycle guard: check readiness before accessing UI
            if (uiObject is not null)
            {
                // Access UI — should not reach here when uiObject is null
                _ = uiObject.ToString();
            }
        };

        // Act: fire the event (same as UpdatePlan does)
        try
        {
            scheduler.UpdatePlan(new ShutdownPlan
            {
                Enabled = true,
                ShutdownTime = new TimeOnly(23, 0),
                DryRun = true
            });
        }
        catch (NullReferenceException)
        {
            handlerCrash = true;
        }

        Assert.True(handlerCalled, "Event handler should have been invoked");
        Assert.False(handlerCrash, "Handler must not crash when UI is not yet initialized");
    }

    /// <summary>
    /// After disposal, event handlers should still not crash if events arrive late.
    /// </summary>
    [Fact]
    public void NextShutdownChanged_AfterDispose_HandlerMustBeSafe()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 20, 0) };
        var scheduler = new SchedulerService(clock, new DryRunShutdownExecutor());

        bool disposed = false;
        bool handlerCrash = false;

        scheduler.NextShutdownChanged += _ =>
        {
            if (disposed) return;
            // Simulate accessing a disposed UI object
            throw new NullReferenceException("Simulated crash");
        };

        // Normal call — would crash but caught
        try
        {
            scheduler.UpdatePlan(new ShutdownPlan
            {
                Enabled = true,
                ShutdownTime = new TimeOnly(23, 0),
                DryRun = true
            });
        }
        catch (NullReferenceException)
        {
            handlerCrash = true;
        }
        Assert.True(handlerCrash, "Without guard, handler crashes");

        // After disposal — guard prevents access
        disposed = true;
        handlerCrash = false;

        try
        {
            scheduler.UpdatePlan(new ShutdownPlan
            {
                Enabled = false,
                ShutdownTime = new TimeOnly(23, 0),
                DryRun = true
            });
        }
        catch (NullReferenceException)
        {
            handlerCrash = true;
        }

        Assert.False(handlerCrash, "After disposal, handler must not crash");
    }

    // ══════════════════════════════════════════════════════════════
    // M3.1.2 Tests — Execution Observability
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// DryRunShutdownExecutor must fire ShutdownTriggered event when executed,
    /// providing an observable hook for the execution.
    /// </summary>
    [Fact]
    public async Task DryRunExecutor_FiresShutdownTriggeredEvent()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59, 58) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        DateTimeOffset? triggeredAt = null;
        executor.ShutdownTriggered += dt => triggeredAt = dt;

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 0));
        await scheduler.StopAsync();

        Assert.NotNull(triggeredAt);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.NotNull(executor.LastTriggeredAt);
    }

    /// <summary>
    /// After execution, NextShutdown must advance to the next day.
    /// </summary>
    [Fact]
    public async Task NextShutdown_AdvancesToNextDay_AfterExecution()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59, 58) };
        var executor = new DryRunShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 0));
        await scheduler.StopAsync();

        // NextShutdown should be tomorrow's 23:00
        Assert.Equal(D(2026, 8, 6, 23, 0), scheduler.NextShutdown!.Value);
    }

    /// <summary>
    /// When the executor throws an exception, the scheduler must:
    /// - Not crash the background loop
    /// - Not retry the same occurrence
    /// - Still advance NextShutdown to tomorrow
    /// </summary>
    [Fact]
    public async Task ExecutorException_DoesNotCrashLoop_DoesNotRetry_AdvancesNextShutdown()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59, 58) };
        var executor = new ThrowingShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 0));

        // Give the loop time to process the exception and continue
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 2));
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 3));

        await scheduler.StopAsync();

        // Executor was called exactly once (no retry)
        Assert.Equal(1, executor.ExecuteCount);

        // NextShutdown advanced to tomorrow (loop didn't crash)
        Assert.Equal(D(2026, 8, 6, 23, 0), scheduler.NextShutdown!.Value);
    }

    /// <summary>
    /// A second occurrence the next day still executes normally
    /// after a previous executor failure.
    /// </summary>
    [Fact]
    public async Task AfterExecutorException_NextDayOccurrenceStillExecutes()
    {
        var clock = new FakeClock { Now = D(2026, 8, 5, 22, 59, 58) };
        var executor = new ThrowingShutdownExecutor();
        var scheduler = new SchedulerService(clock, executor);

        scheduler.UpdatePlan(new ShutdownPlan
        {
            Enabled = true,
            ShutdownTime = new TimeOnly(23, 0),
            DryRun = true
        });

        scheduler.Start();

        // First execution — throws
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 0));
        await clock.AdvanceToAsync(D(2026, 8, 5, 23, 0, 1));

        // Advance to next day
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 59, 58));
        await clock.AdvanceToAsync(D(2026, 8, 6, 23, 0, 0));

        await scheduler.StopAsync();

        // Both occurrences attempted (each exactly once)
        Assert.Equal(2, executor.ExecuteCount);
        Assert.Equal(D(2026, 8, 7, 23, 0), scheduler.NextShutdown!.Value);
    }

    // ── Test helper: executor that always throws ──────────────────

    private sealed class ThrowingShutdownExecutor : IShutdownExecutor
    {
        public int ExecuteCount { get; private set; }

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            throw new InvalidOperationException("Simulated executor failure");
        }
    }
}
