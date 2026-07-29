using Microsoft.Extensions.Logging;
using RyzeHub.Domain.Errors;

namespace RyzeHub.Infrastructure.Clients;

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// Trips after <paramref name="failureThreshold"/> consecutive failures and rejects calls
/// until <paramref name="recoveryTimeout"/> elapses, then allows a single trial request.
/// </summary>
public sealed class CircuitBreaker(
    string serviceName,
    int failureThreshold,
    TimeSpan recoveryTimeout,
    ILogger logger)
{
    private readonly Lock _gate = new();
    private CircuitState _state = CircuitState.Closed;
    private int _failureCount;
    private DateTimeOffset? _lastFailure;

    public CircuitState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Throws <see cref="CircuitBreakerOpenException"/> while the circuit is open.</summary>
    public void EnsureClosed()
    {
        lock (_gate)
        {
            if (_state != CircuitState.Open)
            {
                return;
            }

            if (_lastFailure is { } lastFailure && DateTimeOffset.UtcNow - lastFailure > recoveryTimeout)
            {
                _state = CircuitState.HalfOpen;
                logger.LogInformation("Circuit breaker HALF-OPEN for {Service}", serviceName);
                return;
            }

            throw new CircuitBreakerOpenException(serviceName);
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _failureCount++;
            _lastFailure = DateTimeOffset.UtcNow;

            if (_failureCount >= failureThreshold && _state != CircuitState.Open)
            {
                _state = CircuitState.Open;
                logger.LogWarning("Circuit breaker OPEN for {Service}", serviceName);
            }
        }
    }
}
