use thiserror::Error;

#[derive(Error, Debug)]
pub enum PipelineError {
    #[error("Configuration error: {0}")]
    ConfigError(String),

    #[error("HTTP request failed: {0}")]
    HttpError(String),

    #[error("Authentication failed: {0}")]
    AuthError(String),

    #[error("Validation error: {0}")]
    ValidationError(String),

    #[error("Serialization error: {0}")]
    SerializationError(String),

    #[error("Encryption error: {0}")]
    EncryptionError(String),

    #[error("Rate limit exceeded: retry after {retry_after}s")]
    RateLimitError { retry_after: u64 },

    #[error("Circuit breaker open for {service}")]
    CircuitBreakerOpen { service: String },

    #[error("Timeout: {0}")]
    Timeout(String),

    #[error("Duplicate ticket: {ticket_id}")]
    DuplicateTicket { ticket_id: String },

    #[error("Ticket not found: {ticket_id}")]
    TicketNotFound { ticket_id: String },

    #[error("Connection error: {0}")]
    ConnectionError(String),

    #[error("Health check failed: {0}")]
    HealthCheckFailed(String),

    #[error(transparent)]
    Anyhow(#[from] anyhow::Error),
}

impl From<reqwest::Error> for PipelineError {
    fn from(err: reqwest::Error) -> Self {
        if err.is_timeout() {
            PipelineError::Timeout(err.to_string())
        } else if err.is_connect() {
            PipelineError::ConnectionError(err.to_string())
        } else {
            PipelineError::HttpError(err.to_string())
        }
    }
}

impl From<serde_json::Error> for PipelineError {
    fn from(err: serde_json::Error) -> Self {
        PipelineError::SerializationError(err.to_string())
    }
}

pub type PipelineResult<T> = Result<T, PipelineError>;
