using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Errors;

namespace RyzeHub.Application;

/// <summary>
/// Encrypted key storage with an access control list and an integrity hash.
/// Encryption lives in <see cref="VaultCipher"/> and persistence in <see cref="VaultFileStore"/>,
/// so this type only owns access control and entry bookkeeping.
/// </summary>
public sealed class SecureVault : ISecureVault
{
    private readonly Dictionary<string, VaultEntry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<SecureVault> _logger;
    private readonly VaultCipher _cipher;
    private readonly VaultFileStore _fileStore;
    private readonly string _currentUser;
    private bool _loaded;

    public SecureVault(ILogger<SecureVault> logger, IOptions<SecurityOptions> options)
        : this(logger, options.Value.EncryptionKey, options.Value.VaultPath, options.Value.VaultUser)
    {
    }

    public SecureVault(ILogger<SecureVault> logger, string vaultKeyBase64, string vaultPath, string currentUser)
    {
        _logger = logger;
        _cipher = new VaultCipher(CryptoEngine.DecodeKey(vaultKeyBase64, requiredLength: 32, "Vault key must be 32 bytes"));
        _fileStore = new VaultFileStore(vaultPath, logger);
        _currentUser = currentUser;
    }

    public async Task StoreKeyAsync(
        string keyId,
        byte[] keyData,
        string name,
        string description,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        await MutateAsync(cancellationToken, () =>
        {
            var acl = new AccessControlList();

            // An empty admin list means the vault is unrestricted (single-user or bootstrap).
            if (acl.Admins.Count != 0 && !acl.CanWrite(_currentUser))
            {
                throw new AuthorizationDeniedException($"User {_currentUser} does not have write permission");
            }

            _entries[keyId] = new VaultEntry(
                keyId,
                _cipher.Encrypt(keyData),
                new VaultMetadata(name, description, tags, acl),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0);

            _logger.LogInformation("Stored key {KeyId} in vault", keyId);
        });
    }

    public async Task<byte[]> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        byte[] keyData = [];

        await MutateAsync(cancellationToken, () =>
        {
            var entry = RequireEntry(keyId);

            if (entry.Metadata.Acl.Readers.Count != 0 && !entry.Metadata.Acl.CanRead(_currentUser))
            {
                throw new AuthorizationDeniedException($"User {_currentUser} cannot read key {keyId}");
            }

            keyData = _cipher.Decrypt(entry.EncryptedData);
            _entries[keyId] = entry with
            {
                LastAccessed = DateTimeOffset.UtcNow,
                AccessCount = entry.AccessCount + 1
            };

            _logger.LogDebug("Retrieved key {KeyId} (access #{AccessCount})", keyId, entry.AccessCount + 1);
        });

        return keyData;
    }

    public async Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        await MutateAsync(cancellationToken, () =>
        {
            var entry = RequireEntry(keyId);

            if (entry.Metadata.Acl.Admins.Count != 0 && !entry.Metadata.Acl.CanAdmin(_currentUser))
            {
                throw new AuthorizationDeniedException($"User {_currentUser} cannot administer key {keyId}");
            }

            _entries.Remove(keyId);
            _logger.LogInformation("Deleted key {KeyId} from vault", keyId);
        });
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken) =>
        ReadAsync(cancellationToken, () => (IReadOnlyList<string>)[.. _entries.Keys.Order(StringComparer.Ordinal)]);

    public Task<VaultMetadata?> GetKeyMetadataAsync(string keyId, CancellationToken cancellationToken) =>
        ReadAsync(cancellationToken, () => _entries.TryGetValue(keyId, out var entry) ? entry.Metadata : null);

    public Task<string> IntegrityHashAsync(CancellationToken cancellationToken) =>
        ReadAsync(cancellationToken, () =>
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var (keyId, entry) in _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                hasher.AppendData(Encoding.UTF8.GetBytes(keyId));
                hasher.AppendData(entry.EncryptedData);
            }

            return Convert.ToHexStringLower(hasher.GetHashAndReset());
        });

    private VaultEntry RequireEntry(string keyId) =>
        _entries.TryGetValue(keyId, out var entry)
            ? entry
            : throw new TicketNotFoundException($"Key {keyId} not found in vault");

    /// <summary>Runs a mutation under the lock and persists the result.</summary>
    private async Task MutateAsync(CancellationToken cancellationToken, Action mutate)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureLoadedAsync(cancellationToken);
            mutate();
            await _fileStore.SaveAsync(_entries, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> ReadAsync<T>(CancellationToken cancellationToken, Func<T> read)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return read();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        foreach (var (keyId, entry) in await _fileStore.LoadAsync(cancellationToken))
        {
            _entries[keyId] = entry;
        }
    }
}
