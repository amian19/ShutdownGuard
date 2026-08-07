using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Manages daily reminder-window entry. Does not execute shutdown.
/// Final shutdown at 22:00 is M4 responsibility.
/// </summary>
public sealed class SchedulerService : IAsyncDisposable
{
    /// <summary>
    /// If the loop wakes within this skew of the armed ReminderStart,
    /// treat entry as Scheduled rather than TimeAdvance.
    /// Covers the 1-second poll cadence without mislabeling normal ticks.
    /// </summary>
    private static readonly TimeSpan ScheduledSkew = TimeSpan.FromSeconds(2);

    private readonly DailyPolicy _policy = new();
    private readonly IClock _clock;
    private readonly object _lock = new();

    private ShutdownPlan _plan = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>
    /// The reminder-start occurrence the scheduler is committed to.
    /// When now reaches this instant (and the day's fixed shutdown has not passed),
    /// ReminderWindowStarted fires exactly once, then advances to tomorrow.
    /// </summary>
    private DateTimeOffset? _armedOccurrence;

    /// <summary>
    /// Last reminder occurrence that already entered the window (dedupe).
    /// </summary>
    private DateTimeOffset? _lastReminderOccurrence;

    /// <summary>
    /// Why the currently armed past-or-future occurrence is expected to enter.
    /// Set when arming; refined at fire time for Scheduled → TimeAdvance.
    /// </summary>
    private ReminderWindowEntryReason _pendingEntryReason = ReminderWindowEntryReason.Scheduled;

    public event Action<DateTimeOffset>? ReminderWindowStarted;
    public event Action<DateTimeOffset?>? NextReminderChanged;

    public DateTimeOffset? NextReminder { get; private set; }
    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>Last successful reminder-window entry reason (for tests / diagnostics).</summary>
    internal ReminderWindowEntryReason? LastEntryReason { get; private set; }

    public SchedulerService(IClock clock)
    {
        _clock = clock;
    }

    // ── Plan update ──────────────────────────────────────────────

    /// <summary>
    /// Updates the plan at runtime and re-arms the next reminder occurrence.
    /// If currently inside today's reminder window and today's reminder has not
    /// yet fired, arms today's (past) reminder so the loop enters immediately.
    /// </summary>
    public void UpdatePlan(ShutdownPlan plan)
    {
        lock (_lock)
        {
            _plan = new ShutdownPlan
            {
                Enabled = plan.Enabled,
                ReminderStartTime = plan.ReminderStartTime,
                DryRun = plan.DryRun
            };
            _armedOccurrence = ComputeArmedOccurrence(ReminderWindowEntryReason.PlanUpdate);
            SyncNextReminder();
        }
    }

    /// <summary>
    /// Marks today's reminder as already handled (fired, cancelled, or disabled-for-today)
    /// so re-arming / changing ReminderStartTime will not catch up again today.
    /// NextReminder advances to tomorrow's ReminderStart.
    /// </summary>
    public void MarkTodayReminderHandled()
    {
        lock (_lock)
        {
            var todayReminder = _policy.GetTodayReminder(_plan, _clock.Now);
            if (todayReminder is null)
                return;

            _lastReminderOccurrence = todayReminder.Value;
            _pendingEntryReason = ReminderWindowEntryReason.Scheduled;
            _armedOccurrence = todayReminder.Value.AddDays(1);
            SyncNextReminder();
            AppLogger.Info(
                $"Today's reminder marked handled — next: {_armedOccurrence:yyyy-MM-dd HH:mm}");
        }
    }

    /// <summary>
    /// Clears today's "already handled" mark so the scheduler may re-arm today
    /// (e.g. user restores "今日关机" after cancelling early).
    /// No-op if nothing was marked for today.
    /// </summary>
    public void ClearTodayReminderHandled()
    {
        lock (_lock)
        {
            var todayReminder = _policy.GetTodayReminder(_plan, _clock.Now);
            if (todayReminder is null)
                return;

            if (_lastReminderOccurrence is not { } last
                || last.Year != todayReminder.Value.Year
                || last.Month != todayReminder.Value.Month
                || last.Day != todayReminder.Value.Day)
            {
                return;
            }

            _lastReminderOccurrence = null;
            _armedOccurrence = ComputeArmedOccurrence(ReminderWindowEntryReason.PlanUpdate);
            SyncNextReminder();
            AppLogger.Info(
                $"Today's reminder mark cleared — next: {_armedOccurrence:yyyy-MM-dd HH:mm}");
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            _armedOccurrence = ComputeArmedOccurrence(ReminderWindowEntryReason.Startup);
            SyncNextReminder();

            _cts = new CancellationTokenSource();
            _loopTask = RunLoopAsync(_cts.Token);
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_lock)
        {
            cts = _cts;
            _cts = null;
            loop = _loopTask;
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (loop is not null)
        {
            try { await loop; } catch (OperationCanceledException) { }
            lock (_lock) { _loopTask = null; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    // ── Private: occurrence computation ──────────────────────────

    /// <summary>
    /// Arms the next reminder-start occurrence:
    /// - Disabled → null
    /// - Today's reminder already entered/skipped (any ReminderStart that day) → tomorrow
    /// - Before ReminderStart today → today's ReminderStart (Scheduled)
    /// - Inside [ReminderStart, 22:00) and not yet fired → today's ReminderStart (context reason)
    /// - At/after 22:00 → tomorrow's ReminderStart (no catch-up)
    /// </summary>
    private DateTimeOffset? ComputeArmedOccurrence(ReminderWindowEntryReason insideWindowReason)
    {
        if (!_plan.Enabled)
            return null;

        var todayReminder = _policy.GetTodayReminder(_plan, _clock.Now);
        if (todayReminder is null)
            return null;

        // Same calendar day already handled — never re-arm today when ReminderStartTime changes.
        if (HasHandledReminderOnDay(todayReminder.Value))
        {
            _pendingEntryReason = ReminderWindowEntryReason.Scheduled;
            return todayReminder.Value.AddDays(1);
        }

        var now = _clock.Now;
        var todayShutdownDto = new DateTimeOffset(
            todayReminder.Value.Year, todayReminder.Value.Month, todayReminder.Value.Day,
            ShutdownPolicy.EffectiveFixedShutdownTime.Hour, ShutdownPolicy.EffectiveFixedShutdownTime.Minute, 0,
            todayReminder.Value.Offset);

        if (now < todayReminder.Value)
        {
            _pendingEntryReason = ReminderWindowEntryReason.Scheduled;
            return todayReminder;
        }

        if (now < todayShutdownDto)
        {
            // Inside reminder window — arm today's start so the loop can enter once.
            _pendingEntryReason = insideWindowReason;
            return todayReminder;
        }

        // Past fixed shutdown — wait for tomorrow (never catch up shutdown or reminder).
        _pendingEntryReason = ReminderWindowEntryReason.Scheduled;
        return todayReminder.Value.AddDays(1);
    }

    /// <summary>
    /// True when any reminder occurrence on the same local calendar day was already entered/skipped.
    /// Compares by date, not exact ReminderStartTime, so Save with a new time cannot revive today.
    /// </summary>
    private bool HasHandledReminderOnDay(DateTimeOffset dayReminder)
    {
        if (_lastReminderOccurrence is not { } last)
            return false;

        return last.Year == dayReminder.Year
            && last.Month == dayReminder.Month
            && last.Day == dayReminder.Day;
    }

    private void SyncNextReminder()
    {
        NextReminder = _armedOccurrence;
        NextReminderChanged?.Invoke(NextReminder);
    }

    private static string FormatEntryLog(
        ReminderWindowEntryReason reason,
        DateTimeOffset armed,
        DateTimeOffset dayShutdown)
    {
        var shutdownText = $"{dayShutdown:yyyy-MM-dd HH:mm}";
        return reason switch
        {
            ReminderWindowEntryReason.Scheduled =>
                $"Reminder window started: {armed:yyyy-MM-dd HH:mm}, shutdown scheduled for {ShutdownPolicy.EffectiveFixedShutdownTime:HH:mm}",

            ReminderWindowEntryReason.Startup =>
                $"Reminder window entered on startup: shutdown scheduled for {shutdownText}",

            ReminderWindowEntryReason.PlanUpdate =>
                $"Reminder window entered after plan update: shutdown scheduled for {shutdownText}",

            ReminderWindowEntryReason.TimeAdvance =>
                $"Reminder window entered after time advance: shutdown scheduled for {shutdownText}",

            _ =>
                $"Reminder window entered: shutdown scheduled for {shutdownText}"
        };
    }

    // ── Private: main loop ───────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _clock.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            DateTimeOffset? armed;
            bool enabled;
            ReminderWindowEntryReason pendingReason;

            lock (_lock)
            {
                enabled = _plan.Enabled;
                armed = _armedOccurrence;
                pendingReason = _pendingEntryReason;
            }

            if (!enabled)
                continue;

            if (armed is null)
            {
                lock (_lock)
                {
                    if (_plan.Enabled && _armedOccurrence is null)
                    {
                        _armedOccurrence = ComputeArmedOccurrence(ReminderWindowEntryReason.Startup);
                        SyncNextReminder();
                    }
                }
                continue;
            }

            var now = _clock.Now;

            if (now < armed.Value)
                continue;

            if (_lastReminderOccurrence == armed.Value)
                continue;

            // Window end for the armed day's plan.
            var dayShutdown = new DateTimeOffset(
                armed.Value.Year, armed.Value.Month, armed.Value.Day,
                ShutdownPolicy.EffectiveFixedShutdownTime.Hour,
                ShutdownPolicy.EffectiveFixedShutdownTime.Minute,
                0,
                armed.Value.Offset);

            if (now >= dayShutdown)
            {
                // Slept past the entire reminder window — skip today, no reminder, no shutdown.
                AppLogger.Info(
                    $"Reminder window missed (past {ShutdownPolicy.EffectiveFixedShutdownTime:HH:mm}): " +
                    $"{armed.Value:yyyy-MM-dd HH:mm:ss zzz}");

                _lastReminderOccurrence = armed.Value;

                lock (_lock)
                {
                    if (_armedOccurrence == armed.Value)
                    {
                        _pendingEntryReason = ReminderWindowEntryReason.Scheduled;
                        _armedOccurrence = armed.Value.AddDays(1);
                        SyncNextReminder();
                    }
                }
                continue;
            }

            // Resolve entry reason without claiming "resume" (no PowerModeChanged signal).
            var reason = pendingReason;
            if (reason == ReminderWindowEntryReason.Scheduled
                && now > armed.Value + ScheduledSkew)
            {
                reason = ReminderWindowEntryReason.TimeAdvance;
            }

            AppLogger.Info(FormatEntryLog(reason, armed.Value, dayShutdown));
            LastEntryReason = reason;
            _lastReminderOccurrence = armed.Value;

            try
            {
                ReminderWindowStarted?.Invoke(armed.Value);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"ReminderWindowStarted handler failed: {ex.Message}");
            }

            lock (_lock)
            {
                if (_armedOccurrence == armed.Value)
                {
                    _pendingEntryReason = ReminderWindowEntryReason.Scheduled;
                    _armedOccurrence = armed.Value.AddDays(1);
                    SyncNextReminder();
                }
            }
        }
    }
}
