//! Configuration / Konfiguracja

use anyhow::Result;
use serde::Deserialize;
use std::env;

#[derive(Debug, Clone, Deserialize)]
pub struct PipelineConfig {
    pub source: SourceConfig,
    pub destination: DestinationConfig,
    pub auto_categorize: bool,
    pub auto_priority: bool,
    pub filter_resolved: bool,
    pub filter_closed: bool,
    pub deduplicate: bool,
    pub log_level: String,
    pub poll_interval: u64,
    pub security: SecurityConfig,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SourceConfig {
    pub base_url: String,
    pub api_key: String,
    pub timeout: u64,
    pub max_retries: u32,
    pub retry_delay: u64,
    pub batch_size: usize,
    pub rate_limit: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct DestinationConfig {
    pub base_url: String,
    pub api_key: String,
    pub timeout: u64,
    pub max_retries: u32,
    pub retry_delay: u64,
    pub rate_limit: u32,
}

#[derive(Debug, Clone, Deserialize)]
pub struct SecurityConfig {
    pub encryption_key: String,
    pub audit_log_enabled: bool,
    pub checksum_enabled: bool,
    pub sensitive_fields_mask: bool,
}

impl Default for PipelineConfig {
    fn default() -> Self {
        Self {
            source: SourceConfig::default(),
            destination: DestinationConfig::default(),
            auto_categorize: true,
            auto_priority: true,
            filter_resolved: true,
            filter_closed: true,
            deduplicate: true,
            log_level: "info".to_string(),
            poll_interval: 300,
            security: SecurityConfig::default(),
        }
    }
}

impl Default for SourceConfig {
    fn default() -> Self {
        Self {
            base_url: "https://client-dashboard.example.com/api/v1".to_string(),
            api_key: String::new(),
            timeout: 30,
            max_retries: 3,
            retry_delay: 2,
            batch_size: 50,
            rate_limit: 100,
        }
    }
}

impl Default for DestinationConfig {
    fn default() -> Self {
        Self {
            base_url: "https://helpcenter.example.com/api/v1".to_string(),
            api_key: String::new(),
            timeout: 30,
            max_retries: 3,
            retry_delay: 2,
            rate_limit: 100,
        }
    }
}

impl Default for SecurityConfig {
    fn default() -> Self {
        Self {
            encryption_key: String::new(),
            audit_log_enabled: true,
            checksum_enabled: true,
            sensitive_fields_mask: true,
        }
    }
}

impl PipelineConfig {
    /// Load from environment variables / Załaduj ze zmiennych środowiskowych
    pub fn from_env() -> Result<Self> {
        let mut config = Self::default();

        // Source config / Konfiguracja źródła
        if let Ok(url) = env::var("CLIENT_DASHBOARD_URL") {
            config.source.base_url = url;
        }
        if let Ok(key) = env::var("CLIENT_DASHBOARD_API_KEY") {
            config.source.api_key = key;
        }
        if let Ok(timeout) = env::var("SOURCE_TIMEOUT") {
            config.source.timeout = timeout.parse().unwrap_or(30);
        }
        if let Ok(retries) = env::var("SOURCE_MAX_RETRIES") {
            config.source.max_retries = retries.parse().unwrap_or(3);
        }
        if let Ok(batch) = env::var("SOURCE_BATCH_SIZE") {
            config.source.batch_size = batch.parse().unwrap_or(50);
        }
        if let Ok(rate) = env::var("SOURCE_RATE_LIMIT") {
            config.source.rate_limit = rate.parse().unwrap_or(100);
        }

        // Destination config / Konfiguracja celu
        if let Ok(url) = env::var("HELPCENTER_URL") {
            config.destination.base_url = url;
        }
        if let Ok(key) = env::var("HELPCENTER_API_KEY") {
            config.destination.api_key = key;
        }
        if let Ok(timeout) = env::var("DEST_TIMEOUT") {
            config.destination.timeout = timeout.parse().unwrap_or(30);
        }
        if let Ok(retries) = env::var("DEST_MAX_RETRIES") {
            config.destination.max_retries = retries.parse().unwrap_or(3);
        }
        if let Ok(rate) = env::var("DEST_RATE_LIMIT") {
            config.destination.rate_limit = rate.parse().unwrap_or(100);
        }

        // Security config / Konfiguracja bezpieczeństwa
        if let Ok(key) = env::var("ENCRYPTION_KEY") {
            config.security.encryption_key = key;
        }
        if let Ok(audit) = env::var("AUDIT_LOG_ENABLED") {
            config.security.audit_log_enabled = audit.parse().unwrap_or(true);
        }
        if let Ok(checksum) = env::var("CHECKSUM_ENABLED") {
            config.security.checksum_enabled = checksum.parse().unwrap_or(true);
        }

        // Pipeline config / Konfiguracja pipeline'a
        if let Ok(level) = env::var("PIPELINE_LOG_LEVEL") {
            config.log_level = level;
        }
        if let Ok(interval) = env::var("POLL_INTERVAL") {
            config.poll_interval = interval.parse().unwrap_or(300);
        }

        Ok(config)
    }

    /// Validate configuration / Waliduj konfigurację
    pub fn validate(&self) -> Result<(), Vec<String>> {
        let mut errors = Vec::new();

        if self.source.base_url.is_empty() {
            errors.push("Source base_url is required".to_string());
        }
        if self.source.api_key.is_empty() {
            errors.push("Source api_key is required".to_string());
        }
        if self.destination.base_url.is_empty() {
            errors.push("Destination base_url is required".to_string());
        }
        if self.destination.api_key.is_empty() {
            errors.push("Destination api_key is required".to_string());
        }
        if self.security.encryption_key.is_empty() {
            errors.push("Encryption key is required for security".to_string());
        }

        if errors.is_empty() {
            Ok(())
        } else {
            Err(errors.into_iter().next().unwrap())
                .map_err(|e| anyhow::anyhow!("Configuration errors: {:?}", vec![e]))
        }
    }
}
