using System.Text.Json.Serialization;

namespace RyzeHub.Domain.Tickets;

[JsonConverter(typeof(JsonStringEnumConverter<TicketType>))]
public enum TicketType
{
    Bug,
    FeatureRequest,
    Question,
    Complaint,
    TechnicalIssue,
    Other
}

[JsonConverter(typeof(JsonStringEnumConverter<TicketPriority>))]
public enum TicketPriority
{
    Low,
    Medium,
    High,
    Critical
}

[JsonConverter(typeof(JsonStringEnumConverter<TicketStatus>))]
public enum TicketStatus
{
    New,
    InProgress,
    Waiting,
    Transferred,
    Resolved,
    Closed
}

[JsonConverter(typeof(JsonStringEnumConverter<MessageRole>))]
public enum MessageRole
{
    Client,
    Agent,
    System
}

/// <summary>
/// Maps enum members onto the snake_case wire format used by the RyzeSpace APIs.
/// </summary>
public static class TicketEnumExtensions
{
    public static string ToWireValue(this TicketType value) => value switch
    {
        TicketType.Bug => "bug",
        TicketType.FeatureRequest => "feature_request",
        TicketType.Question => "question",
        TicketType.Complaint => "complaint",
        TicketType.TechnicalIssue => "technical_issue",
        _ => "other"
    };

    public static string ToWireValue(this TicketPriority value) => value switch
    {
        TicketPriority.Low => "low",
        TicketPriority.High => "high",
        TicketPriority.Critical => "critical",
        _ => "medium"
    };

    public static string ToWireValue(this TicketStatus value) => value switch
    {
        TicketStatus.New => "new",
        TicketStatus.InProgress => "in_progress",
        TicketStatus.Waiting => "waiting",
        TicketStatus.Transferred => "transferred",
        TicketStatus.Resolved => "resolved",
        _ => "closed"
    };

    public static string ToWireValue(this MessageRole value) => value switch
    {
        MessageRole.Agent => "agent",
        MessageRole.System => "system",
        _ => "client"
    };
}
