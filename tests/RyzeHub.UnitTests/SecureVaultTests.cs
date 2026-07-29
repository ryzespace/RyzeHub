using System.Text;
using RyzeHub.Application;
using RyzeHub.Domain.Errors;

namespace RyzeHub.UnitTests;

public sealed class SecureVaultTests : IDisposable
{
    private readonly string _vaultPath = Path.Combine(Path.GetTempPath(), $"ryzehub-vault-{Guid.NewGuid():N}.json");

    private SecureVault CreateVault(string user = "test_user") =>
        new(TestSupport.Logger<SecureVault>(), TestSupport.TestKey(), _vaultPath, user);

    [Fact]
    public async Task StoreAndRetrieveRoundTripsKeyMaterial()
    {
        var vault = CreateVault();
        var keyData = "secret key data"u8.ToArray();

        await vault.StoreKeyAsync("key-1", keyData, "Test Key", "Test description", [], CancellationToken.None);
        var retrieved = await vault.RetrieveKeyAsync("key-1", CancellationToken.None);

        retrieved.Should().Equal(keyData);
    }

    [Fact]
    public async Task IntegrityHashChangesWhenContentChanges()
    {
        var vault = CreateVault();

        await vault.StoreKeyAsync("key-1", "data1"u8.ToArray(), "Key 1", "Desc", [], CancellationToken.None);
        var firstHash = await vault.IntegrityHashAsync(CancellationToken.None);

        await vault.StoreKeyAsync("key-2", "data2"u8.ToArray(), "Key 2", "Desc", [], CancellationToken.None);
        var secondHash = await vault.IntegrityHashAsync(CancellationToken.None);

        firstHash.Should().NotBe(secondHash);
    }

    [Fact]
    public async Task RetrievingMissingKeyThrows()
    {
        var vault = CreateVault();

        var act = async () => await vault.RetrieveKeyAsync("does-not-exist", CancellationToken.None);
        await act.Should().ThrowAsync<TicketNotFoundException>();
    }

    [Fact]
    public async Task VaultPersistsAcrossInstances()
    {
        var first = CreateVault();
        await first.StoreKeyAsync("persisted", "value"u8.ToArray(), "Persisted", "Desc", [], CancellationToken.None);

        var second = CreateVault();
        var keys = await second.ListKeysAsync(CancellationToken.None);

        keys.Should().Contain("persisted");
        Encoding.UTF8.GetString(await second.RetrieveKeyAsync("persisted", CancellationToken.None)).Should().Be("value");
    }

    [Fact]
    public async Task DeletedKeysDisappearFromTheListing()
    {
        var vault = CreateVault();
        await vault.StoreKeyAsync("temp", "value"u8.ToArray(), "Temp", "Desc", [], CancellationToken.None);

        await vault.DeleteKeyAsync("temp", CancellationToken.None);

        (await vault.ListKeysAsync(CancellationToken.None)).Should().NotContain("temp");
    }

    [Fact]
    public async Task StoredPayloadIsNotWrittenInPlaintext()
    {
        var vault = CreateVault();
        await vault.StoreKeyAsync("key-1", "super-secret-value"u8.ToArray(), "Key", "Desc", [], CancellationToken.None);

        var fileContent = await File.ReadAllTextAsync(_vaultPath);

        fileContent.Should().NotContain("super-secret-value");
    }

    public void Dispose()
    {
        if (File.Exists(_vaultPath))
        {
            File.Delete(_vaultPath);
        }
    }
}

public sealed class KeyManagerTests
{
    private static KeyManager CreateManager() => new(TestSupport.Logger<KeyManager>(), TestSupport.TestKey());

    [Fact]
    public void DerivesChildKeysForAPurpose()
    {
        var manager = CreateManager();
        var keyId = manager.DeriveKey(KeyPurpose.Encryption, "tickets");

        keyId.Should().NotBeNullOrEmpty();
        manager.GetKey(keyId).Should().NotBeNull();
        manager.GetKey(keyId)!.Metadata.Purpose.Should().Be(KeyPurpose.Encryption);
    }

    [Fact]
    public void GeneratesShortLivedSessionKeys()
    {
        var manager = CreateManager();
        var keyId = manager.GenerateSessionKey();

        var key = manager.GetKey(keyId);
        key.Should().NotBeNull();
        key!.Metadata.Purpose.Should().Be(KeyPurpose.Session);
        key.Metadata.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public void RotatesMasterKeyAndIncrementsVersion()
    {
        var manager = CreateManager();
        var originalKeyId = manager.CurrentKeyId;

        var newKeyId = manager.RotateMasterKey(TestSupport.TestKey(1));

        newKeyId.Should().NotBe(originalKeyId);
        manager.CurrentKey.Metadata.Version.Should().Be(2);
    }

    [Fact]
    public void ExportsMetadataForAllKeys()
    {
        var manager = CreateManager();
        manager.DeriveKey(KeyPurpose.Signing, "requests");

        manager.ExportMetadata().Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
