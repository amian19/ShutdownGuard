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

        AppLogger.Info($"DryRun shutdown executed at {LastTriggeredAt.Value:yyyy-MM-dd HH:mm:ss zzz}");
        ShutdownTriggered?.Invoke(LastTriggeredAt.Value);
        return Task.CompletedTask;
    }
}
