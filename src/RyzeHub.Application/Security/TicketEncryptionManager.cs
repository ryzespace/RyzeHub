using Microsoft.Extensions.Logging;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application;

public interface ITicketEncryptionManager
{
    void EncryptTicket(Ticket ticket);

    void DecryptTicket(Ticket ticket);
}

/// <summary>
/// Applies the crypto engine to the sensitive ticket fields (description and conversation bodies).
/// </summary>
public sealed class TicketEncryptionManager(ICryptoEngine cryptoEngine, ILogger<TicketEncryptionManager> logger)
    : ITicketEncryptionManager
{
    public void EncryptTicket(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (!string.IsNullOrEmpty(ticket.Description) && !IsEncrypted(ticket.Description))
        {
            ticket.Description = cryptoEngine.EncryptToEnvelope(ticket.Description);
        }

        foreach (var message in ticket.Conversation)
        {
            if (!string.IsNullOrEmpty(message.Content) && !IsEncrypted(message.Content))
            {
                message.Content = cryptoEngine.EncryptToEnvelope(message.Content);
            }
        }

        logger.LogDebug("Encrypted sensitive fields for ticket {TicketId}", ticket.TicketId);
    }

    public void DecryptTicket(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (IsEncrypted(ticket.Description))
        {
            ticket.Description = cryptoEngine.DecryptFromEnvelope(ticket.Description);
        }

        foreach (var message in ticket.Conversation)
        {
            if (IsEncrypted(message.Content))
            {
                message.Content = cryptoEngine.DecryptFromEnvelope(message.Content);
            }
        }

        logger.LogDebug("Decrypted sensitive fields for ticket {TicketId}", ticket.TicketId);
    }

    private static bool IsEncrypted(string value) => value.StartsWith("ENC:", StringComparison.Ordinal);
}
