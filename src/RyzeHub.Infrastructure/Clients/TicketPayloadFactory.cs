using System.Text.Json.Nodes;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Infrastructure.Clients;

/// <summary>Builds the HelpCenter wire representation of a ticket.</summary>
internal static class TicketPayloadFactory
{
    public static JsonObject Create(Ticket ticket) => new()
    {
        ["ticket_id"] = ticket.TicketId,
        ["ticket_type"] = ticket.TicketType.ToWireValue(),
        ["description"] = ticket.Description,
        ["conversation"] = BuildConversation(ticket),
        ["priority"] = ticket.Priority.ToWireValue(),
        ["category"] = ticket.Category,
        ["tags"] = BuildTags(ticket),
        ["client_id"] = ticket.ClientId,
        ["client_name"] = ticket.ClientName,
        ["organization_id"] = ticket.OrganizationId,
        ["source"] = "client_dashboard",
        ["source_ticket_id"] = ticket.TicketId,
        ["checksum"] = ticket.Checksum
    };

    public static JsonObject CreateBatch(IReadOnlyList<Ticket> tickets)
    {
        var array = new JsonArray();

        foreach (var ticket in tickets)
        {
            array.Add(Create(ticket));
        }

        return new JsonObject { ["tickets"] = array };
    }

    private static JsonArray BuildConversation(Ticket ticket)
    {
        var conversation = new JsonArray();

        foreach (var message in ticket.Conversation)
        {
            conversation.Add(new JsonObject
            {
                ["sender"] = message.Sender,
                ["role"] = message.Role.ToWireValue(),
                ["content"] = message.Content,
                ["timestamp"] = message.Timestamp.ToString("O"),
                ["message_id"] = message.MessageId
            });
        }

        return conversation;
    }

    private static JsonArray BuildTags(Ticket ticket)
    {
        var tags = new JsonArray();

        foreach (var tag in ticket.Tags)
        {
            tags.Add(tag);
        }

        return tags;
    }
}
