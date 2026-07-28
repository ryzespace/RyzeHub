namespace RyzeHub.Application;

public interface ICryptoEngine
{
    EncryptedPacket Encrypt(ReadOnlySpan<byte> plaintext);

    byte[] Decrypt(EncryptedPacket packet);

    string EncryptToEnvelope(string plaintext);

    string DecryptFromEnvelope(string envelope);

    string CurrentKeyId { get; }

    bool NeedsRotation { get; }

    string RotateKey(string newKeyBase64);
}

public interface ISignatureEngine
{
    string KeyId { get; }

    SignatureResult Sign(ReadOnlySpan<byte> data);

    bool Verify(ReadOnlySpan<byte> data, string signature);

    SignatureResult SignTicket(string ticketId, string description, string ticketType);

    bool VerifyTicketSignature(string ticketId, string description, string ticketType, string signature);

    SignatureResult SignRequest(string method, string path, string body, string timestamp);

    bool VerifyRequestSignature(string method, string path, string body, string timestamp, string signature);
}

public interface IKeyManager
{
    string CurrentKeyId { get; }

    string DeriveKey(KeyPurpose purpose, string info);

    ManagedKey? GetKey(string keyId);

    ManagedKey CurrentKey { get; }

    string RotateMasterKey(string newMasterKeyBase64);

    string GenerateSessionKey();

    int CleanExpiredKeys();

    bool NeedsRotation { get; }

    IReadOnlyList<KeyMetadata> ExportMetadata();
}

public interface ISecureVault
{
    Task StoreKeyAsync(string keyId, byte[] keyData, string name, string description, IReadOnlyList<string> tags, CancellationToken cancellationToken);

    Task<byte[]> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken);

    Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken);

    Task<VaultMetadata?> GetKeyMetadataAsync(string keyId, CancellationToken cancellationToken);

    Task<string> IntegrityHashAsync(CancellationToken cancellationToken);
}
