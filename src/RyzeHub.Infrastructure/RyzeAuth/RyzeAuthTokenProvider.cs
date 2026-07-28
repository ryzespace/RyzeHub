using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;

namespace RyzeHub.Infrastructure.RyzeAuth;

public interface IRyzeAuthTokenProvider
{
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Fetches and caches a service access token from Keycloak using the client_credentials grant.
/// </summary>
public sealed class RyzeAuthTokenProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<RyzeAuthTokenProvider> logger,
    IOptions<RyzeAuthOptions> options) : IRyzeAuthTokenProvider
{
    public const string HttpClientName = "ryzehub.ryzeauth.token";

    private readonly RyzeAuthOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            return null;
        }

        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
        {
            return _cachedToken;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAt)
            {
                return _cachedToken;
            }

            var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret
            });

            using var response = await httpClient.PostAsync(
                $"{_options.Authority.TrimEnd('/')}/protocol/openid-connect/token",
                content,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to obtain RyzeAuth service token: {StatusCode}", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellationToken);
            var accessToken = payload?["access_token"]?.GetValue<string>();
            var expiresIn = payload?["expires_in"]?.GetValue<int>() ?? 300;

            if (string.IsNullOrEmpty(accessToken))
            {
                return null;
            }

            _cachedToken = accessToken;
            // Refresh 30s before the real expiry to avoid races on long calls.
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 30));
            logger.LogDebug("Obtained RyzeAuth service token valid for {ExpiresIn}s", expiresIn);
            return accessToken;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Could not reach RyzeAuth token endpoint");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }
}
