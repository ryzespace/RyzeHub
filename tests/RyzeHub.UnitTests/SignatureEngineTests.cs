using RyzeHub.Application;

namespace RyzeHub.UnitTests;

public sealed class SignatureEngineTests
{
    private static SignatureEngine CreateEngine() =>
        new(TestSupport.Logger<SignatureEngine>(), TestSupport.TestKey());

    [Fact]
    public void SignAndVerifyRoundTrip()
    {
        var engine = CreateEngine();
        var data = "test data"u8.ToArray();

        var signature = engine.Sign(data);

        engine.Verify(data, signature.Signature).Should().BeTrue();
        signature.Algorithm.Should().Be("HMAC-SHA256");
    }

    [Fact]
    public void VerifyRejectsInvalidSignature()
    {
        var engine = CreateEngine();
        engine.Verify("test data"u8, "invalid_signature").Should().BeFalse();
    }

    [Fact]
    public void TicketSignatureRoundTrips()
    {
        var engine = CreateEngine();
        var signature = engine.SignTicket("T-001", "Test description", "bug");

        engine.VerifyTicketSignature("T-001", "Test description", "bug", signature.Signature).Should().BeTrue();
        engine.VerifyTicketSignature("T-002", "Test description", "bug", signature.Signature).Should().BeFalse();
    }

    [Fact]
    public void RequestSignatureRoundTrips()
    {
        var engine = CreateEngine();
        var signature = engine.SignRequest("POST", "/api/tickets", "{\"data\":1}", "2026-07-28T00:00:00Z");

        engine.VerifyRequestSignature("POST", "/api/tickets", "{\"data\":1}", "2026-07-28T00:00:00Z", signature.Signature)
            .Should().BeTrue();
    }
}
