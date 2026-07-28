namespace RyzeHub.Application;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
