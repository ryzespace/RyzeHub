using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application.Tickets;

/// <summary>
/// Validation, enrichment and filtering rules applied before a ticket leaves the hub.
/// </summary>
public sealed partial class TicketTransformer(ILogger<TicketTransformer> logger, IAuditLogger audit, ISystemClock clock)
{
    private static readonly (string Category, string[] Keywords)[] CategoryKeywords =
    [
        ("billing", ["faktura", "platnosc", "invoice", "payment", "billing", "rachunek", "cena"]),
        ("technical", ["blad", "error", "crash", "awaria", "nie dziala", "bug", "timeout", "500", "404"]),
        ("account", ["konto", "account", "logowanie", "login", "haslo", "password", "rejestracja"]),
        ("feature", ["proponuje", "sugestia", "feature", "request", "nowa funkcja", "dodaj"]),
        ("integration", ["api", "integracja", "webhook", "integration", "sdk", "polaczenie"]),
        ("performance", ["wolno", "wydajnosc", "performance", "slow", "lag", "timeout"])
    ];

    private static readonly (TicketPriority Priority, string[] Keywords)[] PriorityKeywords =
    [
        (TicketPriority.Critical, ["critical", "production", "outage", "down", "emergency"]),
        (TicketPriority.High, ["important", "urgent", "asap", "blocking"]),
        (TicketPriority.Low, ["someday", "low priority", "not urgent", "question"])
    ];

    private static readonly Regex[] SensitivePatterns =
    [
        CreditCardRegex(),
        EmailRegex(),
        PhoneRegex()
    ];

    public string AutoCategorize(Ticket ticket)
    {
        var text = BuildSearchText(ticket).ToLowerInvariant();
        var bestCategory = "general";
        var bestScore = 0;

        foreach (var (category, keywords) in CategoryKeywords)
        {
            var score = keywords.Count(keyword => text.Contains(keyword, StringComparison.Ordinal));
            if (score > bestScore)
            {
                bestScore = score;
                bestCategory = category;
            }
        }

        logger.LogDebug("Auto-categorized ticket {TicketId}: {Category} (score={Score})", ticket.TicketId, bestCategory, bestScore);
        return bestCategory;
    }

    public TicketPriority AutoPrioritize(Ticket ticket)
    {
        var text = BuildSearchText(ticket).ToLowerInvariant();
        var bestPriority = TicketPriority.Medium;
        var bestScore = 0;

        foreach (var (priority, keywords) in PriorityKeywords)
        {
            var score = keywords.Count(keyword => text.Contains(keyword, StringComparison.Ordinal));
            if (score > bestScore)
            {
                bestScore = score;
                bestPriority = priority;
            }
        }

        logger.LogDebug("Auto-prioritized ticket {TicketId}: {Priority} (score={Score})", ticket.TicketId, bestPriority, bestScore);
        return bestPriority;
    }

    public IReadOnlyList<string> GenerateTags(Ticket ticket)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal) { ticket.TicketType.ToWireValue() };

        if (!string.IsNullOrEmpty(ticket.Category))
        {
            tags.Add(ticket.Category);
        }

        if (ticket.Priority is TicketPriority.High or TicketPriority.Critical)
        {
            tags.Add("needs_attention");
        }

        if (ticket.Conversation.Count > 3)
        {
            tags.Add("multi_message");
        }

        var text = BuildSearchText(ticket);
        if (SensitivePatterns.Any(pattern => pattern.IsMatch(text)))
        {
            tags.Add("contains_sensitive_data");
        }

        return [.. tags];
    }

    public void EnrichTicket(Ticket ticket, bool autoCategorize, bool autoPriority, bool checksumEnabled)
    {
        if (autoCategorize && string.IsNullOrEmpty(ticket.Category))
        {
            ticket.Category = AutoCategorize(ticket);
        }

        if (autoPriority && ticket.Priority == TicketPriority.Medium)
        {
            ticket.Priority = AutoPrioritize(ticket);
        }

        ticket.Tags = [.. GenerateTags(ticket)];
        ticket.TransferredAt = clock.UtcNow;

        if (checksumEnabled)
        {
            ticket.Checksum = ChecksumGenerator.GenerateTicketChecksum(ticket);
        }
    }

    public (IReadOnlyList<Ticket> Filtered, int ExcludedCount) FilterTickets(
        IReadOnlyList<Ticket> tickets,
        bool filterResolved,
        bool filterClosed,
        IReadOnlySet<string> excludeIds)
    {
        var filtered = tickets
            .Where(ticket =>
            {
                if (filterResolved && ticket.Status == TicketStatus.Resolved)
                {
                    return false;
                }

                if (filterClosed && ticket.Status == TicketStatus.Closed)
                {
                    return false;
                }

                return !excludeIds.Contains(ticket.TicketId);
            })
            .ToArray();

        var excluded = tickets.Count - filtered.Length;
        logger.LogInformation(
            "Filtered tickets: {Transferable} to transfer, {Excluded} excluded (resolved/closed/duplicate)",
            filtered.Length,
            excluded);

        return (filtered, excluded);
    }

    public (IReadOnlyList<Ticket> Valid, IReadOnlyList<TransferResult> Failed) ProcessBatch(
        IReadOnlyList<Ticket> tickets,
        bool autoCategorize,
        bool autoPriority,
        bool filterResolved,
        bool filterClosed,
        IReadOnlySet<string> excludeIds,
        bool checksumEnabled)
    {
        var (filtered, _) = FilterTickets(tickets, filterResolved, filterClosed, excludeIds);

        var validTickets = new List<Ticket>();
        var failedResults = new List<TransferResult>();

        foreach (var ticket in filtered)
        {
            var errors = ticket.Validate();
            if (errors.Count == 0)
            {
                ticket.Normalize();
                EnrichTicket(ticket, autoCategorize, autoPriority, checksumEnabled);
                validTickets.Add(ticket);
                continue;
            }

            logger.LogWarning("Ticket {TicketId} validation failed: {Errors}", ticket.TicketId, string.Join("; ", errors));
            audit.LogValidationError(ticket.TicketId, errors);
            failedResults.Add(new TransferResult(
                ticket.TicketId,
                Success: false,
                HelpCenterTicketId: null,
                ErrorMessage: string.Join("; ", errors),
                Timestamp: clock.UtcNow,
                RetryCount: 0));
        }

        logger.LogInformation("Batch processing: {Valid} valid, {Failed} failed", validTickets.Count, failedResults.Count);
        return (validTickets, failedResults);
    }

    private static string BuildSearchText(Ticket ticket)
    {
        var builder = new StringBuilder(ticket.Description);
        foreach (var message in ticket.Conversation)
        {
            builder.Append(' ').Append(message.Content);
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b")]
    private static partial Regex CreditCardRegex();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b")]
    private static partial Regex PhoneRegex();
}
