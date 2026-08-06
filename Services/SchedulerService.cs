using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Manages daily reminder-window entry. Does not execute shutdown.
/// Final shutdown at 22:00 is M4 responsibility.
/// </summary>
public sealed class SchedulerService : IAsyncDisposable
{
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

    public event Action<DateTimeOffset>? ReminderWindowStarted;
    public event Action<DateTimeOffset?>? NextReminderChanged;

    public DateTimeOffset? NextReminder { get; private set; }
    public bool IsRunning => _loopTask is { IsCompleted: false };

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
            _armedOccurrence = ComputeArmedOccurrence();
            SyncNextReminder();
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            _armedOccurrence = ComputeArmedOccurrence();
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
    /// - Before ReminderStart today → today's ReminderStart
    /// - Inside [ReminderStart, 22:00) and not yet fired → today's ReminderStart (enter now)
    /// - At/after 22:00 → tomorrow's ReminderStart (no catch-up)
    /// </summary>
    private DateTimeOffset? ComputeArmedOccurrence()
    {
        if (!_plan.Enabled)
            return null;

        var todayReminder = _policy.GetTodayReminder(_plan, _clock.Now);
        if (todayReminder is null)
            return null;

        var now = _clock.Now;
        var todayShutdownDto = new DateTimeOffset(
            todayReminder.Value.Year, todayReminder.Value.Month, todayReminder.Value.Day,
            ShutdownPolicy.FixedShutdownTime.Hour, ShutdownPolicy.FixedShutdownTime.Minute, 0,
            todayReminder.Value.Offset);

        if (now < todayReminder.Value)
            return todayReminder;

        if (now < todayShutdownDto)
        {
            // Inside reminder window — arm today's start so the loop can enter once.
            if (_lastReminderOccurrence == todayReminder.Value)
                return todayReminder.Value.AddDays(1);

            return todayReminder;
        }

        // Past fixed shutdown — wait for tomorrow (never catch up shutdown or reminder).
        return todayReminder.Value.AddDays(1);
    }

    private void SyncNextReminder()
    {
        NextReminder = _armedOccurrence;
        NextReminderChanged?.Invoke(NextReminder);
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

            lock (_lock)
            {
                enabled = _plan.Enabled;
                armed = _armedOccurrence;
            }

            if (!enabled)
                continue;

            if (armed is null)
            {
                lock (_lock)
                {
                    if (_plan.Enabled && _armedOccurrence is null)
                    {
                        _armedOccurrence = ComputeArmedOccurrence();
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
                ShutdownPolicy.FixedShutdownTime.Hour,
                ShutdownPolicy.FixedShutdownTime.Minute,
                0,
                armed.Value.Offset);

            if (now >= dayShutdown)
            {
                // Slept past the entire reminder window — skip today, no reminder, no shutdown.
                AppLogger.Info(
                    $"Reminder window missed (past {ShutdownPolicy.FixedShutdownTime:HH:mm}): " +
                    $"{armed.Value:yyyy-MM-dd HH:mm:ss zzz}");

                _lastReminderOccurrence = armed.Value;

                lock (_lock)
                {
                    if (_armedOccurrence == armed.Value)
                    {
                        _armedOccurrence = armed.Value.AddDays(1);
                        SyncNextReminder();
                    }
                }
                continue;
            }

            // Enter reminder window exactly once for this occurrence.
            var coldStartEntry = now > armed.Value;
            if (coldStartEntry)
            {
                AppLogger.Info(
                    $"Reminder window entered on startup/resume: " +
                    $"shutdown scheduled for {dayShutdown:yyyy-MM-dd HH:mm}");
            }
            else
            {
                AppLogger.Info($"Reminder window started: {armed.Value:yyyy-MM-dd HH:mm}");
            }

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
                    _armedOccurrence = armed.Value.AddDays(1);
                    SyncNextReminder();
                }
            }
        }
    }
}
