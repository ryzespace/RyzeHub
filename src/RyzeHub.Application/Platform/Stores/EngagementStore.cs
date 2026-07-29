using System.Text.Json.Nodes;
using RyzeHub.Application.Platform.Internal;
using RyzeHub.Domain.Platform;

namespace RyzeHub.Application.Platform.Stores;

public interface IEngagementStore
{
    void RecordActivity(string userId, string description, JsonNode? metadata, DateTimeOffset now);

    IReadOnlyList<ActivityFeedEntry> Activity(string? userId, int limit);

    InternalMessage RecordMessage(string fromUser, string toUser, string body, DateTimeOffset now);

    IReadOnlyList<InternalMessage> Messages(string? userId, int limit);

    FileTransferRecord RecordFileTransfer(
        string ownerId,
        string fileName,
        bool scanned,
        bool encrypted,
        bool versioned,
        DateTimeOffset now);

    IReadOnlyList<FileTransferRecord> FileTransfers(string? ownerId, int limit);

    int ActivityCount { get; }

    int MessageCount { get; }

    int FileTransferCount { get; }
}

/// <summary>Activity Feed, Internal Messaging and File Transfer Service.</summary>
public sealed class EngagementStore : IEngagementStore
{
    private readonly BoundedLog<ActivityFeedEntry> _activity = new();
    private readonly BoundedLog<InternalMessage> _messages = new();
    private readonly BoundedLog<FileTransferRecord> _fileTransfers = new();

    public int ActivityCount => _activity.Count;

    public int MessageCount => _messages.Count;

    public int FileTransferCount => _fileTransfers.Count;

    public void RecordActivity(string userId, string description, JsonNode? metadata, DateTimeOffset now) =>
        _activity.Append(new ActivityFeedEntry(
            Guid.NewGuid().ToString(),
            userId,
            description,
            now,
            metadata));

    public IReadOnlyList<ActivityFeedEntry> Activity(string? userId, int limit) =>
        _activity.Recent(limit, entry => userId is null || entry.UserId == userId);

    public InternalMessage RecordMessage(string fromUser, string toUser, string body, DateTimeOffset now)
    {
        var message = new InternalMessage(
            Guid.NewGuid().ToString(),
            $"{fromUser}:{toUser}",
            fromUser,
            toUser,
            body,
            now);

        _messages.Append(message);
        return message;
    }

    public IReadOnlyList<InternalMessage> Messages(string? userId, int limit) =>
        _messages.Recent(limit, message =>
            userId is null || message.FromUser == userId || message.ToUser == userId);

    public FileTransferRecord RecordFileTransfer(
        string ownerId,
        string fileName,
        bool scanned,
        bool encrypted,
        bool versioned,
        DateTimeOffset now)
    {
        var record = new FileTransferRecord(
            Guid.NewGuid().ToString(),
            ownerId,
            fileName,
            encrypted,
            scanned,
            versioned,
            now);

        _fileTransfers.Append(record);
        return record;
    }

    public IReadOnlyList<FileTransferRecord> FileTransfers(string? ownerId, int limit) =>
        _fileTransfers.Recent(limit, record => ownerId is null || record.OwnerId == ownerId);
}
