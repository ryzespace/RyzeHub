using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace RyzeHub.IntegrationTests;

/// <summary>
/// Boots the API with RyzeAuth integration disabled so the host can start without a live control plane,
/// then verifies the public surface and that protected routes stay protected.
/// </summary>
public sealed class RyzeHubApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["RyzeAuth:Authority"] = "http://localhost:8080/realms/ryzespace",
                ["RyzeAuth:ApiBaseUrl"] = "http://localhost:8081",
                ["RyzeAuth:RequireHttpsMetadata"] = "false",
                ["RyzeAuth:ApiKeyIntrospectionEnabled"] = "false",
                ["RyzeAuth:ForwardAuditEvents"] = "false",
                ["Security:EncryptionKey"] = Convert.ToBase64String(new byte[32]),
                ["Security:SigningKey"] = Convert.ToBase64String(new byte[32])
            }));
    }
}

public sealed class ApiEndpointTests(RyzeHubApiFactory factory) : IClassFixture<RyzeHubApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task LivenessProbeIsAnonymous()
    {
        using var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReadinessProbeIsAnonymous()
    {
        using var response = await _client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PrometheusEndpointIsExposed()
    {
        using var response = await _client.GetAsync("/metrics");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PlatformModulesAreDiscoverableWithoutAuthentication()
    {
        var modules = await _client.GetFromJsonAsync<JsonArray>("/api/platform/modules");

        modules.Should().NotBeNull();
        modules!.Count.Should().Be(17);
    }

    [Fact]
    public async Task PlatformHealthIsAnonymous()
    {
        using var response = await _client.GetAsync("/api/platform/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HubRepositoryCatalogListsRyzeAuth()
    {
        var catalog = await _client.GetFromJsonAsync<JsonObject>("/api/hub/repositories");

        catalog.Should().NotBeNull();
        catalog!["active"]!.AsArray().Select(node => node!.GetValue<string>()).Should().Contain("RyzeAuth");
    }

    [Fact]
    public async Task ProtectedPlatformSnapshotRequiresAuthentication()
    {
        using var response = await _client.GetAsync("/api/platform/snapshot");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PipelineRunRequiresAuthentication()
    {
        using var response = await _client.PostAsync("/api/pipeline/run", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DiagnosticsRequireAuthentication()
    {
        using var response = await _client.GetAsync("/api/diagnostics/statistics");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SecurityHeadersArePresent()
    {
        using var response = await _client.GetAsync("/health/live");

        response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff).Should().BeTrue();
        nosniff!.Should().Contain("nosniff");
        response.Headers.Should().Contain(header => header.Key == "X-Frame-Options");
        response.Headers.Should().Contain(header => header.Key == "X-Correlation-ID");
    }
}
