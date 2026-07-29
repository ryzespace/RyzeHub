using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application;

public interface ISourceTicketClient
{
    Task<IReadOnlyList<Ticket>> FetchTicketsAsync(IReadOnlyList<TicketStatus>? statuses, int page, CancellationToken cancellationToken);

    Task<IReadOnlyList<Ticket>> FetchAllPagesAsync(IReadOnlyList<TicketStatus>? statuses, CancellationToken cancellationToken);

    Task<Ticket> FetchTicketByIdAsync(string ticketId, CancellationToken cancellationToken);

    Task<bool> MarkAsTransferredAsync(string ticketId, string helpCenterTicketId, CancellationToken cancellationToken);

    Task<int> GetTotalCountAsync(IReadOnlyList<TicketStatus>? statuses, CancellationToken cancellationToken);

    Task<(bool Healthy, TimeSpan Latency)> HealthCheckAsync(CancellationToken cancellationToken);
}

public interface IDestinationTicketClient
{
    Task<string> CreateTicketAsync(Ticket ticket, CancellationToken cancellationToken);

    Task<string?> CheckTicketExistsAsync(string sourceTicketId, CancellationToken cancellationToken);

    Task<BatchTransferResult> BatchCreateTicketsAsync(IReadOnlyList<Ticket> tickets, CancellationToken cancellationToken);

    Task<(bool Healthy, TimeSpan Latency)> HealthCheckAsync(CancellationToken cancellationToken);
}
