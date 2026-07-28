using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RyzeHub.Application;

namespace RyzeHub.UnitTests;

internal static class TestSupport
{
    public static ILogger<T> Logger<T>() => NullLogger<T>.Instance;

    public static IOptions<T> Options<T>(T value) where T : class => Microsoft.Extensions.Options.Options.Create(value);

    /// <summary>Deterministic 32 byte base64 key for crypto tests.</summary>
    public static string TestKey(byte fill = 0) => Convert.ToBase64String(Enumerable.Repeat(fill, 32).ToArray());

    public sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }

    public static ISystemClock Clock() => new FixedClock(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
}
