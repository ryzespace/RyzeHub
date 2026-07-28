namespace RyzeHub.Cli.Commands;

/// <summary>A single top-level CLI verb.</summary>
internal interface ICommandHandler
{
    /// <summary>Verb that selects this handler, e.g. <c>platform</c>.</summary>
    string Name { get; }

    /// <summary>One-line usage text shown by <c>ryzehub --help</c>.</summary>
    string Usage { get; }

    string Description { get; }

    Task<int> ExecuteAsync(IServiceProvider provider, CommandArguments arguments, CancellationToken cancellationToken);
}
