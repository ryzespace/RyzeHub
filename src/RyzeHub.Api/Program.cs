using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RyzeHub.Api.Endpoints;
using RyzeHub.Api.Middleware;
using RyzeHub.Api.Security;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Pipeline;
using RyzeHub.Domain.Errors;
using RyzeHub.Infrastructure;
using RyzeHub.Infrastructure.RyzeAuth;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Context;
using Serilog.Enrichers.Span;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithSpan()
        .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter()));

    var authSection = builder.Configuration.GetSection(RyzeAuthOptions.SectionName);
    var authOptions = authSection.Get<RyzeAuthOptions>() ?? new RyzeAuthOptions();
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    builder.Services.AddRyzeHub(builder.Configuration);
    builder.Services.AddOpenApi();
    builder.Services.AddHealthChecks();

    // Identity is owned by RyzeAuth: JWTs are issued by the Keycloak realm it fronts,
    // and machine-to-machine calls present API keys that RyzeAuth introspects over gRPC.
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Authority = authOptions.Authority;
            options.Audience = authOptions.ValidAudience;
            options.RequireHttpsMetadata = authOptions.RequireHttpsMetadata && !builder.Environment.IsDevelopment();
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
        })
        .AddScheme<RyzeAuthApiKeySchemeOptions, RyzeAuthApiKeyHandler>(RyzeAuthApiKeyHandler.SchemeName, _ => { });

    builder.Services.AddAuthorization(options =>
    {
        var jwtOrApiKey = new AuthorizationPolicyBuilder(
                JwtBearerDefaults.AuthenticationScheme,
                RyzeAuthApiKeyHandler.SchemeName)
            .RequireAuthenticatedUser();

        options.DefaultPolicy = jwtOrApiKey.Build();
        options.FallbackPolicy = jwtOrApiKey.Build();

        options.AddPolicy("TicketTransfer", policy => policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, RyzeAuthApiKeyHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                HasScope(context.User, authOptions.RequiredApiKeyScope)
                || HasRole(context.User, "ryzehub-admin")
                || HasRole(context.User, "ryzehub-superadmin")));

        options.AddPolicy("PlatformWrite", policy => policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, RyzeAuthApiKeyHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                HasScope(context.User, "hub:platform:write")
                || HasRole(context.User, "ryzehub-admin")
                || HasRole(context.User, "ryzehub-superadmin")));

        options.AddPolicy("PlatformAdmin", policy => policy
            .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, RyzeAuthApiKeyHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireAssertion(context =>
                HasScope(context.User, "hub:platform:admin")
                || HasRole(context.User, "ryzehub-superadmin")));
    });

    builder.Services.AddCors(options => options.AddPolicy("strict", policy =>
    {
        if (allowedOrigins.Length != 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    }));

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("pipeline-run", context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.FindFirst("organization_id")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    });

    builder.Services.AddOpenTelemetry()
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter())
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter(PipelineMetrics.MeterName)
            .AddPrometheusExporter());

    var app = builder.Build();

    app.UseExceptionHandler(errors => errors.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var (status, title, detail) = exception switch
        {
            TicketValidationException validation => (StatusCodes.Status400BadRequest, "Validation failed", validation.Message),
            AuthorizationDeniedException denied => (StatusCodes.Status403Forbidden, "Forbidden", denied.Message),
            AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Unauthorized", "A valid token or API key is required."),
            TicketNotFoundException => (StatusCodes.Status404NotFound, "Not found", "The requested resource does not exist."),
            RateLimitExceededException rateLimit => (StatusCodes.Status429TooManyRequests, "Rate limited", rateLimit.Message),
            CircuitBreakerOpenException breaker => (StatusCodes.Status503ServiceUnavailable, "Upstream unavailable", breaker.Message),
            ConfigurationException configuration => (StatusCodes.Status500InternalServerError, "Configuration error", configuration.Message),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error", "An unexpected error occurred.")
        };

        context.Response.StatusCode = status;
        await Results.Problem(detail, statusCode: status, title: title).ExecuteAsync(context);
    }));

    app.Use(async (context, next) =>
    {
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 128)
        {
            correlationId = context.TraceIdentifier;
        }

        context.Response.Headers["X-Correlation-ID"] = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next();
        }
    });

    app.UseMiddleware<SecurityHeadersMiddleware>();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHttpsRedirection();
    }

    app.UseSerilogRequestLogging();
    app.UseRateLimiter();
    app.UseCors("strict");
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("RyzeHub API")).AllowAnonymous();
    app.MapHealthChecks("/health/live").AllowAnonymous();
    app.MapHealthChecks("/health/ready").AllowAnonymous();
    app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous();

    app.MapPipelineEndpoints();
    app.MapPlatformEndpoints();
    app.MapDiagnosticsEndpoints();
    app.MapHubManagementEndpoints();

    await app.RunAsync();
}
catch (Exception exception)
{
    Log.Fatal(exception, "RyzeHub terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

static bool HasScope(ClaimsPrincipal user, string scope) => user.FindAll("scope")
    .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    .Contains(scope, StringComparer.Ordinal);

static bool HasRole(ClaimsPrincipal user, string role) =>
    user.FindAll("roles").Any(claim => string.Equals(claim.Value, role, StringComparison.OrdinalIgnoreCase))
    || user.IsInRole(role);

public partial class Program;
