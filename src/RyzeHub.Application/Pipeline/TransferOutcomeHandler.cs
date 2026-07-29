using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using RyzeHub.Domain.Platform;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application.Pipeline;

public interface ITransferOutcomeHandler
{
    Task OnSuccessAsync(TransferResult result, IReadOnlyList<Ticket> tickets, CancellationToken cancellationToken);

    void OnFailure(TransferResult result, long transferMs);
}

/// <summary>
/// Side effects that follow a transfer: marking the source, audit, hub events and
/// forwarding the record to the RyzeAuth audit trail.
/// </summary>
public sealed class TransferOutcomeHandler(
    ILogger<TransferOutcomeHandler> logger,
    ISourceTicketClient source,
    IAuditLogger audit,
    IHubPlatform hub,
    IErrorDetectionEngine errorDetection,
    ISystemClock clock,
    IRyzeAuthClient? ryzeAuth = null) : ITransferOutcomeHandler
{
    public async Task OnSuccessAsync(
        TransferResult result,
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken)
    {
        audit.LogTransfer(result.TicketId, "client_dashboard", result.HelpCenterTicketId ?? "unknown");

        await source.MarkAsTransferredAsync(
            result.TicketId,
            result.HelpCenterTicketId ?? string.Empty,
            cancellationToken);

        var ticket = tickets.FirstOrDefault(candidate => candidate.TicketId == result.TicketId);
        if (ticket is null)
        {
            return;
        }

        PublishHubEvents(ticket, result);
        await ForwardAuditAsync(ticket, result, cancellationToken);
    }

    private void PublishHubEvents(Ticket ticket, TransferResult result)
    {
        var userId = string.IsNullOrEmpty(ticket.ClientId) ? "unknown-user" : ticket.ClientId;

        hub.UpdatePresence(userId, PresenceStatus.Online, $"ticket-{ticket.TicketId}");

        hub.RecordSupportStatusUpdate(
            userId,
            ticket.TicketId,
            "transferred",
            [NotificationChannel.MobilePush, NotificationChannel.Desktop, NotificationChannel.Email]);

        hub.RecordActivity(
            userId,
            $"Przeniesiono zgłoszenie {ticket.TicketId} do HelpCenter",
            new JsonObject
            {
                ["ticket_id"] = ticket.TicketId,
                ["helpcenter_ticket_id"] = result.HelpCenterTicketId
            });
    }

    private async Task ForwardAuditAsync(Ticket ticket, TransferResult result, CancellationToken cancellationToken)
    {
        if (ryzeAuth is null)
        {
            return;
        }

        try
        {
            await ryzeAuth.ForwardAuditEventAsync(
                new RyzeAuthAuditEvent(
                    "ryzehub.ticket_transferred",
                    "success",
                    clock.UtcNow,
                    ticket.ClientId,
                    ticket.OrganizationId,
                    ticket.TicketId,
                    new JsonObject
                    {
                        ["ticket_id"] = ticket.TicketId,
                        ["helpcenter_ticket_id"] = result.HelpCenterTicketId,
                        ["category"] = ticket.Category,
                        ["priority"] = ticket.Priority.ToWireValue()
                    }),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to forward transfer audit for {TicketId}", ticket.TicketId);
        }
    }

    public void OnFailure(TransferResult result, long transferMs)
    {
        if (result.ErrorMessage is not { } error)
        {
            return;
        }

        errorDetection.DetectError(error, "pipeline.transfer");
        hub.UpdateServiceHealth("RyzeSpace.HelpCenter", healthy: false, latencyMs: transferMs);
        hub.RecordSecurityWarning(
            userId: null,
            $"Błąd transferu zgłoszenia {result.TicketId}: {error}",
            [NotificationChannel.SlackWebhook, NotificationChannel.DiscordWebhook]);
    }
}
