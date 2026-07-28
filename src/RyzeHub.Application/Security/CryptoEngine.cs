using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Errors;

namespace RyzeHub.Application;

/// <summary>
/// Multi-layer AES-256-GCM encryption with key rotation and per-packet integrity checksums.
/// </summary>
public sealed class CryptoEngine : ICryptoEngine, IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const string AlgorithmName = "AES-256-GCM";

    private readonly ConcurrentDictionary<string, AesGcm> _ciphers = new(StringComparer.Ordinal);
    private readonly ILogger<CryptoEngine> _logger;
    private readonly TimeSpan _keyRotationInterval;
    private readonly Lock _rotationGate = new();
    private string _currentKeyId;
    private DateTimeOffset _lastRotation;
    private bool _disposed;

    public CryptoEngine(ILogger<CryptoEngine> logger, IOptions<SecurityOptions> options)
        : this(logger, options.Value.EncryptionKey, TimeSpan.FromDays(options.Value.KeyRotationDays))
    {
    }

    public CryptoEngine(ILogger<CryptoEngine> logger, string masterKeyBase64, TimeSpan? keyRotationInterval = null)
    {
        _logger = logger;
        _keyRotationInterval = keyRotationInterval ?? TimeSpan.FromDays(30);

        var masterKey = DecodeKey(masterKeyBase64, requiredLength: 32, "Master key must be 32 bytes (256 bits)");
        _currentKeyId = GenerateKeyId(masterKey);
        _ciphers[_currentKeyId] = new AesGcm(masterKey, TagSize);
        _lastRotation = DateTimeOffset.UtcNow;

        _logger.LogInformation("Initialized crypto engine with key ID: {KeyId}", _currentKeyId);
    }

    public string CurrentKeyId => _currentKeyId;

    public bool NeedsRotation => DateTimeOffset.UtcNow - _lastRotation > _keyRotationInterval;

    public EncryptedPacket Encrypt(ReadOnlySpan<byte> plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var keyId = _currentKeyId;
        if (!_ciphers.TryGetValue(keyId, out var cipher))
        {
            throw new EncryptionFailedException($"Cipher not found for key {keyId}");
        }

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        cipher.Encrypt(nonce, plaintext, ciphertext, tag);

        // The tag travels with the ciphertext so the envelope stays a single blob.
        var payload = new byte[ciphertext.Length + TagSize];
        ciphertext.CopyTo(payload.AsSpan());
        tag.CopyTo(payload.AsSpan(ciphertext.Length));

        var context = new EncryptionContext(
            Version: 1,
            Algorithm: AlgorithmName,
            KeyId: keyId,
            Nonce: nonce,
            CreatedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(90),
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal));

        _logger.LogDebug("Encrypted {ByteCount} bytes with key {KeyId}", plaintext.Length, keyId);
        return new EncryptedPacket(context, payload, tag, CalculateChecksum(payload));
    }

    public byte[] Decrypt(EncryptedPacket packet)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(packet);

        if (packet.Context.IsExpired)
        {
            _logger.LogWarning("Attempting to decrypt expired packet with key {KeyId}", packet.Context.KeyId);
        }

        if (!_ciphers.TryGetValue(packet.Context.KeyId, out var cipher))
        {
            throw new EncryptionFailedException($"Cipher not found for key {packet.Context.KeyId}");
        }

        if (!string.IsNullOrEmpty(packet.Checksum)
            && !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(CalculateChecksum(packet.Ciphertext)),
                Encoding.ASCII.GetBytes(packet.Checksum)))
        {
            throw new EncryptionFailedException("Checksum mismatch - data may be corrupted");
        }

        if (packet.Ciphertext.Length < TagSize)
        {
            throw new EncryptionFailedException("Invalid encrypted data: too short");
        }

        var cipherLength = packet.Ciphertext.Length - TagSize;
        var plaintext = new byte[cipherLength];

        try
        {
            cipher.Decrypt(
                packet.Context.Nonce,
                packet.Ciphertext.AsSpan(0, cipherLength),
                packet.Ciphertext.AsSpan(cipherLength, TagSize),
                plaintext);
        }
        catch (CryptographicException exception)
        {
            throw new EncryptionFailedException("Decryption failed", exception);
        }

        _logger.LogDebug("Decrypted {ByteCount} bytes with key {KeyId}", plaintext.Length, packet.Context.KeyId);
        return plaintext;
    }

    /// <summary>Produces the portable <c>ENC:keyId:nonce:ciphertext</c> envelope.</summary>
    public string EncryptToEnvelope(string plaintext)
    {
        var packet = Encrypt(Encoding.UTF8.GetBytes(plaintext));
        return $"ENC:{packet.Context.KeyId}:{Convert.ToBase64String(packet.Context.Nonce)}:{Convert.ToBase64String(packet.Ciphertext)}";
    }

    public string DecryptFromEnvelope(string envelope)
    {
        if (!envelope.StartsWith("ENC:", StringComparison.Ordinal))
        {
            return envelope;
        }

        var parts = envelope.Split(':');
        if (parts.Length != 4)
        {
            throw new EncryptionFailedException("Invalid encrypted data format");
        }

        var ciphertext = Convert.FromBase64String(parts[3]);
        var packet = new EncryptedPacket(
            new EncryptionContext(
                Version: 1,
                Algorithm: AlgorithmName,
                KeyId: parts[1],
                Nonce: Convert.FromBase64String(parts[2]),
                CreatedAt: DateTimeOffset.UtcNow,
                ExpiresAt: null,
                Metadata: new Dictionary<string, string>(StringComparer.Ordinal)),
            ciphertext,
            AuthTag: [],
            Checksum: CalculateChecksum(ciphertext));

        return Encoding.UTF8.GetString(Decrypt(packet));
    }

    public string RotateKey(string newKeyBase64)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var newKey = DecodeKey(newKeyBase64, requiredLength: 32, "New key must be 32 bytes");
        var keyId = GenerateKeyId(newKey);

        lock (_rotationGate)
        {
            _ciphers[keyId] = new AesGcm(newKey, TagSize);
            _currentKeyId = keyId;
            _lastRotation = DateTimeOffset.UtcNow;
        }

        _logger.LogInformation("Rotated to new key: {KeyId}", keyId);
        return keyId;
    }

    internal static string CalculateChecksum(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    internal static string GenerateKeyId(ReadOnlySpan<byte> key)
    {
        var buffer = new byte[key.Length + 32];
        key.CopyTo(buffer);
        Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"), buffer.AsSpan(key.Length));
        return Convert.ToHexStringLower(SHA256.HashData(buffer))[..32];
    }

    internal static byte[] DecodeKey(string base64, int requiredLength, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new ConfigurationException("Encryption key is not configured");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            throw new EncryptionFailedException("Key must be valid base64", exception);
        }

        if (key.Length != requiredLength)
        {
            throw new EncryptionFailedException(errorMessage);
        }

        return key;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var cipher in _ciphers.Values)
        {
            cipher.Dispose();
        }

        _ciphers.Clear();
        _disposed = true;
    }
}
