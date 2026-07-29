using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using RyzeHub.Api.Security;
using RyzeHub.Application.Configuration;
using RyzeHub.Infrastructure.RyzeAuth;

namespace RyzeHub.Api.Configuration;

/// <summary>
/// Wires authentication and authorization to the RyzeAuth control plane:
/// JWTs from the Keycloak realm, plus API keys introspected over gRPC.
/// </summary>
internal static class AuthenticationSetup
{
    public static IServiceCollection AddRyzeAuthAuthentication(
        this IServiceCollection services,
        RyzeAuthOptions authOptions,
        bool isDevelopment)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => ConfigureJwtBearer(options, authOptions, isDevelopment))
            .AddScheme<RyzeAuthApiKeySchemeOptions, RyzeAuthApiKeyHandler>(RyzeAuthApiKeyHandler.SchemeName, _ => { });

        services.AddAuthorization(options => ConfigurePolicies(options, authOptions));

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options, RyzeAuthOptions authOptions, bool isDevelopment)
    {
        options.Authority = authOptions.Authority;
        options.Audience = authOptions.ValidAudience;
        options.RequireHttpsMetadata = authOptions.RequireHttpsMetadata && !isDevelopment;
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authOptions.Authority,
            ValidateAudience = true,
            ValidAudience = authOptions.ValidAudience,
            ValidateLifetime = true,
            NameClaimType = "preferred_username",
            RoleClaimType = "roles"
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                // Project RyzeAuth realm roles and hub:* scopes onto the platform RBAC model.
                if (context.Principal is { } principal)
                {
                    context.HttpContext.RequestServices
                        .GetRequiredService<IRyzeAuthRoleSynchronizer>()
                        .Synchronize(principal);
                }

                return Task.CompletedTask;
            }
        };
    }

    private static void ConfigurePolicies(AuthorizationOptions options, RyzeAuthOptions authOptions)
    {
        var schemes = new[] { JwtBearerDefaults.AuthenticationScheme, RyzeAuthApiKeyHandler.SchemeName };

        var authenticated = new AuthorizationPolicyBuilder(schemes).RequireAuthenticatedUser().Build();
        options.DefaultPolicy = authenticated;
        options.FallbackPolicy = authenticated;

        options.AddPolicy("TicketTransfer", policy => policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => Grants(context.User, authOptions.RequiredApiKeyScope, "ryzehub-admin", "ryzehub-superadmin")));

        options.AddPolicy("PlatformWrite", policy => policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => Grants(context.User, "hub:platform:write", "ryzehub-admin", "ryzehub-superadmin")));

        options.AddPolicy("PlatformAdmin", policy => policy
            .AddAuthenticationSchemes(schemes)
            .RequireAuthenticatedUser()
            .RequireAssertion(context => Grants(context.User, "hub:platform:admin", "ryzehub-superadmin")));
    }

    /// <summary>True when the principal carries the scope, or any of the listed elevated roles.</summary>
    private static bool Grants(ClaimsPrincipal user, string scope, params string[] roles) =>
        HasScope(user, scope) || roles.Any(role => HasRole(user, role));

    private static bool HasScope(ClaimsPrincipal user, string scope) => user.FindAll("scope")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Contains(scope, StringComparer.Ordinal);

    private static bool HasRole(ClaimsPrincipal user, string role) =>
        user.FindAll("roles").Any(claim => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase))
        || user.IsInRole(role);
}
