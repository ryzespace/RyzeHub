using System.Globalization;

namespace RyzeHub.Cli;

/// <summary>Raised when the user supplies missing or malformed arguments.</summary>
internal sealed class CommandUsageException(string message) : Exception(message);

/// <summary>Minimal <c>--key value</c> / <c>--flag</c> argument parser.</summary>
internal sealed class CommandArguments
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = [];

    public CommandArguments(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];

            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                _positional.Add(current);
                continue;
            }

            var name = current[2..];
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                _values[name] = args[index + 1];
                index++;
            }
            else
            {
                _flags.Add(name);
            }
        }
    }

    public IReadOnlyList<string> Positional => _positional;

    /// <summary>First positional argument, lower-cased, or <paramref name="fallback"/> when absent.</summary>
    public string Action(string fallback) =>
        (_positional.Count > 0 ? _positional[0] : fallback).ToLowerInvariant();

    public string? GetValue(string name) => _values.GetValueOrDefault(name);

    public string RequireValue(string name) =>
        _values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new CommandUsageException($"--{name} is required");

    /// <summary>Splits a comma-separated option into trimmed, non-empty parts.</summary>
    public IReadOnlyList<string> GetValues(string name) =>
        _values.TryGetValue(name, out var value)
            ? [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : [];

    public int GetInt32(string name, int fallback) =>
        _values.TryGetValue(name, out var value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public long GetInt64(string name, long fallback) =>
        _values.TryGetValue(name, out var value)
        && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    public bool HasFlag(string name) =>
        _flags.Contains(name)
        || (_values.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed) && parsed);
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int UsageError = 2;
    public const int Cancelled = 130;
}
