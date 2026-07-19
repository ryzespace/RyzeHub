//! Source client - Client Dashboard API

use anyhow::Result;
use reqwest::Client;
use std::time::Duration;
use tracing::{debug, error, info, warn};

use crate::config::SourceConfig;
use crate::errors::PipelineError;
use crate::models::{Ticket, TicketStatus};
use crate::security::RateLimiter;

pub struct ClientDashboardClient {
    client: Client,
    config: SourceConfig,
    rate_limiter: RateLimiter,
}

impl ClientDashboardClient {
    pub fn new(config: SourceConfig) -> Result<Self> {
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
        })
    }

    /// Fetch tickets from client dashboard
    pub async fn fetch_tickets(
        &self,
        status_filter: Option<Vec<TicketStatus>>,
        page: usize,
    ) -> Result<Vec<Ticket>> {
        self.rate_limiter.wait().await;

        let url = format!("{}/tickets", self.config.base_url);
        let mut request = self
            .client
            .get(&url)
            .bearer_auth(&self.config.api_key)
            .query(&[("page", &page.to_string()), ("per_page", &self.config.batch_size.to_string())]);

        if let Some(statuses) = &status_filter {
            let status_str: Vec<String> = statuses.iter().map(|s| format!("{:?}", s).to_lowercase()).collect();
            request = request.query(&[("status", &status_str.join(","))]);
        }

        info!("Fetching tickets from {} (page={})", url, page);

        let response = request.send().await?;

        if !response.status().is_success() {
            let status = response.status();
            let body = response.text().await.unwrap_or_default();
            error!("Failed to fetch tickets: {} - {}", status, body);
            return Err(PipelineError::HttpError(format!("{}: {}", status, body)).into());
        }

        let data: serde_json::Value = response.json().await?;
        let tickets_raw = data
            .get("tickets")
            .or_else(|| data.get("data"))
            .and_then(|v| v.as_array())
            .cloned()
            .unwrap_or_default();

        let tickets: Vec<Ticket> = tickets_raw
            .into_iter()
            .filter_map(|raw| serde_json::from_value(raw).ok())
            .collect();

        info!("Fetched {} tickets from client dashboard", tickets.len());
        Ok(tickets)
    }

    /// Fetch single ticket
    pub async fn fetch_ticket_by_id(&self, ticket_id: &str) -> Result<Ticket> {
        self.rate_limiter.wait().await;

        let url = format!("{}/tickets/{}", self.config.base_url, ticket_id);
        debug!("Fetching ticket {}", ticket_id);

        let response = self
            .client
            .get(&url)
            .bearer_auth(&self.config.api_key)
            .send()
            .await?;

        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(PipelineError::TicketNotFound {
                ticket_id: ticket_id.to_string(),
            }
            .into());
        }

        response.error_for_status_ref()?;
        let ticket: Ticket = response.json().await?;
        Ok(ticket)
    }

    /// Mark ticket as transferred
    pub async fn mark_as_transferred(
        &self,
        ticket_id: &str,
        helpcenter_id: &str,
    ) -> Result<bool> {
        self.rate_limiter.wait().await;

        let url = format!("{}/tickets/{}/status", self.config.base_url, ticket_id);
        let payload = serde_json::json!({
            "status": "transferred",
            "helpcenter_ticket_id": helpcenter_id,
        });

        debug!("Marking ticket {} as transferred (helpcenter: {})", ticket_id, helpcenter_id);

        let response = self
            .client
            .patch(&url)
            .bearer_auth(&self.config.api_key)
            .json(&payload)
            .send()
            .await;

        match response {
            Ok(resp) if resp.status().is_success() => {
                info!("✓ Marked ticket {} as transferred", ticket_id);
                Ok(true)
            }
            Ok(resp) => {
                let status = resp.status();
                warn!("Failed to mark ticket {} as transferred: {}", ticket_id, status);
                Ok(false)
            }
            Err(e) => {
                error!("Failed to mark ticket {} as transferred: {}", ticket_id, e);
                Ok(false)
            }
        }
    }

    /// Get total ticket count
    pub async fn get_total_count(&self, status_filter: Option<Vec<TicketStatus>>) -> Result<usize> {
        self.rate_limiter.wait().await;

        let url = format!("{}/tickets", self.config.base_url);
        let mut request = self
            .client
            .get(&url)
            .bearer_auth(&self.config.api_key)
            .query(&[("count_only", "true")]);

        if let Some(statuses) = &status_filter {
            let status_str: Vec<String> = statuses.iter().map(|s| format!("{:?}", s).to_lowercase()).collect();
            request = request.query(&[("status", &status_str.join(","))]);
        }

        let response = request.send().await?;
        let data: serde_json::Value = response.json().await?;
        Ok(data.get("total").and_then(|v| v.as_u64()).unwrap_or(0) as usize)
    }

    /// Health check
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
            Ok(resp) if resp.status().is_success() => Ok((true, latency)),
            _ => Ok((false, latency)),
        }
    }

    /// Fetch all pages with pagination
    pub async fn fetch_all_pages(
        &self,
        status_filter: Option<Vec<TicketStatus>>,
    ) -> Result<Vec<Ticket>> {
        let mut all_tickets = Vec::new();
        let mut page = 1;

        loop {
            let batch = self.fetch_tickets(status_filter.clone(), page).await?;

            if batch.is_empty() {
                break;
            }

            all_tickets.extend(batch.clone());

            if batch.len() < self.config.batch_size {
                break; // Last page
            }
            page += 1;
        }

        info!("Fetched {} total tickets across all pages", all_tickets.len());
        Ok(all_tickets)
    }
}
