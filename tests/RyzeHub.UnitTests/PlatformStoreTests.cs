using System.Text.Json.Nodes;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Platform.Stores;
using RyzeHub.Domain.Platform;

namespace RyzeHub.UnitTests;

/// <summary>Each store owns one module, so it can be exercised without the facade.</summary>
public sealed class PlatformStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EventStoreTrimsToTheConfiguredMaximum()
    {
        var store = new RealtimeEventStore(TestSupport.Options(new HubPlatformOptions { MaxEvents = 3 }));

        for (var index = 0; index < 10; index++)
        {
            store.Append(new RealtimeEvent(
                $"event-{index}",
                "test.topic",
                RealtimeEventType.AuditRecorded,
                "actor",
                "subject",
                [],
                null,
                Now));
        }

        store.Count.Should().Be(3);
        store.Recent(10).Should().HaveCount(3);
        store.Recent(1)[0].EventId.Should().Be("event-9");
    }

    [Fact]
    public void NotificationStoreCountsDeliveriesAndFiltersByUser()
    {
        var store = new NotificationStore(TestSupport.Options(new HubPlatformOptions()));

        store.Send("user-1", "A", "first", [NotificationChannel.Email], NotificationPriority.Low, null, Now);
        store.Send("user-2", "B", "second", [NotificationChannel.Sms], NotificationPriority.High, null, Now);

        store.DeliveredCount.Should().Be(2);
        store.Recent("user-1", 10).Should().ContainSingle();
        store.Recent(null, 10).Should().HaveCount(2);
    }

    [Fact]
    public void NotificationStoreUpdatesAnExistingEndpoint()
    {
        var store = new NotificationStore(TestSupport.Options(new HubPlatformOptions()));

        store.SetEndpoint(NotificationChannel.Email, "smtp://backup", enabled: false);

        var endpoint = store.Endpoints().Single(item => item.Channel == NotificationChannel.Email);
        endpoint.Target.Should().Be("smtp://backup");
        endpoint.Enabled.Should().BeFalse();
        store.Endpoints().Should().HaveCount(6);
    }

    [Fact]
    public void AuditStoreFiltersByActorAndReturnsNewestFirst()
    {
        var store = new AuditStore();

        store.Record("actor-1", "first", AuditCategory.Security, "res", null, Now);
        store.Record("actor-2", "second", AuditCategory.Finance, "res", null, Now);
        store.Record("actor-1", "third", AuditCategory.Support, "res", null, Now);

        store.Count.Should().Be(3);
        store.Recent("actor-1", 10).Should().HaveCount(2);
        store.Recent(null, 1)[0].Action.Should().Be("third");
    }

    [Fact]
    public void AccessControlStoreUnionsRoleAndDirectPermissions()
    {
        var store = new AccessControlStore();

        store.AssignRole("user-1", "User");
        store.GrantPermission("user-1", "billing:manage");

        var profile = store.AccessProfile("user-1");
        profile.Roles.Should().ContainSingle().Which.Should().Be("User");
        profile.DirectPermissions.Should().ContainSingle().Which.Should().Be("billing:manage");
        profile.EffectivePermissions.Should().Contain("server:create");
        store.HasPermission("user-1", "billing:manage").Should().BeTrue();
        store.HasPermission("user-1", "permissions:manage").Should().BeFalse();
    }

    [Fact]
    public void AccessControlStoreReturnsEmptyProfileForUnknownUsers()
    {
        var profile = new AccessControlStore().AccessProfile("nobody");

        profile.Roles.Should().BeEmpty();
        profile.EffectivePermissions.Should().BeEmpty();
    }

    [Fact]
    public void IdentityStateStoreTracksActiveUsersAcrossSessions()
    {
        var store = new IdentityStateStore();

        var device = store.RegisterDevice("user-1", "Laptop", ClientPlatform.Desktop, trusted: true, Now);
        var first = store.CreateSession("user-1", device.DeviceId, ClientPlatform.Desktop, "203.0.113.1", Now);
        store.CreateSession("user-1", device.DeviceId, ClientPlatform.Web, "203.0.113.2", Now);

        // Two sessions, one distinct user.
        store.ActiveSessionCount.Should().Be(2);
        store.ActiveUserCount.Should().Be(1);

        store.TerminateSession(first.SessionId, Now);
        store.ActiveSessionCount.Should().Be(1);
        store.ActiveUserCount.Should().Be(1);
    }

    [Fact]
    public void IdentityStateStoreAccumulatesPresenceDevicesWithoutDuplicates()
    {
        var store = new IdentityStateStore();

        store.UpdatePresence("user-1", PresenceStatus.Online, "device-a", Now);
        store.UpdatePresence("user-1", PresenceStatus.Busy, "device-b", Now);
        store.UpdatePresence("user-1", PresenceStatus.Busy, "device-a", Now);

        var presence = store.Presence("user-1").Single();
        presence.Status.Should().Be(PresenceStatus.Busy);
        presence.ActiveDevices.Should().BeEquivalentTo(new[] { "device-a", "device-b" });
    }

    [Fact]
    public void RevokedDevicesRemainVisibleButInactive()
    {
        var store = new IdentityStateStore();
        var device = store.RegisterDevice("user-1", "Phone", ClientPlatform.Mobile, trusted: false, Now);

        store.RevokeDevice(device.DeviceId, Now);

        store.Devices("user-1").Single().Active.Should().BeFalse();
    }

    [Fact]
    public void GatewayCacheStoreExposesRyzeAuthRouteAndRoundTripsValues()
    {
        var store = new GatewayCacheStore(TestSupport.Options(new HubPlatformOptions()));

        store.Routes().Should().Contain(route => route.UpstreamService == "RyzeAuth" && route.AuthRequired);

        store.Put("key", new JsonObject { ["score"] = 92 }, Now);

        store.Get("key")!["score"]!.GetValue<int>().Should().Be(92);
        store.Get("missing").Should().BeNull();
        store.EntryCount.Should().Be(1);
    }

    [Fact]
    public void EngagementStoreFiltersMessagesForBothParticipants()
    {
        var store = new EngagementStore();

        store.RecordMessage("user-1", "support-1", "hello", Now);
        store.RecordMessage("user-2", "support-1", "hi", Now);

        store.Messages("user-1", 10).Should().ContainSingle();
        store.Messages("support-1", 10).Should().HaveCount(2);
        store.MessageCount.Should().Be(2);
    }

    [Fact]
    public void OperationsStoreReportsDegradedHealthWhenAServiceFails()
    {
        var store = new OperationsStore(TestSupport.Options(new HubPlatformOptions()), TestSupport.Clock());

        store.Health(0, 0, 0).Status.Should().Be("healthy");

        store.UpdateServiceHealth("RyzeAuth", healthy: false, latencyMs: 250, Now);

        var health = store.Health(0, 0, 0);
        health.Status.Should().Be("degraded");
        health.DegradedServices.Should().Be(1);
        store.Telemetry.ApiErrors.Should().Be(1);
    }

    [Fact]
    public void OperationsStoreRespectsTheTelemetryToggle()
    {
        var store = new OperationsStore(
            TestSupport.Options(new HubPlatformOptions { TelemetryEnabled = false }),
            TestSupport.Clock());

        store.RecordApiRequest(failed: true);

        store.Telemetry.ApiRequests.Should().Be(0);
        store.Telemetry.ApiErrors.Should().Be(0);
    }
}
