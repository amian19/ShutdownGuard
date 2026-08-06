namespace ShutdownGuard.Core;

public interface IClock
{
    DateTimeOffset Now { get; }

    Task Delay(TimeSpan delay, CancellationToken cancellationToken);
}
