namespace RyzeHub.Domain.Errors;

public abstract class PipelineException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class ConfigurationException(string message) : PipelineException($"Configuration error: {message}");

public sealed class HttpTransportException(string message) : PipelineException($"HTTP request failed: {message}");

public sealed class AuthenticationFailedException(string message) : PipelineException($"Authentication failed: {message}");

public sealed class TicketValidationException(string message) : PipelineException($"Validation error: {message}");

public sealed class SerializationFailedException(string message) : PipelineException($"Serialization error: {message}");

public sealed class EncryptionFailedException(string message, Exception? innerException = null)
    : PipelineException($"Encryption error: {message}", innerException);

public sealed class RateLimitExceededException(long retryAfterSeconds)
    : PipelineException($"Rate limit exceeded: retry after {retryAfterSeconds}s")
{
    public long RetryAfterSeconds { get; } = retryAfterSeconds;
}

public sealed class CircuitBreakerOpenException(string service)
    : PipelineException($"Circuit breaker open for {service}")
{
    public string Service { get; } = service;
}

public sealed class PipelineTimeoutException(string message) : PipelineException($"Timeout: {message}");

public sealed class DuplicateTicketException(string ticketId) : PipelineException($"Duplicate ticket: {ticketId}")
{
    public string TicketId { get; } = ticketId;
}

public sealed class TicketNotFoundException(string ticketId) : PipelineException($"Ticket not found: {ticketId}")
{
    public string TicketId { get; } = ticketId;
}

public sealed class ConnectionFailedException(string message) : PipelineException($"Connection error: {message}");

public sealed class HealthCheckFailedException(string message) : PipelineException($"Health check failed: {message}");

/// <summary>
/// Raised when RyzeAuth denies an operation (missing scope, revoked key, inactive organization).
/// </summary>
public sealed class AuthorizationDeniedException(string message) : PipelineException($"Authorization denied: {message}");
