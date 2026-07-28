using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Errors;

namespace RyzeHub.Application;

/// <summary>
/// HMAC-SHA256 signatures for tickets and outbound API requests.
/// </summary>
public sealed class SignatureEngine : ISignatureEngine
{
    private const string AlgorithmName = "HMAC-SHA256";

    private readonly byte[] _signingKey;
    private readonly ILogger<SignatureEngine> _logger;

    public SignatureEngine(ILogger<SignatureEngine> logger, IOptions<SecurityOptions> options)
        : this(logger, string.IsNullOrWhiteSpace(options.Value.SigningKey)
            ? options.Value.EncryptionKey
            : options.Value.SigningKey)
    {
    }

    public SignatureEngine(ILogger<SignatureEngine> logger, string signingKeyBase64)
    {
        _logger = logger;

        if (string.IsNullOrWhiteSpace(signingKeyBase64))
        {
            throw new ConfigurationException("Signing key is not configured");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(signingKeyBase64);
        }
        catch (FormatException exception)
        {
            throw new EncryptionFailedException("Signing key must be valid base64", exception);
        }

        if (key.Length < 32)
        {
            throw new EncryptionFailedException("Signing key must be at least 32 bytes");
        }

        _signingKey = key;
        KeyId = Convert.ToHexStringLower(SHA256.HashData(key))[..32];
        _logger.LogInformation("Initialized signature engine with key ID: {KeyId}", KeyId);
    }

    public string KeyId { get; }

    public SignatureResult Sign(ReadOnlySpan<byte> data)
    {
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(_signingKey, data));
        _logger.LogDebug("Signed {ByteCount} bytes with key {KeyId}", data.Length, KeyId);
        return new SignatureResult(signature, AlgorithmName, KeyId, DateTimeOffset.UtcNow.ToString("O"));
    }

    public bool Verify(ReadOnlySpan<byte> data, string signature)
    {
        if (string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var computed = Convert.ToHexStringLower(HMACSHA256.HashData(_signingKey, data));
        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(signature));

        _logger.LogDebug("Signature verification: {Result} (key: {KeyId})", valid ? "valid" : "invalid", KeyId);
        return valid;
    }

    public SignatureResult SignTicket(string ticketId, string description, string ticketType) =>
        Sign(Encoding.UTF8.GetBytes($"{ticketId}:{description}:{ticketType}"));

    public bool VerifyTicketSignature(string ticketId, string description, string ticketType, string signature) =>
        Verify(Encoding.UTF8.GetBytes($"{ticketId}:{description}:{ticketType}"), signature);

    public SignatureResult SignRequest(string method, string path, string body, string timestamp) =>
        Sign(Encoding.UTF8.GetBytes($"{method}:{path}:{body}:{timestamp}"));

    public bool VerifyRequestSignature(string method, string path, string body, string timestamp, string signature) =>
        Verify(Encoding.UTF8.GetBytes($"{method}:{path}:{body}:{timestamp}"), signature);
}
