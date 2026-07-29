using Moq;
using RyzeHub.Application;
using RyzeHub.Application.Tickets;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.UnitTests;

public sealed class TicketTransformerTests
{
    private static TicketTransformer CreateTransformer() => new(
        TestSupport.Logger<TicketTransformer>(),
        Mock.Of<IAuditLogger>(),
        TestSupport.Clock());

    private static Ticket MakeTicket()
    {
        var ticket = Ticket.Create("T-001", TicketType.Bug, "Application crash");
        ticket.Conversation.Add(ConversationMessage.Create("John Smith", MessageRole.Client, "Cannot log in, critical error"));
        return ticket;
    }

    [Fact]
    public void AutoCategorizeDetectsTechnicalTickets()
    {
        CreateTransformer().AutoCategorize(MakeTicket()).Should().Be("technical");
    }

    [Fact]
    public void AutoCategorizeDetectsBillingTickets()
    {
        var ticket = Ticket.Create("T-002", TicketType.Question, "Problem with invoice");
        ticket.Conversation.Add(ConversationMessage.Create("Client", MessageRole.Client, "Payment failed, invoice not paid"));

        CreateTransformer().AutoCategorize(ticket).Should().Be("billing");
    }

    [Fact]
    public void AutoPrioritizeDetectsCriticalTickets()
    {
        var ticket = Ticket.Create("T-003", TicketType.Bug, "Critical error in production");
        CreateTransformer().AutoPrioritize(ticket).Should().Be(TicketPriority.Critical);
    }

    [Fact]
    public void ValidationRejectsIncompleteTickets()
    {
        MakeTicket().Validate().Should().BeEmpty();

        var invalid = Ticket.Create(string.Empty, TicketType.Other, string.Empty);
        invalid.Validate().Should().NotBeEmpty();
    }

    [Fact]
    public void FilterRemovesResolvedTickets()
    {
        var resolved = MakeTicket();
        resolved.Status = TicketStatus.Resolved;

        var (filtered, excluded) = CreateTransformer().FilterTickets(
            [MakeTicket(), resolved],
            filterResolved: true,
            filterClosed: true,
            excludeIds: new HashSet<string>(StringComparer.Ordinal));

        filtered.Should().HaveCount(1);
        excluded.Should().Be(1);
    }

    [Fact]
    public void FilterRemovesExcludedDuplicates()
    {
        var (filtered, excluded) = CreateTransformer().FilterTickets(
            [MakeTicket()],
            filterResolved: false,
            filterClosed: false,
            excludeIds: new HashSet<string>(StringComparer.Ordinal) { "T-001" });

        filtered.Should().BeEmpty();
        excluded.Should().Be(1);
    }

    [Fact]
    public void GenerateTagsIncludesTicketTypeAndSensitiveDataMarker()
    {
        var ticket = MakeTicket();
        ticket.Conversation.Add(ConversationMessage.Create("Client", MessageRole.Client, "my email is user@example.com"));

        var tags = CreateTransformer().GenerateTags(ticket);

        tags.Should().Contain("bug");
        tags.Should().Contain("contains_sensitive_data");
    }

    [Fact]
    public void ProcessBatchSeparatesValidAndInvalidTickets()
    {
        var invalid = Ticket.Create("T-999", TicketType.Other, string.Empty);

        var (valid, failed) = CreateTransformer().ProcessBatch(
            [MakeTicket(), invalid],
            autoCategorize: true,
            autoPriority: true,
            filterResolved: true,
            filterClosed: true,
            excludeIds: new HashSet<string>(StringComparer.Ordinal),
            checksumEnabled: true);

        valid.Should().HaveCount(1);
        valid[0].Checksum.Should().NotBeNullOrEmpty();
        valid[0].Category.Should().Be("technical");
        failed.Should().HaveCount(1);
        failed[0].Success.Should().BeFalse();
    }

    [Fact]
    public void NormalizeTitleCasesClientNameAndSortsConversation()
    {
        var ticket = MakeTicket();
        ticket.ClientName = "jOHN  smith";
        ticket.Conversation.Insert(0, new ConversationMessage
        {
            Sender = "Agent",
            Role = MessageRole.Agent,
            Content = "later message",
            Timestamp = DateTimeOffset.UtcNow.AddHours(1)
        });

        ticket.Normalize();

        ticket.ClientName.Should().Be("John Smith");
        ticket.Conversation[^1].Content.Should().Be("later message");
    }

    [Fact]
    public void ChecksumDetectsTampering()
    {
        var ticket = MakeTicket();
        ticket.Checksum = ChecksumGenerator.GenerateTicketChecksum(ticket);

        ChecksumGenerator.VerifyTicketChecksum(ticket).Should().BeTrue();

        ticket.Description = "tampered";
        ChecksumGenerator.VerifyTicketChecksum(ticket).Should().BeFalse();
    }

    [Fact]
    public void EncryptionManagerRoundTripsTicketFields()
    {
        using var engine = new CryptoEngine(TestSupport.Logger<CryptoEngine>(), TestSupport.TestKey());
        var manager = new TicketEncryptionManager(engine, TestSupport.Logger<TicketEncryptionManager>());
        var ticket = MakeTicket();
        var originalDescription = ticket.Description;
        var originalContent = ticket.Conversation[0].Content;

        manager.EncryptTicket(ticket);
        ticket.Description.Should().StartWith("ENC:");
        ticket.Conversation[0].Content.Should().StartWith("ENC:");

        manager.DecryptTicket(ticket);
        ticket.Description.Should().Be(originalDescription);
        ticket.Conversation[0].Content.Should().Be(originalContent);
    }
}
