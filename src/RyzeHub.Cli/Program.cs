using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RyzeHub.Cli;
using RyzeHub.Infrastructure;
using Serilog;

Console.OutputEncoding = Encoding.UTF8;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddDotEnvFile()
    .AddRyzeHubEnvironmentVariables()
    .AddEnvironmentVariables("RYZEHUB_")
    .Build();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Is(ParseLogLevel(configuration["Pipeline:LogLevel"]))
    .WriteTo.Console(new Serilog.Formatting.Compact.RenderedCompactJsonFormatter())
    .CreateLogger();

var services = new ServiceCollection();
services.AddLogging(logging => logging.ClearProviders().AddSerilog(Log.Logger, dispose: true));
services.AddRyzeHub(configuration);

await using var provider = services.BuildServiceProvider();

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    return await CommandRouter.ExecuteAsync(args, provider, cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception exception)
{
    Log.Fatal(exception, "RyzeHub CLI failed");
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static Serilog.Events.LogEventLevel ParseLogLevel(string? value) => value?.ToLowerInvariant() switch
{
    "trace" or "verbose" => Serilog.Events.LogEventLevel.Verbose,
    "debug" => Serilog.Events.LogEventLevel.Debug,
    "warn" or "warning" => Serilog.Events.LogEventLevel.Warning,
    "error" => Serilog.Events.LogEventLevel.Error,
    _ => Serilog.Events.LogEventLevel.Information
};
