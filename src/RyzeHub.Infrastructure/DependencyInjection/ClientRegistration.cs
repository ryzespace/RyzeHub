using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Hub;
using RyzeHub.Infrastructure.Clients;

namespace RyzeHub.Infrastructure.DependencyInjection;

/// <summary>Registers the outbound HTTP clients: ticket source, ticket destination and GitHub.</summary>
internal static class ClientRegistration
{
    public const string GitHubHttpClientName = "ryzehub.github";

    public static IServiceCollection AddRyzeHubClients(this IServiceCollection services)
    {
        services.AddHttpClient(ClientDashboardClient.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<SourceOptions>>().Value;
                Configure(client, options.BaseUrl, options.TimeoutSeconds, options.ApiKey);
            });

        services.AddHttpClient(HelpCenterClient.HttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<DestinationOptions>>().Value;
                Configure(client, options.BaseUrl, options.TimeoutSeconds, options.ApiKey);
            });

        services.AddHttpClient(GitHubHttpClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<HubManagerOptions>>().Value;
                Configure(client, options.BaseUrl, timeoutSeconds: 100, options.Token);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            });

        services.AddSingleton<ISourceTicketClient>(provider => new ClientDashboardClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientDashboardClient.HttpClientName),
            provider.GetRequiredService<ILogger<ClientDashboardClient>>(),
            provider.GetRequiredService<IOptions<SourceOptions>>()));

        services.AddSingleton<IDestinationTicketClient>(provider => new HelpCenterClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HelpCenterClient.HttpClientName),
            provider.GetRequiredService<ILogger<HelpCenterClient>>(),
            provider.GetRequiredService<IOptions<DestinationOptions>>()));

        services.AddSingleton<IGitHubManager>(provider => new GitHubManager(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(GitHubHttpClientName),
            provider.GetRequiredService<ILogger<GitHubManager>>(),
            provider.GetRequiredService<IOptions<HubManagerOptions>>().Value.OrganizationName));

        return services;
    }

    private static void Configure(HttpClient client, string baseUrl, int timeoutSeconds, string? bearerToken)
    {
        client.BaseAddress = new Uri(HttpClientDefaults.EnsureTrailingSlash(baseUrl));
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RyzeHub/1.0");

        if (!string.IsNullOrEmpty(bearerToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }
}
