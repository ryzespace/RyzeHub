//! Metrics module / Moduł metryk

use chrono::Utc;
use prometheus::{
    self, Encoder, IntCounter, IntCounterVec, IntGauge, Opts, Registry, TextEncoder,
};
use serde::Serialize;
use std::sync::OnceLock;
use std::time::Instant;

static START_TIME: OnceLock<Instant> = OnceLock::new();

/// Get or initialize start time
fn get_start_time() -> Instant {
    *START_TIME.get_or_init(Instant::now)
}

/// Metrics registry singleton
static REGISTRY: OnceLock<Registry> = OnceLock::new();

fn get_registry() -> &'static Registry {
    REGISTRY.get_or_init(|| {
        let registry = Registry::new();

        // Register metrics / Zarejestruj metryki
        registry.register(Box::new(tickets_fetched())).unwrap();
        registry.register(Box::new(tickets_transferred())).unwrap();
        registry.register(Box::new(tickets_failed())).unwrap();
        registry.register(Box::new(tickets_filtered())).unwrap();
        registry.register(Box::new(pipeline_runs())).unwrap();
        registry.register(Box::new(transfer_duration_seconds())).unwrap();
        registry.register(Box::new(active_connections())).unwrap();

        registry
    })
}

// ─── Metric definitions / Definicje metryk ──────────────────

fn tickets_fetched() -> IntCounter {
    IntCounter::new("pipeline_tickets_fetched_total", "Total tickets fetched from source").unwrap()
}

fn tickets_transferred() -> IntCounter {
    IntCounter::new("pipeline_tickets_transferred_total", "Total tickets successfully transferred").unwrap()
}

fn tickets_failed() -> IntCounter {
    IntCounter::new("pipeline_tickets_failed_total", "Total tickets that failed to transfer").unwrap()
}

fn tickets_filtered() -> IntCounter {
    IntCounter::new("pipeline_tickets_filtered_total", "Total tickets filtered out").unwrap()
}

fn pipeline_runs() -> IntCounter {
    IntCounter::new("pipeline_runs_total", "Total pipeline runs").unwrap()
}

fn transfer_duration_seconds() -> IntGauge {
    IntGauge::new("pipeline_transfer_duration_ms", "Transfer duration in milliseconds").unwrap()
}

fn active_connections() -> IntGauge {
    IntGauge::new("pipeline_active_connections", "Active connections to APIs").unwrap()
}

// ─── Public API ──────────────────────────────────────────────

/// Increment fetched counter / Inkrementuj licznik pobranych
pub fn inc_fetched(count: usize) {
    tickets_fetched().inc_by(count as u64);
}

/// Increment transferred counter / Inkrementuj licznik przeniesionych
pub fn inc_transferred(count: usize) {
    tickets_transferred().inc_by(count as u64);
}

/// Increment failed counter / Inkrementuj licznik błędów
pub fn inc_failed(count: usize) {
    tickets_failed().inc_by(count as u64);
}

/// Increment filtered counter / Inkrementuj licznik odfiltrowanych
pub fn inc_filtered(count: usize) {
    tickets_filtered().inc_by(count as u64);
}

/// Increment pipeline runs / Inkrementuj licznik uruchomień
pub fn inc_pipeline_runs() {
    pipeline_runs().inc();
}

/// Set transfer duration / Ustaw czas transferu
pub fn set_transfer_duration(ms: u64) {
    transfer_duration_seconds().set(ms as i64);
}

/// Set active connections / Ustaw aktywne połączenia
pub fn set_active_connections(count: i64) {
    active_connections().set(count);
}

/// Get all metrics as Prometheus text format / Pobierz metryki w formacie Prometheus
pub fn get_metrics() -> String {
    let registry = get_registry();
    let encoder = TextEncoder::new();
    let metric_families = registry.gather();
    let mut buffer = Vec::new();
    encoder.encode(&metric_families, &mut buffer).unwrap();
    String::from_utf8(buffer).unwrap()
}

/// Pipeline run metrics / Metryki uruchomienia pipeline'a
#[derive(Debug, Clone, Serialize)]
pub struct PipelineMetrics {
    pub started_at: Option<String>,
    pub finished_at: Option<String>,
    pub duration_seconds: f64,
    pub fetched_count: usize,
    pub filtered_count: usize,
    pub valid_count: usize,
    pub transferred_count: usize,
    pub failed_count: usize,
    pub errors: Vec<String>,
}

impl PipelineMetrics {
    pub fn new() -> Self {
        Self {
            started_at: Some(Utc::now().to_rfc3339()),
            finished_at: None,
            duration_seconds: 0.0,
            fetched_count: 0,
            filtered_count: 0,
            valid_count: 0,
            transferred_count: 0,
            failed_count: 0,
            errors: Vec::new(),
        }
    }

    pub fn finish(&mut self) {
        self.finished_at = Some(Utc::now().to_rfc3339());
        if let (Some(start), Some(end)) = (&self.started_at, &self.finished_at) {
            let start_dt = chrono::DateTime::parse_from_rfc3339(start).unwrap();
            let end_dt = chrono::DateTime::parse_from_rfc3339(end).unwrap();
            self.duration_seconds = (end_dt - start_dt).num_milliseconds() as f64 / 1000.0;
        }
    }

    pub fn summary(&self) -> serde_json::Value {
        serde_json::json!({
            "started_at": self.started_at,
            "finished_at": self.finished_at,
            "duration_seconds": self.duration_seconds,
            "fetched": self.fetched_count,
            "filtered_out": self.filtered_count,
            "valid_for_transfer": self.valid_count,
            "transferred": self.transferred_count,
            "failed": self.failed_count,
            "errors": if self.errors.len() > 10 { &self.errors[self.errors.len()-10..] } else { &self.errors },
        })
    }

    pub fn get_uptime() -> u64 {
        get_start_time().elapsed().as_secs()
    }
}
