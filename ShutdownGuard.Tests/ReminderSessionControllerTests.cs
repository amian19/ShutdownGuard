using ShutdownGuard.Core;
using ShutdownGuard.Models;
using ShutdownGuard.Services;
using Xunit;

namespace ShutdownGuard.Tests;

public class ReminderSessionControllerTests
{
    private static readonly TimeSpan LocalOffset = DateTimeOffset.Now.Offset;

    private static DateTimeOffset D(int year, int month, int day, int hour, int minute, int second = 0)
        => new(year, month, day, hour, minute, second, LocalOffset);

    private static ReminderSessionController Create(
        FakeClock clock,
        MemoryDailyCancellationStore? store = null)
        => new(clock, store ?? new MemoryDailyCancellationStore());

    [Fact]
    public async Task BeginSession_BecomesActive_Once()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 18, 0) };
        var session = Create(clock);

        session.BeginSession(D(2026, 8, 6, 18, 0));
        Assert.Equal(ReminderSessionPhase.Active, session.Current.Phase);

        session.BeginSession(D(2026, 8, 6, 18, 0)); // duplicate
        await clock.AdvanceToAsync(D(2026, 8, 6, 18, 0, 1));

        Assert.Equal(ReminderSessionPhase.Active, session.Current.Phase);
        Assert.Equal(D(2026, 8, 6, 22, 0), session.Current.FixedShutdownAt);
        Assert.NotNull(session.Current.RemainingUntilShutdown);
        Assert.Equal(0, session.ShutdownDueCount);

        await session.StopAsync();
    }

    [Fact]
    public async Task StartupInsideWindow_RestoresActiveSession()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 30) };
        var session = Create(clock);

        session.BeginSession(D(2026, 8, 6, 18, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 20, 30, 1));

        Assert.Equal(ReminderSessionPhase.Active, session.Current.Phase);
        Assert.True(session.Current.RemainingUntilShutdown < TimeSpan.FromHours(2));

        await session.StopAsync();
    }

    [Fact]
    public async Task DuplicateBeginSession_SameDay_DoesNotCreateSecondSession()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 19, 0) };
        var session = Create(clock);
        var activeCount = 0;
        session.StateChanged += s =>
        {
            if (s.Phase == ReminderSessionPhase.Active) activeCount++;
        };

        session.BeginSession(D(2026, 8, 6, 18, 0));
        session.BeginSession(D(2026, 8, 6, 18, 0));

        Assert.Equal(1, activeCount);
        Assert.Equal(ReminderSessionPhase.Active, session.Current.Phase);
        await session.StopAsync();
    }

    [Fact]
    public async Task CancelToday_BecomesCancelled_AndPersists()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 0) };
        var store = new MemoryDailyCancellationStore();
        var session = Create(clock, store);

        session.BeginSession(D(2026, 8, 6, 18, 0));
        Assert.True(session.CancelToday());

        Assert.Equal(ReminderSessionPhase.Cancelled, session.Current.Phase);
        Assert.Equal(new DateOnly(2026, 8, 6), store.CancelledDate);
        await session.StopAsync();
    }

    [Fact]
    public async Task CancelToday_SaveFailure_StaysActive()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 0) };
        var store = new MemoryDailyCancellationStore { ThrowOnSave = true };
        var session = Create(clock, store);

        session.BeginSession(D(2026, 8, 6, 18, 0));
        Assert.False(session.CancelToday());

        Assert.Equal(ReminderSessionPhase.Active, session.Current.Phase);
        Assert.Null(store.CancelledDate);
        await session.StopAsync();
    }

    [Fact]
    public async Task Cancel_ThenSettingsConfigSave_DoesNotReviveActive()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ShutdownGuard_Sep_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var stateStore = new FileDailyCancellationStore(dir);
            var configStore = new ConfigStore(dir);
            var clock = new FakeClock { Now = D(2026, 8, 6, 20, 0) };
            var session = new ReminderSessionController(clock, stateStore);

            session.BeginSession(D(2026, 8, 6, 18, 0));
            Assert.True(session.CancelToday());
            await session.StopAsync();

            // Simulate Settings Save changing ReminderStartTime only.
            configStore.Save(new AppConfig
            {
                RunAtStartup = false,
                Shutdown = new ShutdownPlan
                {
                    Enabled = true,
                    ReminderStartTime = new TimeOnly(19, 0),
                    DryRun = true
                }
            });

            var clock2 = new FakeClock { Now = D(2026, 8, 6, 20, 30) };
            var session2 = new ReminderSessionController(clock2, stateStore);
            session2.BeginSession(D(2026, 8, 6, 18, 0));

            Assert.Equal(ReminderSessionPhase.Cancelled, session2.Current.Phase);
            Assert.Equal(new DateOnly(2026, 8, 6), stateStore.LoadCancelledDate());

            var configJson = File.ReadAllText(Path.Combine(dir, "config.json"));
            Assert.DoesNotContain("cancelledShutdownDate", configJson, StringComparison.OrdinalIgnoreCase);

            await session2.StopAsync();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task CancelToday_SurvivesRestart_DoesNotReviveActive()
    {
        var store = new MemoryDailyCancellationStore();

        var clock1 = new FakeClock { Now = D(2026, 8, 6, 19, 0) };
        var session1 = Create(clock1, store);
        session1.BeginSession(D(2026, 8, 6, 18, 0));
        Assert.True(session1.CancelToday());
        await session1.StopAsync();

        var clock2 = new FakeClock { Now = D(2026, 8, 6, 20, 0) };
        var session2 = Create(clock2, store);
        session2.BeginSession(D(2026, 8, 6, 18, 0));

        Assert.Equal(ReminderSessionPhase.Cancelled, session2.Current.Phase);
        Assert.NotEqual(ReminderSessionPhase.Active, session2.Current.Phase);
        await session2.StopAsync();
    }

    [Fact]
    public async Task NextDay_CancellationExpires_AllowsActive()
    {
        var store = new MemoryDailyCancellationStore();
        store.SaveCancelledDate(new DateOnly(2026, 8, 6));

        var clock = new FakeClock { Now = D(2026, 8, 7, 18, 0) };
        var session = Create(clock, store);
        session.BeginSession(D(2026, 8, 7, 18, 0));

        Assert.Equal(ReminderSessionPhase.Active, session.Current.Phase);
        await session.StopAsync();
    }

    [Fact]
    public async Task Dismiss_EndsSession_WithoutWritingCancellation()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 19, 0) };
        var store = new MemoryDailyCancellationStore();
        var session = Create(clock, store);

        session.BeginSession(D(2026, 8, 6, 18, 0));
        session.Dismiss();

        Assert.Equal(ReminderSessionPhase.Idle, session.Current.Phase);
        Assert.Null(store.CancelledDate);

        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0));
        Assert.Equal(0, session.ShutdownDueCount);
        await session.StopAsync();
    }

    [Fact]
    public async Task Cancelled_NeverRaisesShutdownDue()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 20, 0) };
        var session = Create(clock);
        var dueCount = 0;
        session.ShutdownDue += _ => dueCount++;

        session.BeginSession(D(2026, 8, 6, 18, 0));
        Assert.True(session.CancelToday());
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 1));
        await clock.AdvanceToAsync(D(2026, 8, 6, 23, 0));
        await session.StopAsync();

        Assert.Equal(0, dueCount);
        Assert.Equal(0, session.ShutdownDueCount);
        Assert.Equal(ReminderSessionPhase.Cancelled, session.Current.Phase);
    }

    [Fact]
    public async Task Active_RaisesShutdownDue_ExactlyOnce_At2200()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 21, 59) };
        var session = Create(clock);
        var dueCount = 0;
        DateTimeOffset? dueAt = null;
        session.ShutdownDue += t => { dueCount++; dueAt = t; };

        session.BeginSession(D(2026, 8, 6, 18, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 21, 59, 1));
        Assert.Equal(0, dueCount);

        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0, 1));
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 1));
        await clock.AdvanceToAsync(D(2026, 8, 6, 23, 0));
        await session.StopAsync();

        Assert.Equal(1, dueCount);
        Assert.Equal(1, session.ShutdownDueCount);
        Assert.Equal(D(2026, 8, 6, 22, 0), dueAt);
        Assert.Equal(ReminderSessionPhase.Due, session.Current.Phase);
    }

    [Fact]
    public async Task DryRunTrueOrFalse_SameDueBehavior_NoExecutorPath()
    {
        foreach (var dry in new[] { true, false })
        {
            var clock = new FakeClock { Now = D(2026, 8, 6, 21, 59) };
            var session = Create(clock);
            var due = 0;
            session.ShutdownDue += _ => due++;

            session.BeginSession(D(2026, 8, 6, 18, 0), dryRunDisplay: dry);
            await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0));
            await session.StopAsync();

            Assert.Equal(1, due);
            Assert.Equal(ReminderSessionPhase.Due, session.Current.Phase);
            Assert.Equal(dry, session.Current.DryRun);
        }
    }

    [Fact]
    public void Controller_HasNoExecutorDependency_ConstructsWithoutExecutor()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 10, 0) };
        var session = new ReminderSessionController(clock, new MemoryDailyCancellationStore());
        Assert.Equal(ReminderSessionPhase.Idle, session.Current.Phase);
        // Compile-time guarantee: constructor has no IShutdownExecutor parameter.
    }

    [Fact]
    public async Task Remaining_ClampsToZero()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 21, 59, 59) };
        var session = Create(clock);
        session.BeginSession(D(2026, 8, 6, 18, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0));
        await session.StopAsync();

        Assert.Equal(ReminderSessionPhase.Due, session.Current.Phase);
        Assert.Equal(TimeSpan.Zero, session.Current.RemainingUntilShutdown);
    }

    [Fact]
    public async Task StateChangedHandlerException_DoesNotKillController()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 21, 59) };
        var session = Create(clock);
        session.StateChanged += _ => throw new InvalidOperationException("boom");
        var due = 0;
        session.ShutdownDue += _ => due++;

        session.BeginSession(D(2026, 8, 6, 18, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0));
        await session.StopAsync();

        Assert.Equal(1, due);
        Assert.Equal(ReminderSessionPhase.Due, session.Current.Phase);
    }

    [Fact]
    public async Task ShutdownDueHandlerException_DoesNotRepeatDue()
    {
        var clock = new FakeClock { Now = D(2026, 8, 6, 21, 59) };
        var session = Create(clock);
        var calls = 0;
        session.ShutdownDue += _ =>
        {
            calls++;
            throw new InvalidOperationException("handler boom");
        };

        session.BeginSession(D(2026, 8, 6, 18, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 0));
        await clock.AdvanceToAsync(D(2026, 8, 6, 22, 1));
        await session.StopAsync();

        Assert.Equal(1, calls);
        Assert.Equal(1, session.ShutdownDueCount);
    }

    [Fact]
    public void ReminderViewModel_Active_ShowsCancelAndRemaining()
    {
        var snap = new ReminderSessionSnapshot(
            ReminderSessionPhase.Active,
            D(2026, 8, 6, 18, 0),
            D(2026, 8, 6, 22, 0),
            TimeSpan.FromHours(2),
            dryRun: true);

        var vm = new ViewModels.ReminderViewModel();
        vm.Apply(snap);

        Assert.True(vm.CanCancel);
        Assert.True(vm.IsVisiblePhase);
        Assert.Contains("2 小时", vm.RemainingText);
    }

    [Fact]
    public void ReminderViewModel_Cancelled_DisablesCancel()
    {
        var snap = new ReminderSessionSnapshot(
            ReminderSessionPhase.Cancelled,
            D(2026, 8, 6, 18, 0),
            D(2026, 8, 6, 22, 0),
            TimeSpan.FromHours(1),
            dryRun: true);

        var vm = new ViewModels.ReminderViewModel();
        vm.Apply(snap);

        Assert.False(vm.CanCancel);
        Assert.Equal("本次不会关机", vm.RemainingText);
    }

    [Fact]
    public void ReminderViewModel_RemainingZero_ClampsDisplay()
    {
        var snap = new ReminderSessionSnapshot(
            ReminderSessionPhase.Active,
            D(2026, 8, 6, 18, 0),
            D(2026, 8, 6, 22, 0),
            TimeSpan.Zero,
            dryRun: true);

        Assert.Equal("剩余时间：0 秒", ViewModels.ReminderViewModel.FormatRemaining(snap));
    }
}
