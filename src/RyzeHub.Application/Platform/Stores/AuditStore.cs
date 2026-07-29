using System.Text.Json.Nodes;
using RyzeHub.Application.Platform.Internal;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform.Stores;

public interface IAuditStore
{
    void Record(string actorId, string action, AuditCategory category, string resource, JsonNode? metadata, DateTimeOffset now);

    IReadOnlyList<AuditLogEntry> Recent(string? actorId, int limit);

    int Count { get; }
}

/// <summary>Audit Log Engine: logins, settings, admin actions, financial operations, permission changes.</summary>
public sealed class AuditStore : IAuditStore
{
    private readonly BoundedLog<AuditLogEntry> _entries = new();

    public int Count => _entries.Count;

    public void Record(
        string actorId,
        string action,
        AuditCategory category,
        string resource,
        JsonNode? metadata,
        DateTimeOffset now) =>
        _entries.Append(new AuditLogEntry(
            Guid.NewGuid().ToString(),
            actorId,
            action,
            category,
            resource,
            metadata,
            now));

    public IReadOnlyList<AuditLogEntry> Recent(string? actorId, int limit) =>
        _entries.Recent(limit, entry => actorId is null || entry.ActorId == actorId);
}
