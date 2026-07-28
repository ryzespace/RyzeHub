using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;

namespace RyzeHub.Api.Security;

public sealed class RyzeAuthApiKeySchemeOptions : AuthenticationSchemeOptions
{
    public string HeaderName { get; set; } = "X-RyzeHub-Api-Key";
}

/// <summary>
/// Authenticates machine-to-machine calls by introspecting the presented API key against RyzeAuth.
/// The resulting principal carries the organization id and the scopes granted by RyzeAuth.
/// </summary>
public sealed class RyzeAuthApiKeyHandler(
    IOptionsMonitor<RyzeAuthApiKeySchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IRyzeAuthClient ryzeAuth,
    IOptions<RyzeAuthOptions> authOptions)
    : AuthenticationHandler<RyzeAuthApiKeySchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    public const string SchemeName = "RyzeAuthApiKey";

    private readonly RyzeAuthOptions _authOptions = authOptions.Value;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var headerValues))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = headerValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return AuthenticateResult.Fail("Empty API key");
        }

        var result = await ryzeAuth.IntrospectApiKeyAsync(
            apiKey,
            _authOptions.RequiredApiKeyScope,
            Context.RequestAborted);

        if (!result.Active)
        {
            Logger.LogWarning("RyzeAuth rejected API key: {Reason}", result.Reason ?? "inactive");
            return AuthenticateResult.Fail(result.Reason ?? "API key is not active");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.KeyId ?? "unknown"),
            new("sub", result.KeyId ?? "unknown"),
            new("azp", "ryzehub-api-key")
        };

        if (result.OrganizationId is { } organizationId)
        {
            claims.Add(new Claim("organization_id", organizationId));
        }

        if (result.Scopes.Count > 0)
        {
            claims.Add(new Claim("scope", string.Join(' ', result.Scopes)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
