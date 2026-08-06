using ShutdownGuard.Core;

namespace ShutdownGuard.Tests;

public sealed class FakeClock : IClock
{
    private TaskCompletionSource? _delayTcs;
    private TaskCompletionSource? _delayRequestedTcs;
    private readonly object _lock = new();

    public DateTimeOffset Now { get; set; }

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        TaskCompletionSource tcs;
        lock (_lock)
        {
            tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _delayTcs = tcs;
            // Signal that the scheduler has called Delay() (it's waiting for the next tick)
            _delayRequestedTcs?.TrySetResult();
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
    /// Waits for the scheduler loop to call Delay() again before returning,
    /// ensuring the iteration has fully completed (including any I/O in executors).
    /// </summary>
    public async Task AdvanceToAsync(DateTimeOffset target)
    {
        var requestedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_lock)
        {
            _delayRequestedTcs = requestedTcs;
        }

        Now = target;
        Tick();

        // Wait for the scheduler to call Delay() again, indicating it has
        // finished processing the current iteration (including executor I/O).
        await requestedTcs.Task;
    }
}
