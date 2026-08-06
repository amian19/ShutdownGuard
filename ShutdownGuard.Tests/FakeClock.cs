using ShutdownGuard.Core;

namespace ShutdownGuard.Tests;

public sealed class FakeClock : IClock
{
    private TaskCompletionSource? _delayTcs;
    private readonly object _lock = new();

    public DateTimeOffset Now { get; set; }

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        TaskCompletionSource tcs;
        lock (_lock)
        {
            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _delayTcs = tcs;
        }

        cancellationToken.Register(() =>
        {
            lock (_lock)
            {
                if (_delayTcs == tcs)
                    _delayTcs = null;
            }
            tcs.TrySetCanceled(cancellationToken);
        });

        return tcs.Task;
    }

    /// <summary>
    /// Completes the current pending Delay, allowing the scheduler loop
    /// to run one iteration. Call this after setting <see cref="Now"/>.
    /// </summary>
    public void Tick()
    {
        TaskCompletionSource? tcs;
        lock (_lock)
        {
            tcs = _delayTcs;
            _delayTcs = null;
        }
        tcs?.TrySetResult();
    }

    /// <summary>
    /// Sets <see cref="Now"/> and ticks the scheduler forward by one iteration.
    /// </summary>
    public async Task AdvanceToAsync(DateTimeOffset target)
    {
        Now = target;
        Tick();
        // Yield so the scheduler's async loop can process the tick
        await Task.Yield();
    }
}
