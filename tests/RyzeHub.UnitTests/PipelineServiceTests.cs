using Moq;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Pipeline;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.UnitTests;

public sealed class TicketTransferServiceTests
{
    private static Ticket MakeTicket(string id = "T-001") =>
        Ticket.Create(id, TicketType.Bug, "Something broke");

    private static TicketTransferService Create(
        IDestinationTicketClient destination,
        int maxRetries = 3,
        int retryDelaySeconds = 0) =>
        new(
            TestSupport.Logger<TicketTransferService>(),
            destination,
            TestSupport.Options(new DestinationOptions
            {
                MaxRetries = maxRetries,
                RetryDelaySeconds = retryDelaySeconds
            }),
            TestSupport.Clock());

    [Fact]
    public async Task SuccessfulTransferReportsTheHelpCenterId()
    {
        var destination = new Mock<IDestinationTicketClient>();
        destination
            .Setup(client => client.CreateTicketAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("HC-42");

        var results = await Create(destination.Object).TransferAsync([MakeTicket()], CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Success.Should().BeTrue();
        results[0].HelpCenterTicketId.Should().Be("HC-42");
        results[0].RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task TransientFailureIsRetriedUntilItSucceeds()
    {
        var attempts = 0;
        var destination = new Mock<IDestinationTicketClient>();
        destination
            .Setup(client => client.CreateTicketAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                return attempts < 3
                    ? throw new HttpRequestException("upstream hiccup")
                    : Task.FromResult("HC-7");
            });

        var results = await Create(destination.Object).TransferAsync([MakeTicket()], CancellationToken.None);

        results[0].Success.Should().BeTrue();
        results[0].RetryCount.Should().Be(3);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task PermanentFailureStopsAtTheRetryLimit()
    {
        var destination = new Mock<IDestinationTicketClient>();
        destination
            .Setup(client => client.CreateTicketAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream is down"));

        var results = await Create(destination.Object, maxRetries: 2).TransferAsync([MakeTicket()], CancellationToken.None);

        results[0].Success.Should().BeFalse();
        results[0].ErrorMessage.Should().Be("upstream is down");
        results[0].RetryCount.Should().Be(2);

        destination.Verify(
            client => client.CreateTicketAsync(It.IsAny<Ticket>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task OneFailingTicketDoesNotBlockTheRest()
    {
        var destination = new Mock<IDestinationTicketClient>();
        destination
            .Setup(client => client.CreateTicketAsync(It.Is<Ticket>(t => t.TicketId == "T-bad"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("rejected"));
        destination
            .Setup(client => client.CreateTicketAsync(It.Is<Ticket>(t => t.TicketId == "T-good"), It.IsAny<CancellationToken>()))
            .ReturnsAsync("HC-9");

        var results = await Create(destination.Object, maxRetries: 1)
            .TransferAsync([MakeTicket("T-bad"), MakeTicket("T-good")], CancellationToken.None);

        results.Should().HaveCount(2);
        results.Single(r => r.TicketId == "T-bad").Success.Should().BeFalse();
        results.Single(r => r.TicketId == "T-good").Success.Should().BeTrue();
    }
}

public sealed class PipelineHealthServiceTests
{
    private static PipelineHealthService Create(
        bool sourceHealthy,
        bool destinationHealthy,
        IRyzeAuthClient? ryzeAuth = null)
    {
        var source = new Mock<ISourceTicketClient>();
        source
            .Setup(client => client.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((sourceHealthy, TimeSpan.FromMilliseconds(5)));

        var destination = new Mock<IDestinationTicketClient>();
        destination
            .Setup(client => client.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((destinationHealthy, TimeSpan.FromMilliseconds(7)));

        return new PipelineHealthService(
            TestSupport.Logger<PipelineHealthService>(),
            source.Object,
            destination.Object,
            TestSupport.CreateHubPlatform(),
            TestSupport.Clock(),
            ryzeAuth);
    }

    [Fact]
    public async Task AllUpstreamsHealthyReportsHealthy()
    {
        var health = await Create(true, true).CheckAsync(CancellationToken.None);

        health.Status.Should().Be("healthy");
        health.Source.Status.Should().Be("up");
        health.Destination.Status.Should().Be("up");
        health.Auth.Should().BeNull();
    }

    [Fact]
    public async Task PartialOutageReportsDegraded()
    {
        var health = await Create(true, false).CheckAsync(CancellationToken.None);

        health.Status.Should().Be("degraded");
        health.Destination.Status.Should().Be("down");
    }

    [Fact]
    public async Task TotalOutageReportsUnhealthy()
    {
        var health = await Create(false, false).CheckAsync(CancellationToken.None);

        health.Status.Should().Be("unhealthy");
    }

    [Fact]
    public async Task ReachableRyzeAuthIsSurfacedInTheAuthBlock()
    {
        var ryzeAuth = new Mock<IRyzeAuthClient>();
        ryzeAuth
            .Setup(client => client.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, TimeSpan.FromMilliseconds(12), true, true));

        var health = await Create(true, true, ryzeAuth.Object).CheckAsync(CancellationToken.None);

        health.Auth.Should().NotBeNull();
        health.Auth!.Status.Should().Be("up");
        health.Auth.JwksReachable.Should().BeTrue();
        health.Status.Should().Be("healthy");
    }

    [Fact]
    public async Task RyzeAuthOutageDegradesOverallHealth()
    {
        var ryzeAuth = new Mock<IRyzeAuthClient>();
        ryzeAuth
            .Setup(client => client.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unreachable"));

        var health = await Create(true, true, ryzeAuth.Object).CheckAsync(CancellationToken.None);

        health.Auth!.Status.Should().Be("down");
        health.Status.Should().Be("degraded");
    }

    [Fact]
    public async Task ThrowingProbeIsReportedAsDownRatherThanPropagating()
    {
        var source = new Mock<ISourceTicketClient>();
        source
            .Setup(client => client.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var destination = new Mock<IDestinationTicketClient>();
        destination
            .Setup(client => client.HealthCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, TimeSpan.Zero));

        var service = new PipelineHealthService(
            TestSupport.Logger<PipelineHealthService>(),
            source.Object,
            destination.Object,
            TestSupport.CreateHubPlatform(),
            TestSupport.Clock());

        var health = await service.CheckAsync(CancellationToken.None);

        health.Source.Status.Should().Be("down");
        health.Status.Should().Be("degraded");
    }
}
