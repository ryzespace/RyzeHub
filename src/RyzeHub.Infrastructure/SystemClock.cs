using RyzeHub.Application;

namespace RyzeHub.Infrastructure;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
