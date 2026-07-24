use std::collections::HashSet;
use std::time::Instant;

use anyhow::Result;
use tracing::{error, info, warn};

use crate::config::PipelineConfig;
use crate::hub_platform::{ClientPlatform, HubPlatform, NotificationChannel, PresenceStatus};
use crate::destination_client::HelpCenterClient;
use crate::metrics::{self, PipelineMetrics};
use crate::models::{HealthStatus, PipelineHealth, ServiceHealth, Ticket, TicketStatus, TransferResult};
use crate::security::{AuditLogger, ChecksumGenerator, EncryptionManager};
use crate::source_client::ClientDashboardClient;
use crate::transformer;

pub struct TicketPipeline {
    config: PipelineConfig,
    source: ClientDashboardClient,
    destination: HelpCenterClient,
    audit: AuditLogger,
    encryption: Option<EncryptionManager>,
    hub: HubPlatform,
}

impl TicketPipeline {
    pub async fn new(config: PipelineConfig) -> Result<Self> {
        let source = ClientDashboardClient::new(config.source.clone())?;
        let destination = HelpCenterClient::new(config.destination.clone())?;
        let audit = AuditLogger::new(config.security.audit_log_enabled);

        let encryption = if !config.security.encryption_key.is_empty() {
            EncryptionManager::new(&config.security.encryption_key).ok()
        } else {
            None
        };
        let hub = HubPlatform::new(config.hub.clone());

        Ok(Self {
            config,
            source,
            destination,
            audit,
            encryption,
            hub,
        })
    }

    pub async fn run_once(&self) -> Result<PipelineMetrics> {
        let mut metrics = PipelineMetrics::new();
        metrics::inc_pipeline_runs();
        let start = Instant::now();

        info!("═══════════════════════════════════════════════");
        info!("Pipeline run started");
        info!("═══════════════════════════════════════════════");

        info!("Step 1: Fetching tickets from client dashboard...");
        let statuses_to_fetch = self.get_statuses_to_fetch();
        let tickets = self
            .source
            .fetch_all_pages(Some(statuses_to_fetch))
            .await?;
        self.hub.update_service_health("RyzeSpace.Client", true, 0);
        metrics.fetched_count = tickets.len();
        metrics::inc_fetched(tickets.len());

        if tickets.is_empty() {
            info!("No tickets to process.");
            metrics.finish();
            return Ok(metrics);
        }

        let mut tickets = tickets;
        if let Some(ref enc) = self.encryption {
            info!("Step 2: Decrypting ticket data...");
            for ticket in &mut tickets {
                if let Err(e) = enc.decrypt_ticket(ticket) {
                    warn!("Failed to decrypt ticket {}: {}", ticket.ticket_id, e);
                }
            }
        }

        let mut already_transferred: HashSet<String> = HashSet::new();
        if self.config.deduplicate {
            info!("Step 3: Checking for duplicates...");
            for ticket in &tickets {
                if let Some(existing_id) = self.destination.check_ticket_exists(&ticket.ticket_id).await {
                    already_transferred.insert(ticket.ticket_id.clone());
                    info!("Duplicate found: {} -> {}", ticket.ticket_id, existing_id);
                }
            }
            info!(
                "Found {} already transferred tickets",
                already_transferred.len()
            );
        }

        info!("Step 4: Transforming tickets (validate + enrich)...");
        let (valid_tickets, failed_results) = transformer::process_batch(
            tickets,
            self.config.auto_categorize,
            self.config.auto_priority,
            self.config.filter_resolved,
            self.config.filter_closed,
            &already_transferred,
            self.config.security.checksum_enabled,
            &self.audit,
        );

        metrics.valid_count = valid_tickets.len();
        metrics.failed_count += failed_results.len();

        if valid_tickets.is_empty() {
            info!("No valid tickets after transformation.");
            metrics.finish();
            return Ok(metrics);
        }

        let mut tickets_to_send = valid_tickets;
        if let Some(ref enc) = self.encryption {
            info!("Step 5: Encrypting sensitive data before transfer...");
            for ticket in &mut tickets_to_send {
                if let Err(e) = enc.encrypt_ticket(ticket) {
                    warn!("Failed to encrypt ticket {}: {}", ticket.ticket_id, e);
                }
            }
        }

        info!(
            "Step 6: Transferring {} tickets to helpcenter...",
            tickets_to_send.len()
        );
        let transfer_start = Instant::now();
        let transfer_results = self.transfer_tickets(&tickets_to_send).await;
        let transfer_duration = transfer_start.elapsed();
        metrics::set_transfer_duration(transfer_duration.as_millis() as u64);
        self.hub
            .update_service_health("RyzeSpace.HelpCenter", true, transfer_duration.as_millis() as u64);

        for result in &transfer_results {
            if result.success {
                metrics.transferred_count += 1;
                metrics::inc_transferred(1);
                self.audit.log_transfer(
                    &result.ticket_id,
                    "client_dashboard",
                    result.helpcenter_ticket_id.as_deref().unwrap_or("unknown"),
                );
                let _ = self
                    .source
                    .mark_as_transferred(
                        &result.ticket_id,
                        result.helpcenter_ticket_id.as_deref().unwrap_or(""),
                    )
                    .await;

                if let Some(ticket) = tickets_to_send
                    .iter()
                    .find(|ticket| ticket.ticket_id == result.ticket_id)
                {
                    let user_id = if ticket.client_id.is_empty() {
                        "unknown-user"
                    } else {
                        ticket.client_id.as_str()
                    };

                    self.hub.update_presence(
                        user_id,
                        PresenceStatus::Online,
                        Some(format!("ticket-{}", ticket.ticket_id)),
                    );
                    self.hub.record_support_status_update(
                        user_id,
                        &ticket.ticket_id,
                        "transferred",
                        vec![
                            NotificationChannel::MobilePush,
                            NotificationChannel::Desktop,
                            NotificationChannel::Email,
                        ],
                    );
                    self.hub.record_activity(
                        user_id,
                        format!("Przeniesiono zgłoszenie {} do HelpCenter", ticket.ticket_id),
                        serde_json::json!({
                            "ticket_id": ticket.ticket_id,
                            "helpcenter_ticket_id": result.helpcenter_ticket_id,
                        }),
                    );
                }
            } else {
                metrics.failed_count += 1;
                metrics::inc_failed(1);
                if let Some(err) = &result.error_message {
                    metrics.errors.push(format!("{}: {}", result.ticket_id, err));
                    self.hub.update_service_health(
                        "RyzeSpace.HelpCenter",
                        false,
                        transfer_duration.as_millis() as u64,
                    );
                    self.hub.record_security_warning(
                        None,
                        format!("Błąd transferu zgłoszenia {}: {}", result.ticket_id, err).as_str(),
                        vec![NotificationChannel::SlackWebhook, NotificationChannel::DiscordWebhook],
                    );
                }
            }
        }

        metrics.finish();
        info!("═══════════════════════════════════════════════");
        info!("Pipeline finished. Summary:");
        info!(
            "  Fetched: {}, Transferred: {}, Failed: {}, Duration: {:.2}s",
            metrics.fetched_count,
            metrics.transferred_count,
            metrics.failed_count,
            metrics.duration_seconds
        );
        info!("═══════════════════════════════════════════════");

        Ok(metrics)
    }

    pub async fn run_continuous(&self) -> Result<()> {
        info!(
            "Starting continuous pipeline (poll every {}s)",
            self.config.poll_interval
        );

        loop {
            match self.run_once().await {
                Ok(_) => {}
                Err(e) => {
                    error!("Pipeline run error: {}", e);
                }
            }

            info!("Waiting {}s until next run...", self.config.poll_interval);
            tokio::time::sleep(tokio::time::Duration::from_secs(self.config.poll_interval)).await;
        }
    }

    async fn transfer_tickets(&self, tickets: &[Ticket]) -> Vec<TransferResult> {
        let mut results = Vec::new();
        let now = chrono::Utc::now();

        for ticket in tickets {
            let mut last_error = String::new();
            let mut success = false;
            let mut helpcenter_id = None;

            for attempt in 0..self.config.destination.max_retries {
                match self.destination.create_ticket(ticket).await {
                    Ok(id) => {
                        helpcenter_id = Some(id);
                        success = true;
                        break;
                    }
                    Err(e) => {
                        last_error = e.to_string();
                        warn!(
                            "Transfer attempt {}/{} failed for ticket {}: {}",
                            attempt + 1,
                            self.config.destination.max_retries,
                            ticket.ticket_id,
                            last_error
                        );

                        let delay = self.config.destination.retry_delay * 2u64.pow(attempt);
                        tokio::time::sleep(tokio::time::Duration::from_secs(delay)).await;
                    }
                }
            }

            results.push(TransferResult {
                ticket_id: ticket.ticket_id.clone(),
                success,
                helpcenter_ticket_id: helpcenter_id,
                error_message: if success { None } else { Some(last_error) },
                timestamp: now,
                retry_count: self.config.destination.max_retries,
            });
        }

        results
    }

    fn get_statuses_to_fetch(&self) -> Vec<TicketStatus> {
        vec![
            TicketStatus::New,
            TicketStatus::InProgress,
            TicketStatus::Waiting,
        ]
    }

    pub async fn health_check(&self) -> Result<HealthStatus> {
        let (source_healthy, source_latency) = self.source.health_check().await.unwrap_or((false, Default::default()));
        let (dest_healthy, dest_latency) = self.destination.health_check().await.unwrap_or((false, Default::default()));
        self.hub
            .update_service_health("RyzeSpace.Client", source_healthy, source_latency.as_millis() as u64);
        self.hub.update_service_health(
            "RyzeSpace.HelpCenter",
            dest_healthy,
            dest_latency.as_millis() as u64,
        );

        let overall_status = if source_healthy && dest_healthy {
            "healthy"
        } else if source_healthy || dest_healthy {
            "degraded"
        } else {
            "unhealthy"
        };

        Ok(HealthStatus {
            status: overall_status.to_string(),
            timestamp: chrono::Utc::now(),
            source: ServiceHealth {
                name: "client_dashboard".to_string(),
                status: if source_healthy { "up".to_string() } else { "down".to_string() },
                latency_ms: source_latency.as_millis() as u64,
                last_check: chrono::Utc::now(),
            },
            destination: ServiceHealth {
                name: "helpcenter".to_string(),
                status: if dest_healthy { "up".to_string() } else { "down".to_string() },
                latency_ms: dest_latency.as_millis() as u64,
                last_check: chrono::Utc::now(),
            },
            pipeline: PipelineHealth {
                status: overall_status.to_string(),
                uptime_seconds: PipelineMetrics::get_uptime(),
                tickets_processed: 0,
                error_rate: 0.0,
            },
            hub: Some(self.hub.health_status()),
        })
    }

    pub fn hub_snapshot(&self) -> crate::hub_platform::HubPlatformSnapshot {
        self.hub.snapshot()
    }

    pub fn hub_handle(&self) -> HubPlatform {
        self.hub.clone()
    }

    pub fn seed_hub_demo(&self, user_id: &str) -> crate::hub_platform::HubPlatformSnapshot {
        let device_id = self
            .hub
            .register_device(user_id, "RyzeHub Control Center", ClientPlatform::Desktop, true);
        let _session_id = self.hub.create_session(
            user_id,
            &device_id,
            ClientPlatform::Desktop,
            "198.51.100.10",
        );
        self.hub
            .update_presence(user_id, PresenceStatus::Online, Some(device_id));
        self.hub.seed_demo_data(user_id)
    }
}
