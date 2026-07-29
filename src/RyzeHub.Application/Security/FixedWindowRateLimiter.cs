namespace RyzeHub.Application;

/// <summary>
/// Simple fixed-window limiter used to stay inside upstream API quotas.
/// </summary>
public sealed class FixedWindowRateLimiter(int maxRequests, TimeSpan window)
{
    private readonly Lock _gate = new();
    private int _currentCount;
    private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;

    public bool TryAcquire()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (now - _windowStart >= window)
            {
                _windowStart = now;
                _currentCount = 0;
            }

            if (_currentCount >= maxRequests)
            {
                return false;
            }

            _currentCount++;
            return true;
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        while (!TryAcquire())
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }
}
