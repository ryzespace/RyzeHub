using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RyzeHub.Application.Configuration;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application.Pipeline;

public interface ITicketTransferService
{
    Task<IReadOnlyList<TransferResult>> TransferAsync(IReadOnlyList<Ticket> tickets, CancellationToken cancellationToken);
}

/// <summary>
/// Delivers tickets to the destination with per-ticket exponential backoff.
/// Isolated from the orchestrator so the retry policy can be tested on its own.
/// </summary>
public sealed class TicketTransferService(
    ILogger<TicketTransferService> logger,
    IDestinationTicketClient destination,
    IOptions<DestinationOptions> options,
    ISystemClock clock) : ITicketTransferService
{
    private readonly DestinationOptions _options = options.Value;

    public async Task<IReadOnlyList<TransferResult>> TransferAsync(
        IReadOnlyList<Ticket> tickets,
        CancellationToken cancellationToken)
    {
        var results = new List<TransferResult>(tickets.Count);

        foreach (var ticket in tickets)
        {
            results.Add(await TransferOneAsync(ticket, cancellationToken));
        }

        return results;
    }

    private async Task<TransferResult> TransferOneAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxRetries);
        var lastError = string.Empty;
        var attempts = 0;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            attempts = attempt + 1;

            try
            {
                var helpCenterId = await destination.CreateTicketAsync(ticket, cancellationToken);
                return new TransferResult(ticket.TicketId, true, helpCenterId, null, clock.UtcNow, attempts);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                lastError = exception.Message;
                logger.LogWarning(
                    "Transfer attempt {Attempt}/{MaxAttempts} failed for ticket {TicketId}: {Error}",
                    attempts,
                    maxAttempts,
                    ticket.TicketId,
                    lastError);

                if (attempts >= maxAttempts)
                {
                    break;
                }

                var delaySeconds = _options.RetryDelaySeconds * Math.Pow(2, attempt);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        return new TransferResult(ticket.TicketId, false, null, lastError, clock.UtcNow, attempts);
    }
}
