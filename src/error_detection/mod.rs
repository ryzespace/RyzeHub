//! Error Detection System / System Wykrywania Błędów
//! Pattern recognition and anomaly detection

use chrono::{DateTime, Duration, Utc};
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, VecDeque};
use tracing::{debug, error, info, warn};

/// Error severity / Poziom błędu
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq, Hash)]
pub enum ErrorSeverity {
    Low,
    Medium,
    High,
    Critical,
}

/// Error category / Kategoria błędu
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq, Hash)]
pub enum ErrorCategory {
    Network,
    Authentication,
    Authorization,
    Validation,
    Serialization,
    Database,
    External,
    Configuration,
    Timeout,
    RateLimit,
    Unknown,
}

/// Detected error / Wykryty błąd
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DetectedError {
    pub id: String,
    pub timestamp: DateTime<Utc>,
    pub message: String,
    pub category: ErrorCategory,
    pub severity: ErrorSeverity,
    pub source: String,
    pub context: HashMap<String, String>,
    pub pattern_id: Option<String>,
    pub correlation_id: Option<String>,
}

/// Error pattern / Wzorzec błędu
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ErrorPattern {
    pub id: String,
    pub name: String,
    pub description: String,
    pub regex_pattern: String,
    pub category: ErrorCategory,
    pub severity: ErrorSeverity,
    pub occurrence_count: u64,
    pub first_seen: DateTime<Utc>,
    pub last_seen: DateTime<Utc>,
    pub auto_resolve: bool,
}

/// Anomaly detection result / Wynik detekcji anomalii
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AnomalyResult {
    pub timestamp: DateTime<Utc>,
    pub metric_name: String,
    pub current_value: f64,
    pub threshold: f64,
    pub deviation: f64,
    pub severity: ErrorSeverity,
    pub description: String,
}

/// Error Detection Engine / Silnik Wykrywania Błędów
pub struct ErrorDetectionEngine {
    patterns: HashMap<String, ErrorPattern>,
    recent_errors: VecDeque<DetectedError>,
    error_counts: HashMap<ErrorCategory, u64>,
    anomaly_thresholds: HashMap<String, f64>,
    metric_history: HashMap<String, VecDeque<f64>>,
    max_history_size: usize,
    max_recent_errors: usize,
}

impl ErrorDetectionEngine {
    /// Create new error detection engine / Utwórz nowy silnik
    pub fn new() -> Self {
        let mut engine = Self {
            patterns: HashMap::new(),
            recent_errors: VecDeque::new(),
            error_counts: HashMap::new(),
            anomaly_thresholds: HashMap::new(),
            metric_history: HashMap::new(),
            max_history_size: 1000,
            max_recent_errors: 10000,
        };

        // Initialize default patterns / Inicjalizuj domyślne wzorce
        engine.initialize_default_patterns();

        info!("Initialized error detection engine with {} patterns", engine.patterns.len());
        engine
    }

    /// Initialize default error patterns / Inicjalizuj domyślne wzorce błędów
    fn initialize_default_patterns(&mut self) {
        let patterns = vec![
            ErrorPattern {
                id: "network_timeout".to_string(),
                name: "Network Timeout".to_string(),
                description: "Connection or request timeout".to_string(),
                regex_pattern: r"(?i)(timeout|timed out|deadline exceeded)".to_string(),
                category: ErrorCategory::Timeout,
                severity: ErrorSeverity::Medium,
                occurrence_count: 0,
                first_seen: Utc::now(),
                last_seen: Utc::now(),
                auto_resolve: false,
            },
            ErrorPattern {
                id: "auth_failure".to_string(),
                name: "Authentication Failure".to_string(),
                description: "Authentication or authorization error".to_string(),
                regex_pattern: r"(?i)(unauthorized|forbidden|auth.*fail|invalid.*token|401|403)".to_string(),
                category: ErrorCategory::Authentication,
                severity: ErrorSeverity::High,
                occurrence_count: 0,
                first_seen: Utc::now(),
                last_seen: Utc::now(),
                auto_resolve: false,
            },
            ErrorPattern {
                id: "rate_limit".to_string(),
                name: "Rate Limit Exceeded".to_string(),
                description: "API rate limit exceeded".to_string(),
                regex_pattern: r"(?i)(rate.?limit|too many requests|429|throttl)".to_string(),
                category: ErrorCategory::RateLimit,
                severity: ErrorSeverity::Medium,
                occurrence_count: 0,
                first_seen: Utc::now(),
                last_seen: Utc::now(),
                auto_resolve: true,
            },
            ErrorPattern {
                id: "validation_error".to_string(),
                name: "Validation Error".to_string(),
                description: "Data validation failed".to_string(),
                regex_pattern: r"(?i)(validation.*fail|invalid.*data|required.*field|missing.*field)".to_string(),
                category: ErrorCategory::Validation,
                severity: ErrorSeverity::Low,
                occurrence_count: 0,
                first_seen: Utc::now(),
                last_seen: Utc::now(),
                auto_resolve: false,
            },
            ErrorPattern {
                id: "connection_error".to_string(),
                name: "Connection Error".to_string(),
                description: "Network connection failure".to_string(),
                regex_pattern: r"(?i)(connection.*refused|connection.*reset|network.*unreachable|503)".to_string(),
                category: ErrorCategory::Network,
                severity: ErrorSeverity::High,
                occurrence_count: 0,
                first_seen: Utc::now(),
                last_seen: Utc::now(),
                auto_resolve: false,
            },
        ];

        for pattern in patterns {
            self.patterns.insert(pattern.id.clone(), pattern);
        }
    }

    /// Detect error from message / Wykryj błąd z wiadomości
    pub fn detect_error(&mut self, message: &str, source: &str) -> Option<DetectedError> {
        let message_lower = message.to_lowercase();

        // Check against patterns / Sprawdź wzorce
        for pattern in self.patterns.values() {
            if let Ok(regex) = regex::Regex::new(&pattern.regex_pattern) {
                if regex.is_match(&message_lower) {
                    let error_id = uuid::Uuid::new_v4().to_string();
                    let detected = DetectedError {
                        id: error_id,
                        timestamp: Utc::now(),
                        message: message.to_string(),
                        category: pattern.category.clone(),
                        severity: pattern.severity.clone(),
                        source: source.to_string(),
                        context: HashMap::new(),
                        pattern_id: Some(pattern.id.clone()),
                        correlation_id: None,
                    };

                    // Update pattern stats / Aktualizuj statystyki wzorca
                    if let Some(p) = self.patterns.get_mut(&pattern.id) {
                        p.occurrence_count += 1;
                        p.last_seen = Utc::now();
                    }

                    // Update error counts / Aktualizuj liczniki błędów
                    *self.error_counts.entry(pattern.category.clone()).or_insert(0) += 1;

                    // Store in recent errors / Zapisz w ostatnich błędach
                    self.recent_errors.push_back(detected.clone());
                    if self.recent_errors.len() > self.max_recent_errors {
                        self.recent_errors.pop_front();
                    }

                    debug!("Detected error pattern: {} in {}", pattern.name, source);
                    return Some(detected);
                }
            }
        }

        None
    }

    /// Add custom pattern / Dodaj niestandardowy wzorzec
    pub fn add_pattern(&mut self, pattern: ErrorPattern) {
        info!("Added custom error pattern: {}", pattern.name);
        self.patterns.insert(pattern.id.clone(), pattern);
    }

    /// Set anomaly threshold / Ustaw próg anomalii
    pub fn set_anomaly_threshold(&mut self, metric_name: &str, threshold: f64) {
        self.anomaly_thresholds.insert(metric_name.to_string(), threshold);
    }

    /// Record metric value / Zapisz wartość metryki
    pub fn record_metric(&mut self, metric_name: &str, value: f64) {
        let history = self.metric_history.entry(metric_name.to_string()).or_insert_with(VecDeque::new);
        history.push_back(value);
        if history.len() > self.max_history_size {
            history.pop_front();
        }
    }

    /// Detect anomalies / Wykryj anomalie
    pub fn detect_anomalies(&self) -> Vec<AnomalyResult> {
        let mut anomalies = Vec::new();

        for (metric_name, history) in &self.metric_history {
            if history.len() < 10 {
                continue; // Need enough data / Potrzeba wystarczająco danych
            }

            let current_value = *history.back().unwrap();
            let mean = history.iter().sum::<f64>() / history.len() as f64;
            let variance = history.iter().map(|x| (x - mean).powi(2)).sum::<f64>() / history.len() as f64;
            let std_dev = variance.sqrt();

            // Check if current value is anomalous / Sprawdź czy wartość jest anomalią
            let threshold = self.anomaly_thresholds.get(metric_name).copied().unwrap_or(2.0);
            let deviation = if std_dev > 0.0 {
                (current_value - mean).abs() / std_dev
            } else {
                0.0
            };

            if deviation > threshold {
                let severity = if deviation > threshold * 2.0 {
                    ErrorSeverity::Critical
                } else if deviation > threshold * 1.5 {
                    ErrorSeverity::High
                } else {
                    ErrorSeverity::Medium
                };

                anomalies.push(AnomalyResult {
                    timestamp: Utc::now(),
                    metric_name: metric_name.clone(),
                    current_value,
                    threshold,
                    deviation,
                    severity,
                    description: format!(
                        "Metric {} is {:.2} std devs from mean (current: {:.2}, mean: {:.2})",
                        metric_name, deviation, current_value, mean
                    ),
                });

                warn!("Anomaly detected: {}", metric_name);
            }
        }

        anomalies
    }

    /// Get error statistics / Pobierz statystyki błędów
    pub fn get_statistics(&self) -> HashMap<ErrorCategory, u64> {
        self.error_counts.clone()
    }

    /// Get recent errors / Pobierz ostatnie błędy
    pub fn get_recent_errors(&self, limit: usize) -> Vec<DetectedError> {
        self.recent_errors.iter().rev().take(limit).cloned().collect()
    }

    /// Get pattern statistics / Pobierz statystyki wzorców
    pub fn get_pattern_stats(&self) -> Vec<ErrorPattern> {
        self.patterns.values().cloned().collect()
    }

    /// Correlate errors / Koreluj błędy
    pub fn correlate_errors(&self, time_window: Duration) -> Vec<Vec<DetectedError>> {
        let mut correlations: Vec<Vec<DetectedError>> = Vec::new();
        let now = Utc::now();

        let recent: Vec<_> = self
            .recent_errors
            .iter()
            .filter(|e| now - e.timestamp < time_window)
            .collect();

        // Group by source / Grupuj po źródle
        let mut by_source: HashMap<String, Vec<DetectedError>> = HashMap::new();
        for error in recent {
            by_source
                .entry(error.source.clone())
                .or_insert_with(Vec::new)
                .push(error.clone());
        }

        // Find correlated errors (multiple errors from same source) / Znajdź skorelowane błędy
        for (_, errors) in by_source {
            if errors.len() > 1 {
                correlations.push(errors);
            }
        }

        correlations
    }

    /// Predict potential issues / Przewiduj potencjalne problemy
    pub fn predict_issues(&self) -> Vec<String> {
        let mut predictions = Vec::new();

        // Check error rate trends / Sprawdź trendy błędów
        for (category, count) in &self.error_counts {
            if *count > 100 {
                predictions.push(format!(
                    "High {} error count: {} errors detected",
                    format!("{:?}", category),
                    count
                ));
            }
        }

        // Check for anomalies / Sprawdź anomalie
        let anomalies = self.detect_anomalies();
        for anomaly in anomalies {
            predictions.push(anomaly.description);
        }

        predictions
    }
}

impl Default for ErrorDetectionEngine {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_detect_timeout() {
        let mut engine = ErrorDetectionEngine::new();
        let error = engine.detect_error("Request timeout after 30s", "api_client");
        assert!(error.is_some());
        assert_eq!(error.unwrap().category, ErrorCategory::Timeout);
    }

    #[test]
    fn test_detect_auth_failure() {
        let mut engine = ErrorDetectionEngine::new();
        let error = engine.detect_error("401 Unauthorized: Invalid token", "api_client");
        assert!(error.is_some());
        assert_eq!(error.unwrap().category, ErrorCategory::Authentication);
    }

    #[test]
    fn test_anomaly_detection() {
        let mut engine = ErrorDetectionEngine::new();
        engine.set_anomaly_threshold("error_rate", 2.0);

        // Record normal values / Zapisz normalne wartości
        for _ in 0..20 {
            engine.record_metric("error_rate", 5.0);
        }

        // Record anomalous value / Zapisz anomalię
        engine.record_metric("error_rate", 50.0);

        let anomalies = engine.detect_anomalies();
        assert!(!anomalies.is_empty());
    }

    #[test]
    fn test_error_statistics() {
        let mut engine = ErrorDetectionEngine::new();
        engine.detect_error("Timeout error", "source1");
        engine.detect_error("Another timeout", "source2");
        engine.detect_error("Auth failure", "source1");

        let stats = engine.get_statistics();
        assert_eq!(*stats.get(&ErrorCategory::Timeout).unwrap_or(&0), 2);
        assert_eq!(*stats.get(&ErrorCategory::Authentication).unwrap_or(&0), 1);
    }
}
