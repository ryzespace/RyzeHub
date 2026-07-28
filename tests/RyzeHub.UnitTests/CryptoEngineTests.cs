using System.Text;
using RyzeHub.Application;
using RyzeHub.Domain.Errors;

namespace RyzeHub.UnitTests;

public sealed class CryptoEngineTests
{
    [Fact]
    public void EncryptThenDecryptRoundTripsPlaintext()
    {
        using var engine = new CryptoEngine(TestSupport.Logger<CryptoEngine>(), TestSupport.TestKey());
        var plaintext = "Hello, World!"u8.ToArray();

        var packet = engine.Encrypt(plaintext);
        var decrypted = engine.Decrypt(packet);

        decrypted.Should().Equal(plaintext);
    }

    [Fact]
    public void EnvelopeRoundTripsUnicodeContent()
    {
        using var engine = new CryptoEngine(TestSupport.Logger<CryptoEngine>(), TestSupport.TestKey());
        const string Plaintext = "Zgłoszenie z polskimi znakami: ąćęłńóśźż";

        var envelope = engine.EncryptToEnvelope(Plaintext);

        envelope.Should().StartWith("ENC:");
        engine.DecryptFromEnvelope(envelope).Should().Be(Plaintext);
    }

    [Fact]
    public void DecryptRejectsTamperedCiphertext()
    {
        using var engine = new CryptoEngine(TestSupport.Logger<CryptoEngine>(), TestSupport.TestKey());
        var packet = engine.Encrypt("sensitive"u8);

        packet.Ciphertext[0] ^= 0xFF;

        var act = () => engine.Decrypt(packet);
        act.Should().Throw<EncryptionFailedException>();
    }

    [Fact]
    public void RotateKeyProducesNewKeyIdAndKeepsOldPacketsReadable()
    {
        using var engine = new CryptoEngine(TestSupport.Logger<CryptoEngine>(), TestSupport.TestKey());
        var originalKeyId = engine.CurrentKeyId;
        var packet = engine.Encrypt("before rotation"u8);

        var newKeyId = engine.RotateKey(TestSupport.TestKey(1));

        newKeyId.Should().NotBeNullOrEmpty().And.NotBe(originalKeyId);
        engine.CurrentKeyId.Should().Be(newKeyId);
        Encoding.UTF8.GetString(engine.Decrypt(packet)).Should().Be("before rotation");
    }

    [Fact]
    public void ConstructorRejectsShortKeys()
    {
        var act = () => new CryptoEngine(TestSupport.Logger<CryptoEngine>(), Convert.ToBase64String(new byte[16]));
        act.Should().Throw<EncryptionFailedException>();
    }

    [Fact]
    public void ConstructorRejectsMissingKey()
    {
        var act = () => new CryptoEngine(TestSupport.Logger<CryptoEngine>(), string.Empty);
        act.Should().Throw<ConfigurationException>();
    }
}
