using System.Security.Claims;
using Moq;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform;
using RyzeHub.Infrastructure.RyzeAuth;

namespace RyzeHub.UnitTests;

/// <summary>
/// Covers the RyzeAuth control-plane integration: role projection and API key gating.
/// </summary>
public sealed class RyzeAuthRoleSynchronizerTests
{
    private static (HubPlatform Hub, RyzeAuthRoleSynchronizer Synchronizer) Create(RyzeAuthOptions? options = null)
    {
        var hub = new HubPlatform(TestSupport.Options(new HubPlatformOptions()), TestSupport.Clock());
        var synchronizer = new RyzeAuthRoleSynchronizer(
            hub,
            TestSupport.Logger<RyzeAuthRoleSynchronizer>(),
            TestSupport.Options(options ?? new RyzeAuthOptions()));

        return (hub, synchronizer);
    }

    private static ClaimsPrincipal Principal(string subject, IEnumerable<string> roles, string? scope = null)
    {
        var claims = new List<Claim> { new("sub", subject) };
        claims.AddRange(roles.Select(role => new Claim("roles", role)));

        if (scope is not null)
        {
            claims.Add(new Claim("scope", scope));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Fact]
    public void MapsRealmRolesOntoHubRoles()
    {
        var (_, synchronizer) = Create();

        var mapped = synchronizer.MapRoles(["ryzehub-admin", "ryzehub-support", "unrelated-role"]);

        mapped.Should().BeEquivalentTo(new[] { "Admin", "Support" });
    }

    [Fact]
    public void SynchronizeAssignsRolesToTheHubPlatform()
    {
        var (hub, synchronizer) = Create();

        synchronizer.Synchronize(Principal("user-sub-1", ["ryzehub-admin"]));

        var profile = hub.AccessProfile("user-sub-1");
        profile.Roles.Should().Contain("Admin");
        profile.EffectivePermissions.Should().Contain("billing:manage");
    }

    [Fact]
    public void SynchronizeTranslatesHubScopesIntoPermissions()
    {
        var (hub, synchronizer) = Create();

        synchronizer.Synchronize(Principal("user-sub-2", ["ryzehub-user"], "hub:tickets:transfer openid profile"));

        hub.AccessProfile("user-sub-2").DirectPermissions.Should().Contain("tickets:transfer");
    }

    [Fact]
    public void SynchronizeIgnoresPrincipalsWithoutSubject()
    {
        var (hub, synchronizer) = Create();

        synchronizer.Synchronize(new ClaimsPrincipal(new ClaimsIdentity()));

        hub.ListAuditLogs(null, 10).Should().BeEmpty();
    }

    [Fact]
    public void CustomRoleMappingsAreHonoured()
    {
        var options = new RyzeAuthOptions
        {
            RoleMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["platform-owner"] = "SuperAdmin"
            }
        };

        var (_, synchronizer) = Create(options);

        synchronizer.MapRoles(["platform-owner", "ryzehub-admin"]).Should().BeEquivalentTo(new[] { "SuperAdmin" });
    }
}

public sealed class RyzeAuthAuditForwardingTests
{
    [Fact]
    public void AuditLoggerForwardsEventsWhenEnabled()
    {
        var ryzeAuth = new Mock<IRyzeAuthClient>();
        ryzeAuth
            .Setup(client => client.ForwardAuditEventAsync(It.IsAny<RyzeAuthAuditEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = new AuditLogger(
            TestSupport.Logger<AuditLogger>(),
            TestSupport.Options(new SecurityOptions { AuditLogEnabled = true }),
            TestSupport.Options(new RyzeAuthOptions { ForwardAuditEvents = true }),
            TestSupport.Clock(),
            ryzeAuth.Object);

        logger.LogTransfer("T-001", "client_dashboard", "HC-1");

        ryzeAuth.Verify(
            client => client.ForwardAuditEventAsync(
                It.Is<RyzeAuthAuditEvent>(evt => evt.EventType == "ryzehub.ticket_transfer" && evt.CorrelationId == "T-001"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void AuditLoggerSkipsForwardingWhenDisabled()
    {
        var ryzeAuth = new Mock<IRyzeAuthClient>();

        var logger = new AuditLogger(
            TestSupport.Logger<AuditLogger>(),
            TestSupport.Options(new SecurityOptions { AuditLogEnabled = true }),
            TestSupport.Options(new RyzeAuthOptions { ForwardAuditEvents = false }),
            TestSupport.Clock(),
            ryzeAuth.Object);

        logger.LogTransfer("T-002", "client_dashboard", "HC-2");

        ryzeAuth.Verify(
            client => client.ForwardAuditEventAsync(It.IsAny<RyzeAuthAuditEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void AuditLoggerRespectsDisabledAuditFlag()
    {
        var ryzeAuth = new Mock<IRyzeAuthClient>();

        var logger = new AuditLogger(
            TestSupport.Logger<AuditLogger>(),
            TestSupport.Options(new SecurityOptions { AuditLogEnabled = false }),
            TestSupport.Options(new RyzeAuthOptions { ForwardAuditEvents = true }),
            TestSupport.Clock(),
            ryzeAuth.Object);

        logger.Log("test", "T-003", "system", "details");

        ryzeAuth.Verify(
            client => client.ForwardAuditEventAsync(It.IsAny<RyzeAuthAuditEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
