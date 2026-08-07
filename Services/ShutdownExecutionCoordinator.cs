using ShutdownGuard.Core;
using ShutdownGuard.Models;

namespace ShutdownGuard.Services;

/// <summary>
/// Consumes ShutdownDue and safely selects DryRun / Real executor.
/// Scheduler and ReminderSessionController never call executors.
/// </summary>
public sealed class ShutdownExecutionCoordinator : IAsyncDisposable
{
    private readonly IClock _clock;
    private readonly IAppConfigProvider _configProvider;
    private readonly IDailyCancellationStore _cancellationStore;
    private readonly IShutdownExecutor _dryRunExecutor;
    private readonly IShutdownExecutor _realExecutor;
    private readonly object _lock = new();

    private readonly HashSet<DateOnly> _handledOccurrences = new();
    private ShutdownExecutionSnapshot _current = ShutdownExecutionSnapshot.Idle;
    private bool _disposed;

    public event Action<ShutdownExecutionSnapshot>? StateChanged;

    public ShutdownExecutionSnapshot Current
    {
        get { lock (_lock) return _current; }
    }

    public ShutdownExecutionCoordinator(
        IClock clock,
        IAppConfigProvider configProvider,
        IDailyCancellationStore cancellationStore,
        IShutdownExecutor dryRunExecutor,
        IShutdownExecutor realExecutor)
    {
        _clock = clock;
        _configProvider = configProvider;
        _cancellationStore = cancellationStore;
        _dryRunExecutor = dryRunExecutor;
        _realExecutor = realExecutor;
    }

    public Task HandleShutdownDueAsync(ShutdownDueInfo due, CancellationToken cancellationToken = default)
    {
        return HandleCoreAsync(due, cancellationToken);
    }

    /// <summary>Synchronous entry used by event wiring; exceptions are swallowed after logging.</summary>
    public void HandleShutdownDue(ShutdownDueInfo due)
    {
        _ = HandleCoreAsync(due, CancellationToken.None);
    }

    private async Task HandleCoreAsync(ShutdownDueInfo due, CancellationToken cancellationToken)
    {
        if (_disposed)
            return;

        AppLogger.Info(
            $"Shutdown due received: {due.OccurrenceDate:yyyy-MM-dd} at {due.ShutdownAt:yyyy-MM-dd HH:mm}");

        lock (_lock)
        {
            if (_handledOccurrences.Contains(due.OccurrenceDate))
            {
                AppLogger.Info(
                    $"Shutdown due ignored (already handled): {due.OccurrenceDate:yyyy-MM-dd}");
                return;
            }

            // Reserve occurrence before awaiting — defense-in-depth exactly-once.
            _handledOccurrences.Add(due.OccurrenceDate);
        }

        try
        {
            await ExecuteOnceAsync(due, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Outer safety net — ExecuteOnceAsync already handles executor failures.
            AppLogger.Error($"Shutdown execution coordinator failed: {ex.Message}");
            Publish(new ShutdownExecutionSnapshot(
                ShutdownExecutionPhase.Failed,
                due.OccurrenceDate,
                "关机执行失败",
                ex.Message,
                realShutdownWouldBeBlocked: false));
        }
    }

    private async Task ExecuteOnceAsync(ShutdownDueInfo due, CancellationToken cancellationToken)
    {
        // Gate 1: occurrence must be today + ShutdownAt matches fixed 22:00.
        var today = DateOnly.FromDateTime(_clock.Now.DateTime);
        if (due.OccurrenceDate != today)
        {
            Block(due, "Shutdown execution blocked: occurrence date mismatch",
                "自动关机已被安全阻止：关机日期异常。");
            return;
        }

        var expectedShutdown = new DateTimeOffset(
            due.OccurrenceDate.Year, due.OccurrenceDate.Month, due.OccurrenceDate.Day,
            ShutdownPolicy.EffectiveFixedShutdownTime.Hour,
            ShutdownPolicy.EffectiveFixedShutdownTime.Minute,
            0,
            due.ShutdownAt.Offset);

        if (due.ShutdownAt != expectedShutdown)
        {
            Block(due, "Shutdown execution blocked: shutdown time mismatch",
                "自动关机已被安全阻止：关机时间异常。");
            return;
        }

        // Gate 2 + config availability
        if (!_configProvider.TryGetSnapshot(out var config, out var configError))
        {
            AppLogger.Error($"Shutdown execution blocked: config unavailable ({configError})");
            Block(due, "Shutdown execution blocked: config unavailable",
                "自动关机已被安全阻止：无法读取当前配置。",
                configError);
            return;
        }

        if (!config.Enabled)
        {
            Block(due, "Shutdown execution blocked: plan disabled",
                "自动关机已被安全阻止：计划已停用。");
            return;
        }

        // Gate 3: cancellation re-check
        var cancel = _cancellationStore.ReadCancellationState(due.OccurrenceDate);
        if (cancel.Status == DailyCancellationStatus.Cancelled)
        {
            Block(due, "Shutdown execution blocked: occurrence cancelled",
                "自动关机已被安全阻止：今日已取消。");
            return;
        }

        var cancellationUnavailable = cancel.Status == DailyCancellationStatus.Unavailable;
        if (cancellationUnavailable && !config.DryRun)
        {
            AppLogger.Error(
                $"Shutdown execution blocked: cancellation state unavailable ({cancel.Error})");
            Block(due, "Shutdown execution blocked: cancellation state unavailable",
                "自动关机已被安全阻止：无法确认今日取消状态。",
                cancel.Error);
            return;
        }

        if (cancellationUnavailable && config.DryRun)
        {
            AppLogger.Info(
                "Cancellation state unavailable; real shutdown would be blocked");
        }

        Publish(new ShutdownExecutionSnapshot(
            ShutdownExecutionPhase.Executing,
            due.OccurrenceDate,
            "正在执行关机流程…",
            error: null,
            realShutdownWouldBeBlocked: cancellationUnavailable));

        var executor = config.DryRun ? _dryRunExecutor : _realExecutor;

        try
        {
            await executor.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Shutdown execution failed: {ex.Message}");
            Publish(new ShutdownExecutionSnapshot(
                ShutdownExecutionPhase.Failed,
                due.OccurrenceDate,
                "关机执行失败",
                ex.Message,
                realShutdownWouldBeBlocked: cancellationUnavailable));
            return;
        }

        if (config.DryRun)
        {
            var message = cancellationUnavailable
                ? "安全测试完成：已模拟关机（真实关机本会被阻止）"
                : "安全测试完成：22:00 已模拟关机";

            Publish(new ShutdownExecutionSnapshot(
                ShutdownExecutionPhase.DryRunCompleted,
                due.OccurrenceDate,
                message,
                error: null,
                realShutdownWouldBeBlocked: cancellationUnavailable));
        }
        else
        {
            Publish(new ShutdownExecutionSnapshot(
                ShutdownExecutionPhase.RealShutdownRequested,
                due.OccurrenceDate,
                "已请求系统关机",
                error: null,
                realShutdownWouldBeBlocked: false));
        }
    }

    private void Block(ShutdownDueInfo due, string logMessage, string userMessage, string? error = null)
    {
        AppLogger.Info(logMessage);
        Publish(new ShutdownExecutionSnapshot(
            ShutdownExecutionPhase.Blocked,
            due.OccurrenceDate,
            userMessage,
            error,
            realShutdownWouldBeBlocked: true));
    }

    private void Publish(ShutdownExecutionSnapshot snapshot)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _current = snapshot;
        }

        try
        {
            StateChanged?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            AppLogger.Error($"ShutdownExecution StateChanged handler failed: {ex.Message}");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_lock)
        {
            _disposed = true;
        }
        return ValueTask.CompletedTask;
    }
}
