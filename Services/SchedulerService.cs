using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

public sealed class SchedulerService : IAsyncDisposable
{
    private readonly DailyPolicy _policy = new();
    private readonly IClock _clock;
    private readonly object _lock = new();

    private IShutdownExecutor _executor;
    private ShutdownPlan _plan = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    /// <summary>
    /// The occurrence that the scheduler is currently committed to waiting for and executing.
    /// This is the core semantic anchor:
    /// - Set at Start() time based on plan + current time
    /// - Checked each tick; when now >= _armedOccurrence, execute exactly once
    /// - Advanced to the next day after execution
    /// - Set to null when disabled
    ///
    /// Cold start safety: if the app starts after today's target time,
    /// _armedOccurrence is set to tomorrow — no catch-up execution.
    /// </summary>
    private DateTimeOffset? _armedOccurrence;

    /// <summary>
    /// Tracks the last occurrence that was actually executed,
    /// purely as a safety net against duplicate execution.
    /// </summary>
    private DateTimeOffset? _lastExecutedOccurrence;

    public event Action<DateTimeOffset?>? NextShutdownChanged;

    public DateTimeOffset? NextShutdown { get; private set; }
    public bool IsRunning => _loopTask is { IsCompleted: false };

    public SchedulerService(IClock clock, IShutdownExecutor executor)
    {
        _clock = clock;
        _executor = executor;
    }

    // ── Plan update ──────────────────────────────────────────────

    /// <summary>
    /// Updates the plan at runtime. Re-arms the occurrence based on the new plan:
    /// - If disabled → clears armed occurrence.
    /// - If today's target is in the future → arms today's target.
    /// - If today's target has passed → arms tomorrow's target (no catch-up).
    /// This is always a user-initiated change, never a missed-occurrence catch-up.
    /// </summary>
    public void UpdatePlan(ShutdownPlan plan)
    {
        lock (_lock)
        {
            // Defensive copy: don't hold a reference to an external mutable object.
            // This prevents callers from mutating the plan and bypassing UpdatePlan().
            _plan = new ShutdownPlan
            {
                Enabled = plan.Enabled,
                ShutdownTime = plan.ShutdownTime,
                DryRun = plan.DryRun
            };
            _armedOccurrence = ComputeArmedOccurrence();
            SyncNextShutdown();
        }
    }

    /// <summary>
    /// Replaces the executor at runtime (e.g. DryRun on/off switch).
    /// Thread-safe; does not start a second scheduler.
    /// </summary>
    public void UpdateExecutor(IShutdownExecutor executor)
    {
        lock (_lock)
        {
            _executor = executor;
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning) return;

            // Arm the occurrence based on current plan + time.
            // Cold start safety: if now > today's target, arms tomorrow.
            _armedOccurrence = ComputeArmedOccurrence();
            SyncNextShutdown();

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
    /// Computes the occurrence to arm, based on the current plan and time.
    /// Rule:
    ///   If disabled → null.
    ///   If today's target is in the future → today's target.
    ///   If today's target has passed → tomorrow's target.
    /// This is the single place that decides what the scheduler commits to.
    /// </summary>
    private DateTimeOffset? ComputeArmedOccurrence()
    {
        if (!_plan.Enabled)
            return null;

        var todayTarget = _policy.GetTodayTarget(_plan, _clock.Now);
        if (todayTarget is null)
            return null;

        if (todayTarget.Value >= _clock.Now)
        {
            // Today's target is still in the future — arm it.
            return todayTarget;
        }
        else
        {
            // Today's target has passed — arm tomorrow's.
            return todayTarget.Value.AddDays(1);
        }
    }

    /// <summary>
    /// Syncs NextShutdown to _armedOccurrence for UI display.
    /// </summary>
    private void SyncNextShutdown()
    {
        NextShutdown = _armedOccurrence;
        NextShutdownChanged?.Invoke(NextShutdown);
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
            IShutdownExecutor executor;
            bool enabled;

            lock (_lock)
            {
                enabled = _plan.Enabled;
                armed = _armedOccurrence;
                executor = _executor;
            }

            if (!enabled)
                continue;

            if (armed is null)
            {
                // Not armed — try to arm the next future occurrence.
                lock (_lock)
                {
                    // Re-check under lock in case state changed
                    if (_plan.Enabled && _armedOccurrence is null)
                    {
                        _armedOccurrence = ComputeArmedOccurrence();
                        SyncNextShutdown();
                    }
                }
                continue;
            }

            var now = _clock.Now;

            // Not yet time for the armed occurrence
            if (now < armed.Value)
                continue;

            // Safety net: same occurrence already executed
            if (_lastExecutedOccurrence == armed.Value)
                continue;

            // ── Execute ──
            _lastExecutedOccurrence = armed.Value;
            await executor.ExecuteAsync(cancellationToken);

            // Advance to the next day's occurrence
            lock (_lock)
            {
                if (_armedOccurrence == armed.Value)
                {
                    _armedOccurrence = armed.Value.AddDays(1);
                    SyncNextShutdown();
                }
                // If _armedOccurrence changed during execution (e.g. UpdatePlan),
                // respect the new value — don't overwrite.
            }
        }
    }
}
