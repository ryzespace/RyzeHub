using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RyzeHub.Application;

namespace RyzeHub.Cli.Commands;

internal sealed class EncryptCommand : ICommandHandler
{
    public string Name => "encrypt";

    public string Usage => "encrypt --data <text>";

    public string Description => "AES-256-GCM encrypt into a portable envelope";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var data = arguments.RequireValue("data");
        var engine = provider.GetRequiredService<ICryptoEngine>();

        ConsoleOutput.WriteJson(new
        {
            keyId = engine.CurrentKeyId,
            algorithm = "AES-256-GCM",
            originalBytes = Encoding.UTF8.GetByteCount(data),
            encrypted = engine.EncryptToEnvelope(data)
        });

        return Task.FromResult(ExitCodes.Success);
    }
}

internal sealed class DecryptCommand : ICommandHandler
{
    public string Name => "decrypt";

    public string Usage => "decrypt --data <envelope>";

    public string Description => "AES-256-GCM decrypt an envelope";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var data = arguments.RequireValue("data");
        var plaintext = provider.GetRequiredService<ICryptoEngine>().DecryptFromEnvelope(data);

        ConsoleOutput.WriteJson(new { decrypted = plaintext });
        return Task.FromResult(ExitCodes.Success);
    }
}

internal sealed class SignCommand : ICommandHandler
{
    public string Name => "sign";

    public string Usage => "sign --data <text>";

    public string Description => "HMAC-SHA256 sign";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var data = arguments.RequireValue("data");
        var signature = provider.GetRequiredService<ISignatureEngine>().Sign(Encoding.UTF8.GetBytes(data));

        ConsoleOutput.WriteJson(signature);
        return Task.FromResult(ExitCodes.Success);
    }
}

internal sealed class VerifyCommand : ICommandHandler
{
    public string Name => "verify";

    public string Usage => "verify --data <text> --signature <sig>";

    public string Description => "Verify an HMAC-SHA256 signature";

    public Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var data = arguments.RequireValue("data");
        var signature = arguments.RequireValue("signature");

        var valid = provider.GetRequiredService<ISignatureEngine>()
            .Verify(Encoding.UTF8.GetBytes(data), signature);

        ConsoleOutput.WriteJson(new { valid });
        return Task.FromResult(valid ? ExitCodes.Success : ExitCodes.Failure);
    }
}

internal sealed class VaultCommand : ICommandHandler
{
    public string Name => "vault";

    public string Usage => "vault <store|retrieve|list|integrity>";

    public string Description => "Secure vault operations";

    public async Task<int> ExecuteAsync(
        IServiceProvider provider,
        CommandArguments arguments,
        CancellationToken cancellationToken)
    {
        var vault = provider.GetRequiredService<ISecureVault>();
        var action = arguments.Action("list");

        switch (action)
        {
            case "store":
                await vault.StoreKeyAsync(
                    arguments.RequireValue("key-id"),
                    Encoding.UTF8.GetBytes(arguments.RequireValue("data")),
                    arguments.GetValue("name") ?? "unnamed",
                    "Stored via CLI",
                    [],
                    cancellationToken);
                ConsoleOutput.WriteJson(new { stored = true });
                return ExitCodes.Success;

            case "retrieve":
                var keyId = arguments.RequireValue("key-id");
                var data = await vault.RetrieveKeyAsync(keyId, cancellationToken);
                ConsoleOutput.WriteJson(new { keyId, bytes = data.Length, hex = Convert.ToHexStringLower(data) });
                return ExitCodes.Success;

            case "list":
                ConsoleOutput.WriteJson(await vault.ListKeysAsync(cancellationToken));
                return ExitCodes.Success;

            case "integrity":
                ConsoleOutput.WriteJson(new { integrityHash = await vault.IntegrityHashAsync(cancellationToken) });
                return ExitCodes.Success;

            default:
                ConsoleOutput.WriteError($"Unknown vault action: {action}");
                return ExitCodes.Failure;
        }
    }
}
