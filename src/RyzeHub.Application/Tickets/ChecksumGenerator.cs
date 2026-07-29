using System.Security.Cryptography;
using System.Text;
using RyzeHub.Domain.Tickets;

namespace RyzeHub.Application.Tickets;

public static class ChecksumGenerator
{
    public static string Generate(string data) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data)));

    public static string GenerateTicketChecksum(Ticket ticket) =>
        Generate($"{ticket.TicketId}:{ticket.Description}:{ticket.TicketType.ToWireValue()}");

    public static bool VerifyTicketChecksum(Ticket ticket) =>
        ticket.Checksum is { } checksum
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(checksum),
            Encoding.UTF8.GetBytes(GenerateTicketChecksum(ticket)));
}
