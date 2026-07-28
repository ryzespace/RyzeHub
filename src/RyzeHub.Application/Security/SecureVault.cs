using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Errors;

namespace RyzeHub.Application;

/// <summary>
/// Encrypted key storage with an access control list and integrity hashing.
/// </summary>
public sealed class SecureVault : ISecureVault
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly Dictionary<string, VaultEntry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<SecureVault> _logger;
    private readonly byte[] _vaultKey;
    private readonly string _vaultPath;
    private readonly string _currentUser;
    private bool _loaded;

    public SecureVault(ILogger<SecureVault> logger, IOptions<SecurityOptions> options)
        : this(logger, options.Value.EncryptionKey, options.Value.VaultPath, options.Value.VaultUser)
    {
    }

    public SecureVault(ILogger<SecureVault> logger, string vaultKeyBase64, string vaultPath, string currentUser)
    {
        _logger = logger;
        _vaultKey = CryptoEngine.DecodeKey(vaultKeyBase64, requiredLength: 32, "Vault key must be 32 bytes");
        _vaultPath = vaultPath;
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            var acl = new AccessControlList();
            if (acl.Admins.Count != 0 && !acl.CanWrite(_currentUser))
            {
                throw new AuthorizationDeniedException($"User {_currentUser} does not have write permission");
            }

            var entry = new VaultEntry(
                keyId,
                EncryptVaultData(keyData),
                new VaultMetadata(name, description, tags, acl),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0);

            _entries[keyId] = entry;
            await SaveAsync(cancellationToken);
            _logger.LogInformation("Stored key {KeyId} in vault", keyId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            if (!_entries.TryGetValue(keyId, out var entry))
            {
                throw new TicketNotFoundException($"Key {keyId} not found in vault");
            }

            if (entry.Metadata.Acl.Readers.Count != 0 && !entry.Metadata.Acl.CanRead(_currentUser))
            {
                throw new AuthorizationDeniedException($"User {_currentUser} cannot read key {keyId}");
            }

            var keyData = DecryptVaultData(entry.EncryptedData);
            _entries[keyId] = entry with
            {
                LastAccessed = DateTimeOffset.UtcNow,
                AccessCount = entry.AccessCount + 1
            };

            await SaveAsync(cancellationToken);
            _logger.LogDebug("Retrieved key {KeyId} (access #{AccessCount})", keyId, entry.AccessCount + 1);
            return keyData;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            if (!_entries.TryGetValue(keyId, out var entry))
            {
                throw new TicketNotFoundException($"Key {keyId} not found in vault");
            }

            if (entry.Metadata.Acl.Admins.Count != 0 && !entry.Metadata.Acl.CanAdmin(_currentUser))
            {
                throw new AuthorizationDeniedException($"User {_currentUser} cannot administer key {keyId}");
            }

            _entries.Remove(keyId);
            await SaveAsync(cancellationToken);
            _logger.LogInformation("Deleted key {KeyId} from vault", keyId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return [.. _entries.Keys.Order(StringComparer.Ordinal)];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<VaultMetadata?> GetKeyMetadataAsync(string keyId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _entries.TryGetValue(keyId, out var entry) ? entry.Metadata : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> IntegrityHashAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var (keyId, entry) in _entries.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                hasher.AppendData(System.Text.Encoding.UTF8.GetBytes(keyId));
                hasher.AppendData(entry.EncryptedData);
            }

            return Convert.ToHexStringLower(hasher.GetHashAndReset());
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[] EncryptVaultData(byte[] data)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[data.Length];
        var tag = new byte[TagSize];

        using var cipher = new AesGcm(_vaultKey, TagSize);
        cipher.Encrypt(nonce, data, ciphertext, tag);

        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result.AsSpan());
        ciphertext.CopyTo(result.AsSpan(NonceSize));
        tag.CopyTo(result.AsSpan(NonceSize + ciphertext.Length));
        return result;
    }

    private byte[] DecryptVaultData(byte[] data)
    {
        if (data.Length < NonceSize + TagSize)
        {
            throw new EncryptionFailedException("Invalid encrypted data: too short");
        }

        var cipherLength = data.Length - NonceSize - TagSize;
        var plaintext = new byte[cipherLength];

        using var cipher = new AesGcm(_vaultKey, TagSize);
        try
        {
            cipher.Decrypt(
                data.AsSpan(0, NonceSize),
                data.AsSpan(NonceSize, cipherLength),
                data.AsSpan(NonceSize + cipherLength, TagSize),
                plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new EncryptionFailedException("Vault decryption failed", exception);
        }

        return plaintext;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_vaultPath))
        {
            return;
        }

        await using var stream = File.OpenRead(_vaultPath);
        var entries = await JsonSerializer.DeserializeAsync<Dictionary<string, VaultEntry>>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (entries is null)
        {
            return;
        }

        foreach (var (keyId, entry) in entries)
        {
            _entries[keyId] = entry;
        }

        _logger.LogInformation("Loaded {Count} keys from vault", _entries.Count);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_vaultPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_vaultPath);
        await JsonSerializer.SerializeAsync(stream, _entries, SerializerOptions, cancellationToken);
        _logger.LogDebug("Saved vault to {VaultPath}", _vaultPath);
    }
}
