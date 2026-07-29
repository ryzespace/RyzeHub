using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Errors;

namespace RyzeHub.Application;

/// <summary>
/// Hierarchical key management: master key -> derived keys -> short lived session keys.
/// </summary>
public sealed class KeyManager : IKeyManager
{
    private readonly ConcurrentDictionary<string, ManagedKey> _keys = new(StringComparer.Ordinal);
    private readonly ILogger<KeyManager> _logger;
    private readonly Lock _gate = new();
    private byte[] _masterKey;
    private string _currentKeyId;

    public KeyManager(ILogger<KeyManager> logger, IOptions<SecurityOptions> options)
        : this(logger, options.Value.EncryptionKey)
    {
    }

    public KeyManager(ILogger<KeyManager> logger, string masterKeyBase64)
    {
        _logger = logger;

        if (string.IsNullOrWhiteSpace(masterKeyBase64))
        {
            throw new ConfigurationException("Master key is not configured");
        }

        var masterKey = Convert.FromBase64String(masterKeyBase64);
        if (masterKey.Length < 32)
        {
            throw new EncryptionFailedException("Master key must be at least 32 bytes");
        }

        _masterKey = masterKey;
        _currentKeyId = CryptoEngine.GenerateKeyId(masterKey);
        _keys[_currentKeyId] = new ManagedKey(
            masterKey,
            masterKey,
            new KeyMetadata(_currentKeyId, DateTimeOffset.UtcNow, null, "HMAC-SHA256", KeyPurpose.Master, null, 1));

        _logger.LogInformation("Initialized key manager with master key ID: {KeyId}", _currentKeyId);
    }

    public string CurrentKeyId => _currentKeyId;

    public ManagedKey CurrentKey => _keys[_currentKeyId];

    public bool NeedsRotation =>
        CurrentKey.Metadata.ExpiresAt is { } expiry && DateTimeOffset.UtcNow > expiry.AddDays(-7);

    public string DeriveKey(KeyPurpose purpose, string info)
    {
        var context = Encoding.UTF8.GetBytes($"{info}|{purpose}|{DateTimeOffset.UtcNow:O}");
        var derived = HMACSHA256.HashData(_masterKey, context);
        var keyId = CryptoEngine.GenerateKeyId(derived);

        _keys[keyId] = new ManagedKey(
            derived,
            derived,
            new KeyMetadata(
                keyId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(90),
                "AES-256-GCM",
                purpose,
                _currentKeyId,
                1));

        _logger.LogDebug("Derived key {KeyId} for purpose {Purpose}", keyId, purpose);
        return keyId;
    }

    public ManagedKey? GetKey(string keyId) => _keys.TryGetValue(keyId, out var key) ? key : null;

    public string RotateMasterKey(string newMasterKeyBase64)
    {
        var newMasterKey = Convert.FromBase64String(newMasterKeyBase64);
        if (newMasterKey.Length < 32)
        {
            throw new EncryptionFailedException("New master key must be at least 32 bytes");
        }

        lock (_gate)
        {
            var keyId = CryptoEngine.GenerateKeyId(newMasterKey);
            _keys[keyId] = new ManagedKey(
                newMasterKey,
                newMasterKey,
                new KeyMetadata(
                    keyId,
                    DateTimeOffset.UtcNow,
                    null,
                    "HMAC-SHA256",
                    KeyPurpose.Master,
                    _currentKeyId,
                    CurrentKey.Metadata.Version + 1));

            _masterKey = newMasterKey;
            _currentKeyId = keyId;
            _logger.LogInformation("Rotated master key to: {KeyId}", keyId);
            return keyId;
        }
    }

    public string GenerateSessionKey()
    {
        var sessionKey = RandomNumberGenerator.GetBytes(32);
        var keyId = CryptoEngine.GenerateKeyId(sessionKey);

        _keys[keyId] = new ManagedKey(
            sessionKey,
            sessionKey,
            new KeyMetadata(
                keyId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(24),
                "AES-256-GCM",
                KeyPurpose.Session,
                _currentKeyId,
                1));

        _logger.LogDebug("Generated session key: {KeyId}", keyId);
        return keyId;
    }

    public int CleanExpiredKeys()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _keys
            .Where(pair => pair.Value.Metadata.Purpose != KeyPurpose.Master
                && pair.Value.Metadata.ExpiresAt is { } expiry
                && now > expiry)
            .Select(pair => pair.Key)
            .ToArray();

        foreach (var keyId in expired)
        {
            _keys.TryRemove(keyId, out _);
            _logger.LogDebug("Removed expired key: {KeyId}", keyId);
        }

        if (expired.Length > 0)
        {
            _logger.LogInformation("Cleaned {Count} expired keys", expired.Length);
        }

        return expired.Length;
    }

    public IReadOnlyList<KeyMetadata> ExportMetadata() => [.. _keys.Values.Select(key => key.Metadata)];
}
