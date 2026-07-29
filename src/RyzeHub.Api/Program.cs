using RyzeHub.Api.Configuration;
using RyzeHub.Api.Endpoints;
using RyzeHub.Api.Middleware;
using RyzeHub.Application.Configuration;
using RyzeHub.Infrastructure;
using Scalar.AspNetCore;
using Serilog;
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

    var authOptions = builder.Configuration.GetSection(RyzeAuthOptions.SectionName).Get<RyzeAuthOptions>()
        ?? new RyzeAuthOptions();
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    builder.Services.AddRyzeHub(builder.Configuration);
    builder.Services.AddOpenApi();
    builder.Services.AddHealthChecks();
    builder.Services.AddRyzeAuthAuthentication(authOptions, builder.Environment.IsDevelopment());
    builder.Services.AddRyzeHubCors(allowedOrigins);
    builder.Services.AddRyzeHubRateLimiting();
    builder.Services.AddRyzeHubObservability();

    var app = builder.Build();

    app.UseRyzeHubProblemDetails();
    app.UseMiddleware<CorrelationIdMiddleware>();
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

public partial class Program;
