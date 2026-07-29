using System.Text.Json;
using System.Text.Json.Nodes;

namespace RyzeHub.Cli;

/// <summary>Shared JSON console output so every command emits the same machine-readable shape.</summary>
internal static class ConsoleOutput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static void WriteJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    public static void WriteError(string message) => Console.Error.WriteLine(message);

    /// <summary>Parses a CLI argument as JSON, falling back to a plain string value.</summary>
    public static JsonNode? ParseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(value);
        }
        catch (JsonException)
        {
            return JsonValue.Create(value);
        }
    }
}
