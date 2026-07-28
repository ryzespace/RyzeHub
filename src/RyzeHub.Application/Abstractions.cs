using System.Text.Json.Nodes;
using RyzeHub.Domain.Diagnostics;
using RyzeHub.Domain.Platform;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ISourceTicketClient
{
    Task<IReadOnlyList<Ticket>> FetchTicketsAsync(IReadOnlyList<TicketStatus>? statuses, int page, CancellationToken cancellationToken);

    Task<IReadOnlyList<Ticket>> FetchAllPagesAsync(IReadOnlyList<TicketStatus>? statuses, CancellationToken cancellationToken);

    Task<Ticket> FetchTicketByIdAsync(string ticketId, CancellationToken cancellationToken);

    Task<bool> MarkAsTransferredAsync(string ticketId, string helpCenterTicketId, CancellationToken cancellationToken);

    Task<int> GetTotalCountAsync(IReadOnlyList<TicketStatus>? statuses, CancellationToken cancellationToken);

    Task<(bool Healthy, TimeSpan Latency)> HealthCheckAsync(CancellationToken cancellationToken);
}

public interface IDestinationTicketClient
{
    Task<string> CreateTicketAsync(Ticket ticket, CancellationToken cancellationToken);

    Task<string?> CheckTicketExistsAsync(string sourceTicketId, CancellationToken cancellationToken);

    Task<BatchTransferResult> BatchCreateTicketsAsync(IReadOnlyList<Ticket> tickets, CancellationToken cancellationToken);

    Task<(bool Healthy, TimeSpan Latency)> HealthCheckAsync(CancellationToken cancellationToken);
}

public interface ICryptoEngine
{
    EncryptedPacket Encrypt(ReadOnlySpan<byte> plaintext);

    byte[] Decrypt(EncryptedPacket packet);

    string EncryptToEnvelope(string plaintext);

    string DecryptFromEnvelope(string envelope);

    string CurrentKeyId { get; }

    bool NeedsRotation { get; }

    string RotateKey(string newKeyBase64);
}

public interface ISignatureEngine
{
    string KeyId { get; }

    SignatureResult Sign(ReadOnlySpan<byte> data);

    bool Verify(ReadOnlySpan<byte> data, string signature);

    SignatureResult SignTicket(string ticketId, string description, string ticketType);

    bool VerifyTicketSignature(string ticketId, string description, string ticketType, string signature);

    SignatureResult SignRequest(string method, string path, string body, string timestamp);

    bool VerifyRequestSignature(string method, string path, string body, string timestamp, string signature);
}

public interface IKeyManager
{
    string CurrentKeyId { get; }

    string DeriveKey(KeyPurpose purpose, string info);

    ManagedKey? GetKey(string keyId);

    ManagedKey CurrentKey { get; }

    string RotateMasterKey(string newMasterKeyBase64);

    string GenerateSessionKey();

    int CleanExpiredKeys();

    bool NeedsRotation { get; }

    IReadOnlyList<KeyMetadata> ExportMetadata();
}

public interface ISecureVault
{
    Task StoreKeyAsync(string keyId, byte[] keyData, string name, string description, IReadOnlyList<string> tags, CancellationToken cancellationToken);

    Task<byte[]> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken);

    Task DeleteKeyAsync(string keyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken);

    Task<VaultMetadata?> GetKeyMetadataAsync(string keyId, CancellationToken cancellationToken);

    Task<string> IntegrityHashAsync(CancellationToken cancellationToken);
}

public interface IErrorDetectionEngine
{
    DetectedError? DetectError(string message, string source);

    void AddPattern(ErrorPattern pattern);

    void SetAnomalyThreshold(string metricName, double threshold);

    void RecordMetric(string metricName, double value);

    IReadOnlyList<AnomalyResult> DetectAnomalies();

    IReadOnlyDictionary<ErrorCategory, long> GetStatistics();

    IReadOnlyList<DetectedError> GetRecentErrors(int limit);

    IReadOnlyList<ErrorPattern> GetPatternStats();

    IReadOnlyList<IReadOnlyList<DetectedError>> CorrelateErrors(TimeSpan window);

    IReadOnlyList<string> PredictIssues();
}

public interface IAnomalyDetector
{
    void TrackMetric(string metricName, int maxHistory);

    void Record(string metricName, double value);

    IReadOnlyList<AdvancedAnomaly> Detect();

    IReadOnlyList<AdvancedAnomaly> GetRecentAlerts(int limit);

    void ClearAlerts();
}

public interface IHubPlatform
{
    HubPlatformSnapshot Snapshot();

    HubHealthStatus HealthStatus();

    IReadOnlyList<PlatformModuleDefinition> ListModuleCatalog();

    IReadOnlyList<RealtimeEvent> ListEvents(int limit);

    IReadOnlyList<NotificationEndpoint> ListNotificationEndpoints();

    IReadOnlyList<NotificationRecord> ListNotifications(string? userId, int limit);

    IReadOnlyList<AuditLogEntry> ListAuditLogs(string? actorId, int limit);

    IReadOnlyList<RoleDefinition> ListRoles();

    UserAccessProfile AccessProfile(string userId);

    IReadOnlyList<PresenceRecord> ListPresence(string? userId);

    IReadOnlyList<DeviceRecord> ListDevices(string? userId);

    IReadOnlyList<SessionRecord> ListSessions(string? userId);

    IReadOnlyList<GatewayRoute> ListGatewayRoutes();

    IReadOnlyList<CacheEntry> ListCacheEntries();

    IReadOnlyList<ActivityFeedEntry> ListActivityFeed(string? userId, int limit);

    IReadOnlyList<InternalMessage> ListMessages(string? userId, int limit);

    IReadOnlyList<FeatureFlag> ListFeatureFlags();

    IReadOnlyList<MonitoredService> ListServices();

    IReadOnlyList<SecurityAlert> ListSecurityAlerts(string? userId, int limit);

    IReadOnlyList<FileTransferRecord> ListFileTransfers(string? ownerId, int limit);

    IReadOnlyList<EventSubscription> ListSubscriptions();

    NotificationRecord SendNotification(
        string userId,
        string title,
        string message,
        IReadOnlyList<NotificationChannel> channels,
        NotificationPriority priority,
        JsonNode? metadata);

    void SetNotificationEndpoint(NotificationChannel channel, string target, bool enabled);

    HubPlatformSnapshot SeedDemoData(string userId);

    void RecordSupportStatusUpdate(string userId, string ticketId, string status, IReadOnlyList<NotificationChannel> channels);

    void RecordPaymentCompleted(string userId, string paymentId, decimal amount, IReadOnlyList<NotificationChannel> channels);

    void RecordServerActivated(string userId, string serverId, IReadOnlyList<NotificationChannel> channels);

    void RecordSecurityWarning(string? userId, string description, IReadOnlyList<NotificationChannel> channels);

    void RecordLogin(string userId, string ipAddress, string? deviceId);

    void AssignRole(string userId, string roleName);

    void GrantPermission(string userId, string permission);

    IReadOnlyList<string> PermissionsForUser(string userId);

    bool HasPermission(string userId, string permission);

    string RegisterDevice(string userId, string deviceName, ClientPlatform platform, bool trusted);

    void TrustDevice(string deviceId, bool trusted);

    void RevokeDevice(string deviceId);

    string CreateSession(string userId, string deviceId, ClientPlatform platform, string ipAddress);

    void TerminateSession(string sessionId);

    void UpdatePresence(string userId, PresenceStatus status, string? deviceId);

    void PutCache(string key, JsonNode? value);

    JsonNode? CacheGet(string key);

    void RecordActivity(string userId, string description, JsonNode? metadata);

    void RecordInternalMessage(string fromUser, string toUser, string body);

    void SetFeatureFlag(string key, bool enabled, string description);

    void UpdateServiceHealth(string service, bool healthy, long latencyMs);

    void RecordFileTransfer(string ownerId, string fileName, bool scanned, bool encrypted, bool versioned);

    void RecordAudit(string actorId, string action, AuditCategory category, string resource, JsonNode? metadata);
}

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

public interface ITicketPipeline
{
    Task<PipelineRunMetrics> RunOnceAsync(CancellationToken cancellationToken);

    Task RunContinuousAsync(CancellationToken cancellationToken);

    Task<HealthStatus> HealthCheckAsync(CancellationToken cancellationToken);

    HubPlatformSnapshot HubSnapshot();

    IHubPlatform Hub { get; }

    HubPlatformSnapshot SeedHubDemo(string userId);
}

public sealed record PipelineRunMetrics
{
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public double DurationSeconds { get; set; }
    public int FetchedCount { get; set; }
    public int FilteredCount { get; set; }
    public int ValidCount { get; set; }
    public int TransferredCount { get; set; }
    public int FailedCount { get; set; }
    public List<string> Errors { get; init; } = [];

    public void Finish(DateTimeOffset now)
    {
        FinishedAt = now;
        DurationSeconds = (now - StartedAt).TotalSeconds;
    }
}

public sealed record EncryptionContext(
    byte Version,
    string Algorithm,
    string KeyId,
    byte[] Nonce,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyDictionary<string, string> Metadata)
{
    public bool IsExpired => ExpiresAt is { } expiry && DateTimeOffset.UtcNow > expiry;
}

public sealed record EncryptedPacket(EncryptionContext Context, byte[] Ciphertext, byte[] AuthTag, string Checksum);

public sealed record SignatureResult(string Signature, string Algorithm, string KeyId, string Timestamp);

public enum KeyPurpose
{
    Master,
    Encryption,
    Signing,
    KeyDerivation,
    Session
}

public sealed record KeyMetadata(
    string KeyId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string Algorithm,
    KeyPurpose Purpose,
    string? ParentKeyId,
    int Version);

public sealed record ManagedKey(byte[] PublicKey, byte[] PrivateKey, KeyMetadata Metadata);

public sealed record AccessControlList
{
    public IReadOnlyList<string> Readers { get; init; } = [];
    public IReadOnlyList<string> Writers { get; init; } = [];
    public IReadOnlyList<string> Admins { get; init; } = [];

    public bool CanRead(string user) =>
        Readers.Contains(user, StringComparer.Ordinal)
        || Writers.Contains(user, StringComparer.Ordinal)
        || Admins.Contains(user, StringComparer.Ordinal);

    public bool CanWrite(string user) =>
        Writers.Contains(user, StringComparer.Ordinal) || Admins.Contains(user, StringComparer.Ordinal);

    public bool CanAdmin(string user) => Admins.Contains(user, StringComparer.Ordinal);
}

public sealed record VaultMetadata(string Name, string Description, IReadOnlyList<string> Tags, AccessControlList Acl);

public sealed record VaultEntry(
    string KeyId,
    byte[] EncryptedData,
    VaultMetadata Metadata,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastAccessed,
    long AccessCount);
