namespace ShutdownGuard.Services;

public interface IShutdownExecutor
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
