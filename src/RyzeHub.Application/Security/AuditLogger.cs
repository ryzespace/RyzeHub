using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;

namespace RyzeHub.Application;

public interface IAuditLogger
{
    void Log(string action, string ticketId, string user, string details);

    void LogTransfer(string ticketId, string source, string destination);

    void LogValidationError(string ticketId, IReadOnlyList<string> errors);
}

public sealed record AuditEntry(
    string Timestamp,
    string Action,
    string TicketId,
    string User,
    string Details,
    string? IpAddress);

/// <summary>
/// Structured audit trail. When RyzeAuth forwarding is enabled every entry is mirrored
/// into the immutable RyzeAuth security audit table.
/// </summary>
public sealed class AuditLogger(
    ILogger<AuditLogger> logger,
    IOptions<SecurityOptions> securityOptions,
    IOptions<RyzeAuthOptions> authOptions,
    ISystemClock clock,
    IRyzeAuthClient? ryzeAuth = null) : IAuditLogger
{
    private readonly SecurityOptions _security = securityOptions.Value;
    private readonly RyzeAuthOptions _auth = authOptions.Value;

    public void Log(string action, string ticketId, string user, string details)
    {
        if (!_security.AuditLogEnabled)
        {
            return;
        }

        var entry = new AuditEntry(
            clock.UtcNow.ToString("O"),
            action,
            ticketId,
            user,
            details,
            IpAddress: null);

        logger.LogInformation("AUDIT: {AuditEntry}", JsonSerializer.Serialize(entry));

        if (ryzeAuth is null || !_auth.ForwardAuditEvents)
        {
            return;
        }

        var metadata = new JsonObject
        {
            ["ticket_id"] = ticketId,
            ["details"] = details,
            ["component"] = "RyzeHub"
        };

        // Fire-and-forget: audit forwarding must never block or fail a pipeline run.
        _ = ForwardAsync(new RyzeAuthAuditEvent(
            EventType: $"ryzehub.{action}",
            Outcome: "success",
            OccurredAt: clock.UtcNow,
            SubjectId: user,
            OrganizationId: null,
            CorrelationId: ticketId,
            Metadata: metadata));
    }

    public void LogTransfer(string ticketId, string source, string destination) =>
        Log("ticket_transfer", ticketId, "system", $"Transferred from {source} to {destination}");

    public void LogValidationError(string ticketId, IReadOnlyList<string> errors) =>
        Log("validation_error", ticketId, "system", $"Validation failed: {string.Join("; ", errors)}");

    private async Task ForwardAsync(RyzeAuthAuditEvent auditEvent)
    {
        try
        {
            await ryzeAuth!.ForwardAuditEventAsync(auditEvent, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to forward audit event {EventType} to RyzeAuth", auditEvent.EventType);
        }
    }
}
