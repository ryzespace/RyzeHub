using System.Text.Json.Nodes;

namespace RyzeHub.Application;

/// <summary>
/// Contract for the RyzeAuth control plane: token/API-key introspection, RBAC projection and audit forwarding.
/// </summary>
public interface IRyzeAuthClient
{
    Task<ApiKeyIntrospectionResult> IntrospectApiKeyAsync(string apiKey, string requiredScope, CancellationToken cancellationToken);

    Task<TokenIntrospectionResult> IntrospectTokenAsync(string token, CancellationToken cancellationToken);

    Task ForwardAuditEventAsync(RyzeAuthAuditEvent auditEvent, CancellationToken cancellationToken);

    Task<(bool Healthy, TimeSpan Latency, bool JwksReachable, bool GrpcReachable)> HealthCheckAsync(CancellationToken cancellationToken);
}

public sealed record ApiKeyIntrospectionResult(
    bool Active,
    string? OrganizationId,
    IReadOnlyList<string> Scopes,
    string? KeyId,
    string? Reason);

public sealed record TokenIntrospectionResult(
    bool Active,
    string? Subject,
    string? PreferredUsername,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Scopes,
    string? OrganizationId);

public sealed record RyzeAuthAuditEvent(
    string EventType,
    string Outcome,
    DateTimeOffset OccurredAt,
    string? SubjectId,
    string? OrganizationId,
    string? CorrelationId,
    JsonNode? Metadata);
