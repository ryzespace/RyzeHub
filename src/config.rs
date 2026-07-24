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
    pub hub: HubPlatformConfig,
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

#[derive(Debug, Clone, Deserialize)]
pub struct HubPlatformConfig {
    pub event_retention_seconds: u64,
    pub cache_provider: String,
    pub cache_ttl_seconds: u64,
    pub gateway_base_path: String,
    pub gateway_rate_limit: u32,
    pub telemetry_enabled: bool,
    pub notification_retention_seconds: u64,
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
            hub: HubPlatformConfig::default(),
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

impl Default for HubPlatformConfig {
    fn default() -> Self {
        Self {
            event_retention_seconds: 86_400,
            cache_provider: "redis".to_string(),
            cache_ttl_seconds: 3_600,
            gateway_base_path: "/api".to_string(),
            gateway_rate_limit: 120,
            telemetry_enabled: true,
            notification_retention_seconds: 604_800,
        }
    }
}

impl PipelineConfig {
    pub fn from_env() -> Result<Self> {
        let mut config = Self::default();

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

        if let Ok(key) = env::var("ENCRYPTION_KEY") {
            config.security.encryption_key = key;
        }
        if let Ok(audit) = env::var("AUDIT_LOG_ENABLED") {
            config.security.audit_log_enabled = audit.parse().unwrap_or(true);
        }
        if let Ok(checksum) = env::var("CHECKSUM_ENABLED") {
            config.security.checksum_enabled = checksum.parse().unwrap_or(true);
        }

        if let Ok(level) = env::var("PIPELINE_LOG_LEVEL") {
            config.log_level = level;
        }
        if let Ok(interval) = env::var("POLL_INTERVAL") {
            config.poll_interval = interval.parse().unwrap_or(300);
        }

        if let Ok(retention) = env::var("HUB_EVENT_RETENTION_SECONDS") {
            config.hub.event_retention_seconds = retention.parse().unwrap_or(86_400);
        }
        if let Ok(provider) = env::var("HUB_CACHE_PROVIDER") {
            config.hub.cache_provider = provider;
        }
        if let Ok(ttl) = env::var("HUB_CACHE_TTL_SECONDS") {
            config.hub.cache_ttl_seconds = ttl.parse().unwrap_or(3_600);
        }
        if let Ok(path) = env::var("HUB_GATEWAY_BASE_PATH") {
            config.hub.gateway_base_path = path;
        }
        if let Ok(limit) = env::var("HUB_GATEWAY_RATE_LIMIT") {
            config.hub.gateway_rate_limit = limit.parse().unwrap_or(120);
        }
        if let Ok(enabled) = env::var("HUB_TELEMETRY_ENABLED") {
            config.hub.telemetry_enabled = enabled.parse().unwrap_or(true);
        }
        if let Ok(retention) = env::var("HUB_NOTIFICATION_RETENTION_SECONDS") {
            config.hub.notification_retention_seconds = retention.parse().unwrap_or(604_800);
        }

        Ok(config)
    }

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
            Err(errors)
        }
    }
}
