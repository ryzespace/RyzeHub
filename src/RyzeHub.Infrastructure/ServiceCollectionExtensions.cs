using System.Net.Http.Headers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeAuth.Contracts.Grpc;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Diagnostics;
using RyzeHub.Application.Hub;
using RyzeHub.Application.Pipeline;
using RyzeHub.Application.Platform;
using RyzeHub.Application.Tickets;
using RyzeHub.Infrastructure.Clients;
using RyzeHub.Infrastructure.RyzeAuth;

namespace RyzeHub.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRyzeHub(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PipelineOptions>().Bind(configuration.GetSection(PipelineOptions.SectionName));
        services.AddOptions<SourceOptions>().Bind(configuration.GetSection(SourceOptions.SectionName));
        services.AddOptions<DestinationOptions>().Bind(configuration.GetSection(DestinationOptions.SectionName));
        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.SectionName));
        services.AddOptions<HubPlatformOptions>().Bind(configuration.GetSection(HubPlatformOptions.SectionName));
        services.AddOptions<HubManagerOptions>().Bind(configuration.GetSection(HubManagerOptions.SectionName));
        services.AddOptions<RyzeAuthOptions>().Bind(configuration.GetSection(RyzeAuthOptions.SectionName));

        services.AddMemoryCache();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<PipelineMetrics>();

        services.AddSingleton<IHubPlatform, HubPlatform>();
        services.AddSingleton<IErrorDetectionEngine, ErrorDetectionEngine>();
        services.AddSingleton<IAnomalyDetector, AnomalyDetector>();
        services.AddSingleton<IAuditLogger>(provider => new AuditLogger(
            provider.GetRequiredService<ILogger<AuditLogger>>(),
            provider.GetRequiredService<IOptions<SecurityOptions>>(),
            provider.GetRequiredService<IOptions<RyzeAuthOptions>>(),
            provider.GetRequiredService<ISystemClock>(),
            provider.GetService<IRyzeAuthClient>()));
        services.AddSingleton<TicketTransformer>();

        // Crypto services need a configured 256-bit key. When none is present the pipeline still
        // runs, just without payload encryption, instead of failing to build the container.
        var securityOptions = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>();
        if (!string.IsNullOrWhiteSpace(securityOptions?.EncryptionKey))
        {
            services.AddSingleton<ICryptoEngine>(provider => new CryptoEngine(
                provider.GetRequiredService<ILogger<CryptoEngine>>(),
                provider.GetRequiredService<IOptions<SecurityOptions>>()));
            services.AddSingleton<ISignatureEngine>(provider => new SignatureEngine(
                provider.GetRequiredService<ILogger<SignatureEngine>>(),
                provider.GetRequiredService<IOptions<SecurityOptions>>()));
            services.AddSingleton<IKeyManager>(provider => new KeyManager(
                provider.GetRequiredService<ILogger<KeyManager>>(),
                provider.GetRequiredService<IOptions<SecurityOptions>>()));
            services.AddSingleton<ISecureVault>(provider => new SecureVault(
                provider.GetRequiredService<ILogger<SecureVault>>(),
                provider.GetRequiredService<IOptions<SecurityOptions>>()));
            services.AddSingleton<ITicketEncryptionManager, TicketEncryptionManager>();
        }

        services.AddHttpClient(ClientDashboardClient.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<SourceOptions>>().Value;
                client.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RyzeHub/1.0");
                if (!string.IsNullOrEmpty(options.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                }
            });

        services.AddHttpClient(HelpCenterClient.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<DestinationOptions>>().Value;
                client.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RyzeHub/1.0");
                if (!string.IsNullOrEmpty(options.ApiKey))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                }
            });

        services.AddSingleton<ISourceTicketClient>(provider => new ClientDashboardClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientDashboardClient.HttpClientName),
            provider.GetRequiredService<ILogger<ClientDashboardClient>>(),
            provider.GetRequiredService<IOptions<SourceOptions>>()));

        services.AddSingleton<IDestinationTicketClient>(provider => new HelpCenterClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HelpCenterClient.HttpClientName),
            provider.GetRequiredService<ILogger<HelpCenterClient>>(),
            provider.GetRequiredService<IOptions<DestinationOptions>>()));

        services.AddRyzeAuthIntegration(configuration);

        services.AddHttpClient(GitHubManagerHttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<HubManagerOptions>>().Value;
                client.BaseAddress = new Uri(EnsureTrailingSlash(options.BaseUrl));
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RyzeHub/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                if (!string.IsNullOrEmpty(options.Token))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
                }
            });

        services.AddSingleton<IGitHubManager>(provider => new GitHubManager(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(GitHubManagerHttpClientName),
            provider.GetRequiredService<ILogger<GitHubManager>>(),
            provider.GetRequiredService<IOptions<HubManagerOptions>>().Value.OrganizationName));

        services.AddSingleton<IHubManager, HubManager>();

        // Built explicitly: the default container does not honour optional constructor
        // parameters, and both the encryption manager and the RyzeAuth client are optional.
        services.AddSingleton<ITicketPipeline>(provider => new TicketPipeline(
            provider.GetRequiredService<ILogger<TicketPipeline>>(),
            provider.GetRequiredService<IOptions<PipelineOptions>>(),
            provider.GetRequiredService<IOptions<DestinationOptions>>(),
            provider.GetRequiredService<IOptions<SecurityOptions>>(),
            provider.GetRequiredService<ISourceTicketClient>(),
            provider.GetRequiredService<IDestinationTicketClient>(),
            provider.GetRequiredService<IAuditLogger>(),
            provider.GetRequiredService<TicketTransformer>(),
            provider.GetRequiredService<IHubPlatform>(),
            provider.GetRequiredService<PipelineMetrics>(),
            provider.GetRequiredService<IErrorDetectionEngine>(),
            provider.GetRequiredService<ISystemClock>(),
            provider.GetService<ITicketEncryptionManager>(),
            provider.GetService<IRyzeAuthClient>()));

        return services;
    }

    public const string GitHubManagerHttpClientName = "ryzehub.github";

    /// <summary>
    /// Registers the RyzeAuth control-plane integration (REST introspection, audit sink and gRPC API keys).
    /// </summary>
    public static IServiceCollection AddRyzeAuthIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var authOptions = configuration.GetSection(RyzeAuthOptions.SectionName).Get<RyzeAuthOptions>() ?? new RyzeAuthOptions();

        services.AddHttpClient(RyzeAuthTokenProvider.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<RyzeAuthOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RyzeHub/1.0");
            });

        services.AddSingleton<IRyzeAuthTokenProvider, RyzeAuthTokenProvider>();

        services.AddHttpClient(RyzeAuthClient.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<RyzeAuthOptions>>().Value;
                client.BaseAddress = new Uri(EnsureTrailingSlash(options.ApiBaseUrl));
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("RyzeHub/1.0");
            });

        if (authOptions.ApiKeyIntrospectionEnabled)
        {
            services.AddGrpcClient<ApiKeyIntrospection.ApiKeyIntrospectionClient>((provider, grpcOptions) =>
            {
                var options = provider.GetRequiredService<IOptions<RyzeAuthOptions>>().Value;
                grpcOptions.Address = new Uri(options.ApiBaseUrl);
            });
        }

        services.AddSingleton<IRyzeAuthClient>(provider => new RyzeAuthClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(RyzeAuthClient.HttpClientName),
            provider.GetRequiredService<IRyzeAuthTokenProvider>(),
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<ILogger<RyzeAuthClient>>(),
            provider.GetRequiredService<IOptions<RyzeAuthOptions>>(),
            provider.GetService<ApiKeyIntrospection.ApiKeyIntrospectionClient>()));

        services.AddSingleton<IRyzeAuthRoleSynchronizer, RyzeAuthRoleSynchronizer>();

        return services;
    }

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : $"{url}/";
}
