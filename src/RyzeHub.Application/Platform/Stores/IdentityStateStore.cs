using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform.Stores;

public interface IIdentityStateStore
{
    DeviceRecord RegisterDevice(string userId, string deviceName, ClientPlatform platform, bool trusted, DateTimeOffset now);

    void TrustDevice(string deviceId, bool trusted, DateTimeOffset now);

    void RevokeDevice(string deviceId, DateTimeOffset now);

    IReadOnlyList<DeviceRecord> Devices(string? userId);

    SessionRecord CreateSession(string userId, string deviceId, ClientPlatform platform, string ipAddress, DateTimeOffset now);

    void TerminateSession(string sessionId, DateTimeOffset now);

    IReadOnlyList<SessionRecord> Sessions(string? userId);

    void UpdatePresence(string userId, PresenceStatus status, string? deviceId, DateTimeOffset now);

    IReadOnlyList<PresenceRecord> Presence(string? userId);

    int DeviceCount { get; }

    int SessionCount { get; }

    int PresenceCount { get; }

    int ActiveSessionCount { get; }

    long ActiveUserCount { get; }
}

/// <summary>
/// Presence System, Device Management and Session Manager.
/// Mirrors the Keycloak session state that RyzeAuth owns.
/// </summary>
public sealed class IdentityStateStore : IIdentityStateStore
{
    private readonly Dictionary<string, DeviceRecord> _devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PresenceRecord> _presence = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public int DeviceCount
    {
        get { lock (_gate) { return _devices.Count; } }
    }

    public int SessionCount
    {
        get { lock (_gate) { return _sessions.Count; } }
    }

    public int PresenceCount
    {
        get { lock (_gate) { return _presence.Count; } }
    }

    public int ActiveSessionCount
    {
        get { lock (_gate) { return _sessions.Values.Count(session => session.Active); } }
    }

    public long ActiveUserCount
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Values
                    .Where(session => session.Active)
                    .Select(session => session.UserId)
                    .Distinct(StringComparer.Ordinal)
                    .Count();
            }
        }
    }

    public DeviceRecord RegisterDevice(
        string userId,
        string deviceName,
        ClientPlatform platform,
        bool trusted,
        DateTimeOffset now)
    {
        var device = new DeviceRecord(
            Guid.NewGuid().ToString(),
            userId,
            platform,
            deviceName,
            trusted,
            Active: true,
            now,
            now);

        lock (_gate)
        {
            _devices[device.DeviceId] = device;
        }

        return device;
    }

    public void TrustDevice(string deviceId, bool trusted, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var device))
            {
                _devices[deviceId] = device with { Trusted = trusted, LastSeenAt = now };
            }
        }
    }

    public void RevokeDevice(string deviceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_devices.TryGetValue(deviceId, out var device))
            {
                _devices[deviceId] = device with { Active = false, LastSeenAt = now };
            }
        }
    }

    public IReadOnlyList<DeviceRecord> Devices(string? userId)
    {
        lock (_gate)
        {
            return
            [
                .. _devices.Values
                    .Where(record => userId is null || record.UserId == userId)
                    .OrderByDescending(record => record.LastSeenAt)
            ];
        }
    }

    public SessionRecord CreateSession(
        string userId,
        string deviceId,
        ClientPlatform platform,
        string ipAddress,
        DateTimeOffset now)
    {
        var session = new SessionRecord(
            Guid.NewGuid().ToString(),
            userId,
            deviceId,
            platform,
            ipAddress,
            Active: true,
            now,
            now);

        lock (_gate)
        {
            _sessions[session.SessionId] = session;
        }

        return session;
    }

    public void TerminateSession(string sessionId, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                _sessions[sessionId] = session with { Active = false, LastSeenAt = now };
            }
        }
    }

    public IReadOnlyList<SessionRecord> Sessions(string? userId)
    {
        lock (_gate)
        {
            return
            [
                .. _sessions.Values
                    .Where(record => userId is null || record.UserId == userId)
                    .OrderByDescending(record => record.LastSeenAt)
            ];
        }
    }

    public void UpdatePresence(string userId, PresenceStatus status, string? deviceId, DateTimeOffset now)
    {
        lock (_gate)
        {
            List<string> devices = _presence.TryGetValue(userId, out var existing)
                ? [.. existing.ActiveDevices]
                : [];

            if (deviceId is not null && !devices.Contains(deviceId, StringComparer.Ordinal))
            {
                devices.Add(deviceId);
            }

            _presence[userId] = new PresenceRecord(userId, status, now, devices);
        }
    }

    public IReadOnlyList<PresenceRecord> Presence(string? userId)
    {
        lock (_gate)
        {
            return
            [
                .. _presence.Values
                    .Where(record => userId is null || record.UserId == userId)
                    .OrderByDescending(record => record.LastActivityAt)
            ];
        }
    }
}
