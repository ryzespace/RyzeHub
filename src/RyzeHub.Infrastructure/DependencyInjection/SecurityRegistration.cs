using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application;
using RyzeHub.Application.Configuration;

namespace RyzeHub.Infrastructure.DependencyInjection;

/// <summary>Registers the audit logger plus the crypto stack when an encryption key is configured.</summary>
internal static class SecurityRegistration
{
    public static IServiceCollection AddRyzeHubSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        // Explicit factories: these types have optional constructor parameters that the
        // default container would not resolve.
        services.AddSingleton<IAuditLogger>(provider => new AuditLogger(
            provider.GetRequiredService<ILogger<AuditLogger>>(),
            provider.GetRequiredService<IOptions<SecurityOptions>>(),
            provider.GetRequiredService<IOptions<RyzeAuthOptions>>(),
            provider.GetRequiredService<ISystemClock>(),
            provider.GetService<IRyzeAuthClient>()));

        var securityOptions = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>();
        if (string.IsNullOrWhiteSpace(securityOptions?.EncryptionKey))
        {
            // No key: the pipeline still runs, just without payload encryption.
            return services;
        }

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

        return services;
    }
}
