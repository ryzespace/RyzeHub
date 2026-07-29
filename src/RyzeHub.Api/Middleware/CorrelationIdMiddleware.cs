using Serilog.Context;

namespace RyzeHub.Api.Middleware;

/// <summary>
/// Echoes a caller-supplied correlation id, or falls back to the trace identifier,
/// and attaches it to every log entry for the request.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-ID";
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > MaxLength)
        {
            correlationId = context.TraceIdentifier;
        }

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}
