namespace ShutdownGuard.Services;

public sealed class DryRunShutdownExecutor : IShutdownExecutor
{
    public event Action<DateTimeOffset>? ShutdownTriggered;

    public DateTimeOffset? LastTriggeredAt { get; private set; }
    public int ExecuteCount { get; private set; }

    public Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        LastTriggeredAt = DateTimeOffset.Now;
        ExecuteCount++;

        ShutdownTriggered?.Invoke(LastTriggeredAt.Value);
        return Task.CompletedTask;
    }
}
