using RyzeHub.Domain.Errors;
using RyzeHub.Infrastructure.Clients;

namespace RyzeHub.UnitTests;

public sealed class CircuitBreakerTests
{
    private static CircuitBreaker Create(int threshold = 3, int recoverySeconds = 60) =>
        new("test-service", threshold, TimeSpan.FromSeconds(recoverySeconds), TestSupport.Logger<CircuitBreakerTests>());

    [Fact]
    public void StartsClosedAndAllowsCalls()
    {
        var breaker = Create();

        breaker.State.Should().Be(CircuitState.Closed);
        breaker.Invoking(b => b.EnsureClosed()).Should().NotThrow();
    }

    [Fact]
    public void StaysClosedBelowTheFailureThreshold()
    {
        var breaker = Create(threshold: 3);

        breaker.RecordFailure();
        breaker.RecordFailure();

        breaker.State.Should().Be(CircuitState.Closed);
        breaker.Invoking(b => b.EnsureClosed()).Should().NotThrow();
    }

    [Fact]
    public void OpensOnceTheThresholdIsReached()
    {
        var breaker = Create(threshold: 3);

        for (var index = 0; index < 3; index++)
        {
            breaker.RecordFailure();
        }

        breaker.State.Should().Be(CircuitState.Open);
        breaker.Invoking(b => b.EnsureClosed())
            .Should().Throw<CircuitBreakerOpenException>()
            .Which.Service.Should().Be("test-service");
    }

    [Fact]
    public void SuccessResetsTheFailureCount()
    {
        var breaker = Create(threshold: 3);

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.RecordFailure();
        breaker.RecordFailure();

        // The counter restarted, so two further failures must not trip it.
        breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void MovesToHalfOpenAfterTheRecoveryWindow()
    {
        var breaker = Create(threshold: 1, recoverySeconds: 0);
        breaker.RecordFailure();

        breaker.State.Should().Be(CircuitState.Open);

        // A zero recovery window elapses immediately, allowing a trial request.
        breaker.Invoking(b => b.EnsureClosed()).Should().NotThrow();
        breaker.State.Should().Be(CircuitState.HalfOpen);
    }

    [Fact]
    public void SuccessInHalfOpenClosesTheCircuit()
    {
        var breaker = Create(threshold: 1, recoverySeconds: 0);
        breaker.RecordFailure();
        breaker.EnsureClosed();

        breaker.RecordSuccess();

        breaker.State.Should().Be(CircuitState.Closed);
    }
}
