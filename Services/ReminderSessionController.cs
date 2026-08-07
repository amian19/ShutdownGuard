using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Owns today's reminder-round state for M4: Active / Cancelled / Due.
/// Does not execute shutdown — raises <see cref="ShutdownDue"/> once for M4.5.
/// </summary>
public sealed class ReminderSessionController : IAsyncDisposable
{
    private readonly IClock _clock;
    private readonly IDailyCancellationStore _cancellationStore;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    private ReminderSessionPhase _phase = ReminderSessionPhase.Idle;
    private DateTimeOffset? _reminderStartedAt;
    private DateTimeOffset? _fixedShutdownAt;
    private bool _dryRunDisplay = true;
    private DateOnly? _sessionDay;
    private bool _shutdownDueRaised;

    public event Action<ReminderSessionSnapshot>? StateChanged;

    /// <summary>
    /// Raised exactly once when an Active session reaches fixed shutdown time.
    /// M4.5 ShutdownExecutionCoordinator consumes this — Session never calls executors.
    /// </summary>
    public event Action<ShutdownDueInfo>? ShutdownDue;

    public ReminderSessionSnapshot Current => BuildSnapshot();

    public bool IsRunning => _loopTask is { IsCompleted: false };

    /// <summary>Test/diagnostics: how many times ShutdownDue was raised for the current process lifetime.</summary>
    internal int ShutdownDueCount { get; private set; }

    public ReminderSessionController(IClock clock, IDailyCancellationStore cancellationStore)
    {
        _clock = clock;
        _cancellationStore = cancellationStore;
    }

    /// <summary>
    /// Begins or restores today's reminder session after Scheduler fires ReminderWindowStarted.
    /// <paramref name="dryRunDisplay"/> is presentation-only and does not change Due behavior.
    /// </summary>
    public void BeginSession(DateTimeOffset reminderOccurrence, bool dryRunDisplay = true)
    {
        ReminderSessionSnapshot? publish = null;

        lock (_lock)
        {
            var day = DateOnly.FromDateTime(reminderOccurrence.DateTime);
            var shutdownAt = new DateTimeOffset(
                reminderOccurrence.Year, reminderOccurrence.Month, reminderOccurrence.Day,
                ShutdownPolicy.EffectiveFixedShutdownTime.Hour,
                ShutdownPolicy.EffectiveFixedShutdownTime.Minute,
                0,
                reminderOccurrence.Offset);

            var now = _clock.Now;

            if (now >= shutdownAt)
            {
                AppLogger.Info(
                    $"Reminder session ignored (already past {ShutdownPolicy.EffectiveFixedShutdownTime:HH:mm}): " +
                    $"{reminderOccurrence:yyyy-MM-dd HH:mm}");
                return;
            }

            if (_sessionDay == day
                && _phase is ReminderSessionPhase.Active
                    or ReminderSessionPhase.Cancelled
                    or ReminderSessionPhase.Due)
            {
                AppLogger.Info("Reminder session already open for today — ignoring duplicate BeginSession");
                return;
            }

            _reminderStartedAt = reminderOccurrence;
            _fixedShutdownAt = shutdownAt;
            _dryRunDisplay = dryRunDisplay;
            _sessionDay = day;
            _shutdownDueRaised = false;

            if (IsCancelledForDay_NoLock(day))
            {
                _phase = ReminderSessionPhase.Cancelled;
                AppLogger.Info(
                    $"Reminder session restored as Cancelled for {day:yyyy-MM-dd} (persisted cancel)");
            }
            else
            {
                // Unavailable cancellation state still allows Active reminder (not dangerous).
                _phase = ReminderSessionPhase.Active;
                AppLogger.Info(
                    $"Reminder session started: {reminderOccurrence:yyyy-MM-dd HH:mm} → " +
                    $"shutdown {shutdownAt:yyyy-MM-dd HH:mm}");
            }

            EnsureLoopRunning_NoLock();
            publish = BuildSnapshot_NoLock();
        }

        Publish(publish);
    }

    /// <summary>
    /// Cancels today's fixed shutdown. Persists first; only then transitions to Cancelled.
    /// Returns false if persistence fails — session stays Active.
    /// </summary>
    public bool CancelToday()
    {
        DateOnly day;

        lock (_lock)
        {
            if (_phase != ReminderSessionPhase.Active || _sessionDay is null)
                return false;

            day = _sessionDay.Value;
        }

        try
        {
            _cancellationStore.SaveCancelledDate(day);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to persist CancelToday — remaining Active: {ex.Message}");
            return false;
        }

        ReminderSessionSnapshot? publish = null;
        lock (_lock)
        {
            if (_phase != ReminderSessionPhase.Active || _sessionDay != day)
                return false;

            _phase = ReminderSessionPhase.Cancelled;
            AppLogger.Info(
                $"Reminder session cancelled for {day:yyyy-MM-dd}; no ShutdownDue today");
            publish = BuildSnapshot_NoLock();
        }

        Publish(publish);
        return true;
    }

    /// <summary>
    /// Ends the in-memory session without writing CancelledDate.
    /// Prefer <see cref="SuppressTodayAndDismiss"/> when the user disables auto-shutdown,
    /// so today's reminder/shutdown stay suppressed even if they re-enable later today.
    /// </summary>
    public void Dismiss()
    {
        ReminderSessionSnapshot? publish = null;

        lock (_lock)
        {
            if (_phase == ReminderSessionPhase.Idle)
                return;

            _phase = ReminderSessionPhase.Idle;
            _reminderStartedAt = null;
            _fixedShutdownAt = null;
            _sessionDay = null;
            _shutdownDueRaised = false;
            AppLogger.Info("Reminder session dismissed (no cancellation persisted)");
            publish = BuildSnapshot_NoLock();
        }

        Publish(publish);
    }

    /// <summary>
    /// Disables today's shutdown the same way CancelToday does for persistence:
    /// writes CancelledShutdownDate for the local calendar day, then ends the session.
    /// Next calendar day the date expires and reminders work again once Enabled=true.
    /// Persistence is best-effort — session is always dismissed.
    /// </summary>
    public void SuppressTodayAndDismiss()
    {
        var day = DateOnly.FromDateTime(_clock.Now.DateTime);
        try
        {
            _cancellationStore.SaveCancelledDate(day);
            AppLogger.Info(
                $"Today suppressed via disable for {day:yyyy-MM-dd}; no reminder/shutdown today");
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                $"Failed to persist suppress-today on disable (still dismissing): {ex.Message}");
        }

        Dismiss();
    }

    /// <summary>Updates DryRun display flag only — never affects Due / ShutdownDue.</summary>
    public void UpdateDryRunDisplay(bool dryRunDisplay)
    {
        ReminderSessionSnapshot? publish = null;
        lock (_lock)
        {
            if (_dryRunDisplay == dryRunDisplay)
                return;
            _dryRunDisplay = dryRunDisplay;
            if (_phase != ReminderSessionPhase.Idle)
                publish = BuildSnapshot_NoLock();
        }
        Publish(publish);
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

    public async ValueTask DisposeAsync() => await StopAsync();

    private bool IsCancelledForDay_NoLock(DateOnly day)
    {
        var result = _cancellationStore.ReadCancellationState(day);
        return result.Status == DailyCancellationStatus.Cancelled;
    }

    private void EnsureLoopRunning_NoLock()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _loopTask = RunLoopAsync(_cts.Token);
    }

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

            ReminderSessionPhase phase;
            DateTimeOffset? shutdownAt;
            bool dueAlreadyRaised;

            lock (_lock)
            {
                phase = _phase;
                shutdownAt = _fixedShutdownAt;
                dueAlreadyRaised = _shutdownDueRaised;
            }

            if (phase == ReminderSessionPhase.Idle)
                continue;

            var now = _clock.Now;

            // Keep remaining time fresh while before 22:00.
            if (shutdownAt is not null
                && now < shutdownAt.Value
                && phase is ReminderSessionPhase.Active or ReminderSessionPhase.Cancelled)
            {
                Publish(BuildSnapshot());
                continue;
            }

            if (shutdownAt is null || now < shutdownAt.Value)
                continue;

            // Past 22:00 — Cancelled never raises ShutdownDue.
            if (phase == ReminderSessionPhase.Cancelled)
                continue;

            if (phase == ReminderSessionPhase.Due || dueAlreadyRaised)
                continue;

            if (phase != ReminderSessionPhase.Active)
                continue;

            DateTimeOffset dueAt;
            DateTimeOffset reminderAt;
            DateOnly occurrenceDay;
            ReminderSessionSnapshot? snap = null;
            Action<ShutdownDueInfo>? dueHandlers;

            lock (_lock)
            {
                if (_phase != ReminderSessionPhase.Active || _shutdownDueRaised
                    || _fixedShutdownAt is null || _reminderStartedAt is null || _sessionDay is null)
                    continue;

                _shutdownDueRaised = true;
                _phase = ReminderSessionPhase.Due;
                dueAt = _fixedShutdownAt.Value;
                reminderAt = _reminderStartedAt.Value;
                occurrenceDay = _sessionDay.Value;
                ShutdownDueCount++;
                snap = BuildSnapshot_NoLock();
                dueHandlers = ShutdownDue;
            }

            var dueInfo = new ShutdownDueInfo(occurrenceDay, dueAt, reminderAt);
            AppLogger.Info($"ShutdownDue: {dueAt:yyyy-MM-dd HH:mm}");
            Publish(snap);

            try
            {
                dueHandlers?.Invoke(dueInfo);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"ShutdownDue handler failed: {ex.Message}");
            }
        }
    }

    private ReminderSessionSnapshot BuildSnapshot()
    {
        lock (_lock) return BuildSnapshot_NoLock();
    }

    private ReminderSessionSnapshot BuildSnapshot_NoLock()
    {
        TimeSpan? remaining = null;
        if (_fixedShutdownAt is not null
            && _phase is ReminderSessionPhase.Active or ReminderSessionPhase.Cancelled)
        {
            var left = _fixedShutdownAt.Value - _clock.Now;
            remaining = left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
        else if (_phase == ReminderSessionPhase.Due)
        {
            remaining = TimeSpan.Zero;
        }

        return new ReminderSessionSnapshot(
            _phase,
            _reminderStartedAt,
            _fixedShutdownAt,
            remaining,
            _dryRunDisplay);
    }

    private void Publish(ReminderSessionSnapshot? snapshot)
    {
        if (snapshot is null) return;
        try
        {
            StateChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ReminderSession StateChanged handler failed: {ex.Message}");
        }
    }
}
