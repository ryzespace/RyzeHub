using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RyzeHub.Application;

/// <summary>JSON file persistence for vault entries. Holds no crypto or access-control logic.</summary>
internal sealed class VaultFileStore(string vaultPath, ILogger logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<Dictionary<string, VaultEntry>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(vaultPath))
        {
            return new Dictionary<string, VaultEntry>(StringComparer.Ordinal);
        }

        await using var stream = File.OpenRead(vaultPath);
        var entries = await JsonSerializer.DeserializeAsync<Dictionary<string, VaultEntry>>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (entries is null)
        {
            return new Dictionary<string, VaultEntry>(StringComparer.Ordinal);
        }

        logger.LogInformation("Loaded {Count} keys from vault", entries.Count);
        return new Dictionary<string, VaultEntry>(entries, StringComparer.Ordinal);
    }

    public async Task SaveAsync(Dictionary<string, VaultEntry> entries, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(vaultPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(vaultPath);
        await JsonSerializer.SerializeAsync(stream, entries, SerializerOptions, cancellationToken);
        logger.LogDebug("Saved vault to {VaultPath}", vaultPath);
    }
}
