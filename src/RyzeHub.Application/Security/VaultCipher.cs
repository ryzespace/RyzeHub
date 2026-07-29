using System.Security.Cryptography;
using RyzeHub.Domain.Errors;

namespace RyzeHub.Application;

/// <summary>
/// AES-256-GCM envelope used for vault entries at rest: <c>nonce || ciphertext || tag</c>.
/// </summary>
internal sealed class VaultCipher(byte[] key)
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public byte[] Encrypt(byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var cipher = new AesGcm(key, TagSize);
        cipher.Encrypt(nonce, plaintext, ciphertext, tag);

        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        nonce.CopyTo(result.AsSpan());
        ciphertext.CopyTo(result.AsSpan(NonceSize));
        tag.CopyTo(result.AsSpan(NonceSize + ciphertext.Length));
        return result;
    }

    public byte[] Decrypt(byte[] data)
    {
        if (data.Length < NonceSize + TagSize)
        {
            throw new EncryptionFailedException("Invalid encrypted data: too short");
        }

        var cipherLength = data.Length - NonceSize - TagSize;
        var plaintext = new byte[cipherLength];

        using var cipher = new AesGcm(key, TagSize);

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
}
