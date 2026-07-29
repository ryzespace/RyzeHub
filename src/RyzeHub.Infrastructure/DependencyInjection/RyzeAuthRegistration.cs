using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeAuth.Contracts.Grpc;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Infrastructure.RyzeAuth;

namespace RyzeHub.Infrastructure.DependencyInjection;

/// <summary>
/// Registers the RyzeAuth control-plane integration: the client-credentials token provider,
/// the REST client for token introspection and audit forwarding, and the gRPC API-key channel.
/// </summary>
public static class RyzeAuthRegistration
{
    public static IServiceCollection AddRyzeAuthIntegration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authOptions = configuration.GetSection(RyzeAuthOptions.SectionName).Get<RyzeAuthOptions>()
            ?? new RyzeAuthOptions();

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
                client.BaseAddress = new Uri(HttpClientDefaults.EnsureTrailingSlash(options.ApiBaseUrl));
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
}

internal static class HttpClientDefaults
{
    public static string EnsureTrailingSlash(string url) => url.EndsWith('/') ? url : $"{url}/";
}
