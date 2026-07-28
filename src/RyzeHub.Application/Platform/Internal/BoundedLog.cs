namespace RyzeHub.Application.Platform.Internal;

/// <summary>
/// Append-only, size-bounded, thread-safe log with newest-first reads.
/// Shared by every hub store that keeps a rolling history (events, notifications, audit, ...).
/// </summary>
internal sealed class BoundedLog<T>(int maxCount = 0)
{
    private readonly List<T> _items = [];
    private readonly Lock _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public void Append(T item)
    {
        lock (_gate)
        {
            _items.Add(item);

            if (maxCount > 0 && _items.Count > maxCount)
            {
                _items.RemoveRange(0, _items.Count - maxCount);
            }
        }
    }

    /// <summary>Returns up to <paramref name="limit"/> most recent matching entries, newest first.</summary>
    public IReadOnlyList<T> Recent(int limit, Func<T, bool>? predicate = null)
    {
        if (limit <= 0)
        {
            return [];
        }

        lock (_gate)
        {
            var result = new List<T>(Math.Min(limit, _items.Count));

            for (var index = _items.Count - 1; index >= 0 && result.Count < limit; index--)
            {
                if (predicate is null || predicate(_items[index]))
                {
                    result.Add(_items[index]);
                }
            }

            return result;
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
        {
            return [.. _items];
        }
    }
}
