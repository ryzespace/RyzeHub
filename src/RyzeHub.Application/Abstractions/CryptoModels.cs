namespace RyzeHub.Application;

public sealed record EncryptionContext(
    byte Version,
    string Algorithm,
    string KeyId,
    byte[] Nonce,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsExpired => ExpiresAt is { } expiry && DateTimeOffset.UtcNow > expiry;
}

public sealed record EncryptedPacket(EncryptionContext Context, byte[] Ciphertext, byte[] AuthTag, string Checksum);

public sealed record SignatureResult(string Signature, string Algorithm, string KeyId, string Timestamp);

public enum KeyPurpose
{
    Master,
    Encryption,
    Signing,
    KeyDerivation,
    Session
}

public sealed record KeyMetadata(
    string KeyId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Algorithm,
    KeyPurpose Purpose,
    string? ParentKeyId,
    int Version);

public sealed record ManagedKey(byte[] PublicKey, byte[] PrivateKey, KeyMetadata Metadata);

public sealed record AccessControlList
{
    public IReadOnlyList<string> Readers { get; init; } = [];
    public IReadOnlyList<string> Writers { get; init; } = [];
    public IReadOnlyList<string> Admins { get; init; } = [];

    public bool CanRead(string user) =>
        Readers.Contains(user, StringComparer.Ordinal)
        || Writers.Contains(user, StringComparer.Ordinal)
        || Admins.Contains(user, StringComparer.Ordinal);

    public bool CanWrite(string user) =>
        Writers.Contains(user, StringComparer.Ordinal) || Admins.Contains(user, StringComparer.Ordinal);

    public bool CanAdmin(string user) => Admins.Contains(user, StringComparer.Ordinal);
}

public sealed record VaultMetadata(string Name, string Description, IReadOnlyList<string> Tags, AccessControlList Acl);

public sealed record VaultEntry(
    string KeyId,
    byte[] EncryptedData,
    VaultMetadata Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAccessed,
    long AccessCount);
