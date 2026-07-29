using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;
using RyzeHub.Application.Diagnostics;
using RyzeHub.Application.Hub;
using RyzeHub.Application.Pipeline;
using RyzeHub.Application.Tickets;
using RyzeHub.Infrastructure.DependencyInjection;

namespace RyzeHub.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the full RyzeHub composition root.</summary>
    public static IServiceCollection AddRyzeHub(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddRyzeHubOptions(configuration)
            .AddRyzeHubCore()
            .AddHubPlatform()
            .AddRyzeAuthIntegration(configuration)
            .AddRyzeHubSecurity(configuration)
            .AddRyzeHubClients()
            .AddRyzeHubPipeline();

        return services;
    }

    private static IServiceCollection AddRyzeHubOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PipelineOptions>().Bind(configuration.GetSection(PipelineOptions.SectionName));
        services.AddOptions<SourceOptions>().Bind(configuration.GetSection(SourceOptions.SectionName));
        services.AddOptions<DestinationOptions>().Bind(configuration.GetSection(DestinationOptions.SectionName));
        services.AddOptions<SecurityOptions>().Bind(configuration.GetSection(SecurityOptions.SectionName));
        services.AddOptions<HubPlatformOptions>().Bind(configuration.GetSection(HubPlatformOptions.SectionName));
        services.AddOptions<HubManagerOptions>().Bind(configuration.GetSection(HubManagerOptions.SectionName));
        services.AddOptions<RyzeAuthOptions>().Bind(configuration.GetSection(RyzeAuthOptions.SectionName));

        return services;
    }

    private static IServiceCollection AddRyzeHubCore(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<PipelineMetrics>();
        services.AddSingleton<IErrorDetectionEngine, ErrorDetectionEngine>();
        services.AddSingleton<IAnomalyDetector, AnomalyDetector>();
        services.AddSingleton<TicketTransformer>();

        return services;
    }

    private static IServiceCollection AddRyzeHubPipeline(this IServiceCollection services)
    {
        services.AddSingleton<ITicketTransferService, TicketTransferService>();

        // Explicit factories: the optional RyzeAuth and encryption dependencies are
        // resolved with GetService, which the default container would not do.
        services.AddSingleton<IPipelineHealthService>(provider => new PipelineHealthService(
            provider.GetRequiredService<ILogger<PipelineHealthService>>(),
            provider.GetRequiredService<ISourceTicketClient>(),
            provider.GetRequiredService<IDestinationTicketClient>(),
            provider.GetRequiredService<IHubPlatform>(),
            provider.GetRequiredService<ISystemClock>(),
            provider.GetService<IRyzeAuthClient>()));

        services.AddSingleton<ITransferOutcomeHandler>(provider => new TransferOutcomeHandler(
            provider.GetRequiredService<ILogger<TransferOutcomeHandler>>(),
            provider.GetRequiredService<ISourceTicketClient>(),
            provider.GetRequiredService<IAuditLogger>(),
            provider.GetRequiredService<IHubPlatform>(),
            provider.GetRequiredService<IErrorDetectionEngine>(),
            provider.GetRequiredService<ISystemClock>(),
            provider.GetService<IRyzeAuthClient>()));

        services.AddSingleton<IHubManager, HubManager>();

        services.AddSingleton<ITicketPipeline>(provider => new TicketPipeline(
            provider.GetRequiredService<ILogger<TicketPipeline>>(),
            provider.GetRequiredService<IOptions<PipelineOptions>>(),
            provider.GetRequiredService<IOptions<SecurityOptions>>(),
            provider.GetRequiredService<ISourceTicketClient>(),
            provider.GetRequiredService<IDestinationTicketClient>(),
            provider.GetRequiredService<TicketTransformer>(),
            provider.GetRequiredService<ITicketTransferService>(),
            provider.GetRequiredService<IPipelineHealthService>(),
            provider.GetRequiredService<ITransferOutcomeHandler>(),
            provider.GetRequiredService<IHubPlatform>(),
            provider.GetRequiredService<PipelineMetrics>(),
            provider.GetRequiredService<IErrorDetectionEngine>(),
            provider.GetRequiredService<ISystemClock>(),
            provider.GetService<ITicketEncryptionManager>()));

        return services;
    }
}
