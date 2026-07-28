using System.Text.Json.Serialization;

namespace RyzeHub.Domain.Tickets;

public sealed class ConversationMessage
{
    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public MessageRole Role { get; set; } = MessageRole.Client;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    public static ConversationMessage Create(string sender, MessageRole role, string content) => new()
    {
        Sender = sender,
        Role = role,
        Content = content,
        Timestamp = DateTimeOffset.UtcNow,
        MessageId = Guid.NewGuid().ToString()
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Content))
        {
            errors.Add("Empty message content");
        }

        if (string.IsNullOrWhiteSpace(Sender))
        {
            errors.Add("Empty sender");
        }

        return errors;
    }
}

public sealed class Ticket
{
    [JsonPropertyName("ticket_id")]
    public string TicketId { get; set; } = string.Empty;

    [JsonPropertyName("ticket_type")]
    public TicketType TicketType { get; set; } = TicketType.Other;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("conversation")]
    public List<ConversationMessage> Conversation { get; set; } = [];

    [JsonPropertyName("priority")]
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    [JsonPropertyName("status")]
    public TicketStatus Status { get; set; } = TicketStatus.New;

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("client_name")]
    public string ClientName { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("transferred_at")]
    public DateTimeOffset? TransferredAt { get; set; }

    [JsonPropertyName("helpcenter_ticket_id")]
    public string? HelpCenterTicketId { get; set; }

    [JsonPropertyName("checksum")]
    public string? Checksum { get; set; }

    /// <summary>
    /// Organization that owns the ticket, resolved from a RyzeAuth API key or JWT.
    /// </summary>
    [JsonPropertyName("organization_id")]
    public string? OrganizationId { get; set; }

    public static Ticket Create(string ticketId, TicketType ticketType, string description) => new()
    {
        TicketId = ticketId,
        TicketType = ticketType,
        Description = description,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(TicketId))
        {
            errors.Add("Missing ticket_id");
        }

        if (string.IsNullOrWhiteSpace(Description))
        {
            errors.Add("Missing description");
        }

        if (Conversation.Count == 0)
        {
            errors.Add("Missing conversation");
        }

        for (var index = 0; index < Conversation.Count; index++)
        {
            foreach (var error in Conversation[index].Validate())
            {
                errors.Add($"Message #{index + 1}: {error}");
            }
        }

        return errors;
    }

    public bool IsValid => Validate().Count == 0;

    public void Normalize()
    {
        Description = Description.Trim();
        Conversation.Sort(static (left, right) => left.Timestamp.CompareTo(right.Timestamp));
        ClientName = string.Join(
            ' ',
            ClientName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static word => string.Concat(
                    char.ToUpperInvariant(word[0]),
                    word[1..].ToLowerInvariant())));
    }
}

public sealed record TransferResult(
    string TicketId,
    bool Success,
    string? HelpCenterTicketId,
    string? ErrorMessage,
    DateTimeOffset Timestamp,
    int RetryCount);

public sealed record BatchTransferResult(
    IReadOnlyList<TransferResult> Results,
    int Total,
    int Successful,
    int Failed,
    long DurationMs);
