namespace RyzeHub.Cli;

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

    public string? GetValue(string name) => _values.GetValueOrDefault(name);

    public bool HasFlag(string name) =>
        _flags.Contains(name)
        || (_values.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed) && parsed);
}
