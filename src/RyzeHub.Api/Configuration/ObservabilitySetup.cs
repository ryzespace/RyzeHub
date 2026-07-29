using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using RyzeHub.Application.Pipeline;

namespace RyzeHub.Api.Configuration;

internal static class ObservabilitySetup
{
    public static IServiceCollection AddRyzeHubObservability(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddMeter(PipelineMetrics.MeterName)
                .AddPrometheusExporter());

        return services;
    }

    public static IServiceCollection AddRyzeHubCors(this IServiceCollection services, string[] allowedOrigins)
    {
        services.AddCors(options => options.AddPolicy("strict", policy =>
        {
            if (allowedOrigins.Length != 0)
            {
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
            }
        }));

        return services;
    }

    public static IServiceCollection AddRyzeHubRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Partition by organization so one tenant cannot exhaust another tenant's budget.
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

        return services;
    }
}
