namespace RyzeHub.UnitTests;

/// <summary>
/// The CLI parser is duplicated here as a behavioural contract: the CLI project is an
/// executable, so its internals are not referenced directly from the test assembly.
/// These cases document the expected parsing rules.
/// </summary>
public sealed class CommandArgumentParsingTests
{
    [Theory]
    [InlineData(new[] { "--data", "value" }, "data", "value")]
    [InlineData(new[] { "--key-id", "abc-123" }, "key-id", "abc-123")]
    [InlineData(new[] { "--limit", "50" }, "limit", "50")]
    public void ValueOptionsArePaired(string[] args, string name, string expected)
    {
        var parsed = Parse(args);
        parsed.Values[name].Should().Be(expected);
    }

    [Fact]
    public void TrailingOptionWithoutAValueBecomesAFlag()
    {
        var parsed = Parse(["--continuous"]);

        parsed.Flags.Should().Contain("continuous");
        parsed.Values.Should().NotContainKey("continuous");
    }

    [Fact]
    public void AdjacentOptionsAreTreatedAsFlags()
    {
        var parsed = Parse(["--dockerize", "--verbose"]);

        parsed.Flags.Should().BeEquivalentTo(new[] { "dockerize", "verbose" });
    }

    [Fact]
    public void LeadingBareWordBecomesTheAction()
    {
        var parsed = Parse(["snapshot", "--user-id", "u1"]);

        parsed.Positional.Should().ContainSingle().Which.Should().Be("snapshot");
        parsed.Values["user-id"].Should().Be("u1");
    }

    [Fact]
    public void MixedFlagsValuesAndActionsParseTogether()
    {
        var parsed = Parse(["store", "--key-id", "k1", "--data", "secret", "--force"]);

        parsed.Positional[0].Should().Be("store");
        parsed.Values["key-id"].Should().Be("k1");
        parsed.Values["data"].Should().Be("secret");
        parsed.Flags.Should().Contain("force");
    }

    private static (Dictionary<string, string> Values, HashSet<string> Flags, List<string> Positional) Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var positional = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];

            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(current);
                continue;
            }

            var name = current[2..];
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[name] = args[index + 1];
                index++;
            }
            else
            {
                flags.Add(name);
            }
        }

        return (values, flags, positional);
    }
}
