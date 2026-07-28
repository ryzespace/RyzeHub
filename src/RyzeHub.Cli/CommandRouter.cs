using RyzeHub.Cli.Commands;

namespace RyzeHub.Cli;

/// <summary>Dispatches a verb to its <see cref="ICommandHandler"/> and renders help.</summary>
internal static class CommandRouter
{
    private static readonly ICommandHandler[] Handlers =
    [
        new RunCommand(),
        new HealthCommand(),
        new ValidateCommand(),
        new HubCommand(),
        new DepsCommand(),
        new DockerCommand(),
        new EncryptCommand(),
        new DecryptCommand(),
        new SignCommand(),
        new VerifyCommand(),
        new VaultCommand(),
        new ErrorsCommand(),
        new PlatformCommand(),
        new AuthCommand()
    ];

    public static async Task<int> ExecuteAsync(
        string[] args,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return ExitCodes.UsageError;
        }

        if (args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return ExitCodes.Success;
        }

        var verb = args[0];
        var handler = Handlers.FirstOrDefault(
            candidate => string.Equals(candidate.Name, verb, StringComparison.OrdinalIgnoreCase));

        if (handler is null)
        {
            ConsoleOutput.WriteError($"Unknown command: {verb}");
            PrintUsage();
            return ExitCodes.UsageError;
        }

        try
        {
            return await handler.ExecuteAsync(provider, new CommandArguments(args[1..]), cancellationToken);
        }
        catch (CommandUsageException exception)
        {
            ConsoleOutput.WriteError($"Error: {exception.Message}");
            ConsoleOutput.WriteError($"Usage: ryzehub {handler.Usage}");
            return ExitCodes.UsageError;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("RyzeHub CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: ryzehub <command> [action] [--option value] [--flag]");
        Console.WriteLine();
        Console.WriteLine("Commands:");

        var width = Handlers.Max(handler => handler.Usage.Length) + 2;
        foreach (var handler in Handlers)
        {
            Console.WriteLine($"  {handler.Usage.PadRight(width)}{handler.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ryzehub run --continuous");
        Console.WriteLine("  ryzehub platform snapshot --seed-demo-user user-001");
        Console.WriteLine("  ryzehub auth introspect-key --api-key rk_live_... --scope hub:tickets:transfer");
    }
}
