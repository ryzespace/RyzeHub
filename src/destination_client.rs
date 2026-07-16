//! Destination client - HelpCenter API / Klient API helpcenter

use anyhow::Result;
use reqwest::Client;
use std::time::Duration;
use tracing::{debug, error, info, warn};

use crate::config::DestinationConfig;
use crate::errors::PipelineError;
use crate::models::{BatchTransferResult, Ticket, TransferResult};
use crate::security::RateLimiter;

/// Circuit breaker state / Stan circuit breaker'a
#[derive(Debug, Clone, PartialEq)]
enum CircuitState {
    Closed,      // Normal operation / Normalna praca
    Open,        // Failing, reject requests / Błędy, odrzucaj requesty
    HalfOpen,    // Testing recovery / Testowanie recovery
}

pub struct HelpCenterClient {
    client: Client,
    config: DestinationConfig,
    rate_limiter: RateLimiter,
    circuit_state: std::sync::Mutex<CircuitState>,
    failure_count: std::sync::atomic::AtomicU32,
    failure_threshold: u32,
    recovery_timeout: Duration,
    last_failure: std::sync::Mutex<Option<std::time::Instant>>,
}

impl HelpCenterClient {
    pub fn new(config: DestinationConfig) -> Result<Self> {
        let client = Client::builder()
            .timeout(Duration::from_secs(config.timeout))
            .pool_max_idle_per_host(10)
            .tcp_keepalive(Duration::from_secs(60))
            .build()?;

        let rate_limiter = RateLimiter::new(config.rate_limit, 60);

        Ok(Self {
            client,
            config,
            rate_limiter,
            circuit_state: std::sync::Mutex::new(CircuitState::Closed),
            failure_count: std::sync::atomic::AtomicU32::new(0),
            failure_threshold: 5,
            recovery_timeout: Duration::from_secs(60),
            last_failure: std::sync::Mutex::new(None),
        })
    }

    /// Check circuit breaker / Sprawdź circuit breaker
    fn check_circuit(&self) -> Result<()> {
        let state = self.circuit_state.lock().unwrap();
        match *state {
            CircuitState::Open => {
                let last = self.last_failure.lock().unwrap();
                if let Some(last_time) = *last {
                    if last_time.elapsed() > self.recovery_timeout {
                        drop(state);
                        drop(last);
                        // Transition to half-open / Przejdź do half-open
                        *self.circuit_state.lock().unwrap() = CircuitState::HalfOpen;
                        return Ok(());
                    }
                }
                Err(PipelineError::CircuitBreakerOpen {
                    service: "helpcenter".to_string(),
                }
                .into())
            }
            _ => Ok(()),
        }
    }

    /// Record success / Zapisz sukces
    fn record_success(&self) {
        self.failure_count.store(0, std::sync::atomic::Ordering::SeqCst);
        *self.circuit_state.lock().unwrap() = CircuitState::Closed;
    }

    /// Record failure / Zapisz błąd
    fn record_failure(&self) {
        let count = self.failure_count.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        *self.last_failure.lock().unwrap() = Some(std::time::Instant::now());
        if count + 1 >= self.failure_threshold {
            warn!("Circuit breaker OPEN for helpcenter");
            *self.circuit_state.lock().unwrap() = CircuitState::Open;
        }
    }

    /// Create ticket in helpcenter / Utwórz ticket w helpcenter
    pub async fn create_ticket(&self, ticket: &Ticket) -> Result<String> {
        self.check_circuit()?;
        self.rate_limiter.wait().await;

        let url = format!("{}/tickets", self.config.base_url);
        let payload = serde_json::json!({
            "ticket_id": ticket.ticket_id,
            "ticket_type": ticket.ticket_type.to_string(),
            "description": ticket.description,
            "conversation": ticket.conversation.iter().map(|m| serde_json::json!({
                "sender": m.sender,
                "role": format!("{:?}", m.role).to_lowercase(),
                "content": m.content,
                "timestamp": m.timestamp.to_rfc3339(),
                "message_id": m.message_id,
            })).collect::<Vec<_>>(),
            "priority": ticket.priority.to_string(),
            "category": ticket.category,
            "tags": ticket.tags,
            "client_id": ticket.client_id,
            "client_name": ticket.client_name,
            "source": "client_dashboard",
            "source_ticket_id": ticket.ticket_id,
            "checksum": ticket.checksum,
        });

        info!("Creating ticket in helpcenter (source_id={})", ticket.ticket_id);

        let response = self
            .client
            .post(&url)
            .bearer_auth(&self.config.api_key)
            .json(&payload)
            .send()
            .await;

        match response {
            Ok(resp) if resp.status().is_success() => {
                self.record_success();
                let result: serde_json::Value = resp.json().await?;
                let new_id = result
                    .get("ticket_id")
                    .or_else(|| result.get("id"))
                    .and_then(|v| v.as_str())
                    .unwrap_or("")
                    .to_string();
                info!("Created helpcenter ticket: {} (from source: {})", new_id, ticket.ticket_id);
                Ok(new_id)
            }
            Ok(resp) => {
                let status = resp.status();
                self.record_failure();
                if status == reqwest::StatusCode::TOO_MANY_REQUESTS {
                    let retry_after = resp
                        .headers()
                        .get("retry-after")
                        .and_then(|v| v.to_str().ok())
                        .and_then(|v| v.parse::<u64>().ok())
                        .unwrap_or(60);
                    Err(PipelineError::RateLimitError { retry_after }.into())
                } else {
                    let body = resp.text().await.unwrap_or_default();
                    Err(PipelineError::HttpError(format!("{}: {}", status, body)).into())
                }
            }
            Err(e) => {
                self.record_failure();
                Err(e.into())
            }
        }
    }

    /// Check if ticket exists / Sprawdź czy ticket istnieje
    pub async fn check_ticket_exists(&self, source_ticket_id: &str) -> Option<String> {
        self.rate_limiter.wait().await;

        let url = format!("{}/tickets/lookup", self.config.base_url);

        let response = self
            .client
            .get(&url)
            .bearer_auth(&self.config.api_key)
            .query(&[("source_ticket_id", source_ticket_id)])
            .send()
            .await;

        match response {
            Ok(resp) if resp.status().is_success() => {
                let result: serde_json::Value = resp.json().await.unwrap_or_default();
                if result.get("found").and_then(|v| v.as_bool()).unwrap_or(false) {
                    result.get("ticket_id").and_then(|v| v.as_str()).map(String::from)
                } else {
                    None
                }
            }
            _ => None,
        }
    }

    /// Batch create tickets / Utwórz wiele ticketów
    pub async fn batch_create_tickets(&self, tickets: &[Ticket]) -> Result<BatchTransferResult> {
        self.check_circuit()?;
        self.rate_limiter.wait().await;

        let start = std::time::Instant::now();
        let url = format!("{}/tickets/batch", self.config.base_url);

        let payload = serde_json::json!({
            "tickets": tickets.iter().map(|t| serde_json::json!({
                "ticket_id": t.ticket_id,
                "ticket_type": t.ticket_type.to_string(),
                "description": t.description,
                "conversation": t.conversation.iter().map(|m| serde_json::json!({
                    "sender": m.sender,
                    "role": format!("{:?}", m.role).to_lowercase(),
                    "content": m.content,
                    "timestamp": m.timestamp.to_rfc3339(),
                })).collect::<Vec<_>>(),
                "priority": t.priority.to_string(),
                "category": t.category,
                "tags": t.tags,
                "client_id": t.client_id,
                "client_name": t.client_name,
                "source": "client_dashboard",
                "checksum": t.checksum,
            })).collect::<Vec<_>>(),
        });

        info!("Batch creating {} tickets in helpcenter", tickets.len());

        let response = self
            .client
            .post(&url)
            .bearer_auth(&self.config.api_key)
            .json(&payload)
            .send()
            .await?;

        if !response.status().is_success() {
            self.record_failure();
            let status = response.status();
            let body = response.text().await.unwrap_or_default();
            return Err(PipelineError::HttpError(format!("{}: {}", status, body)).into());
        }

        self.record_success();
        let result: serde_json::Value = response.json().await?;
        let duration = start.elapsed();

        let results_raw = result.get("results").and_then(|v| v.as_array()).cloned().unwrap_or_default();
        let results: Vec<TransferResult> = results_raw
            .into_iter()
            .filter_map(|r| serde_json::from_value(r).ok())
            .collect();

        let successful = results.iter().filter(|r| r.success).count();

        Ok(BatchTransferResult {
            results,
            total: tickets.len(),
            successful,
            failed: tickets.len() - successful,
            duration_ms: duration.as_millis() as u64,
        })
    }

    /// Health check / Sprawdzenie zdrowia
    pub async fn health_check(&self) -> Result<(bool, Duration)> {
        let start = std::time::Instant::now();
        let url = format!("{}/health", self.config.base_url);

        let response = self
            .client
            .get(&url)
            .bearer_auth(&self.config.api_key)
            .timeout(Duration::from_secs(5))
            .send()
            .await;

        let latency = start.elapsed();

        match response {
            Ok(resp) if resp.status().is_success() => {
                self.record_success();
                Ok((true, latency))
            }
            _ => {
                self.record_failure();
                Ok((false, latency))
            }
        }
    }
}
