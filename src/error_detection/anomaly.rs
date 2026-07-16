//! Advanced Anomaly Detection / Zaawansowana Detekcja Anomalii
//! Statistical analysis and ML-ready anomaly detection

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::collections::{HashMap, VecDeque};
use tracing::{debug, info, warn};

/// Anomaly type / Typ anomalii
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum AnomalyType {
    Spike,           // Sudden increase / Nagły wzrost
    Drop,            // Sudden decrease / Nagły spadek
    Trend,           // Gradual change / Stopniowa zmiana
    Seasonality,     // Seasonal pattern / Wzorzec sezonowy
    Outlier,         // Statistical outlier / Statystyczny outlier
}

/// Anomaly detection result / Wynik detekcji anomalii
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AdvancedAnomaly {
    pub id: String,
    pub timestamp: DateTime<Utc>,
    pub metric_name: String,
    pub anomaly_type: AnomalyType,
    pub severity: f64, // 0.0 to 1.0
    pub confidence: f64, // 0.0 to 1.0
    pub value: f64,
    pub expected_range: (f64, f64),
    pub description: String,
    pub metadata: HashMap<String, String>,
}

/// Time series data / Dane szeregów czasowych
#[derive(Debug, Clone)]
pub struct TimeSeries {
    pub name: String,
    pub data: VecDeque<(DateTime<Utc>, f64)>,
    pub max_size: usize,
}

impl TimeSeries {
    pub fn new(name: &str, max_size: usize) -> Self {
        Self {
            name: name.to_string(),
            data: VecDeque::new(),
            max_size,
        }
    }

    pub fn add_point(&mut self, timestamp: DateTime<Utc>, value: f64) {
        self.data.push_back((timestamp, value));
        if self.data.len() > self.max_size {
            self.data.pop_front();
        }
    }

    pub fn get_values(&self) -> Vec<f64> {
        self.data.iter().map(|(_, v)| *v).collect()
    }

    pub fn mean(&self) -> f64 {
        if self.data.is_empty() {
            return 0.0;
        }
        let sum: f64 = self.get_values().iter().sum();
        sum / self.data.len() as f64
    }

    pub fn standard_deviation(&self) -> f64 {
        if self.data.len() < 2 {
            return 0.0;
        }
        let mean = self.mean();
        let variance = self
            .get_values()
            .iter()
            .map(|x| (x - mean).powi(2))
            .sum::<f64>()
            / (self.data.len() - 1) as f64;
        variance.sqrt()
    }

    pub fn percentiles(&self, percentiles: &[f64]) -> HashMap<f64, f64> {
        let mut sorted = self.get_values();
        sorted.sort_by(|a, b| a.partial_cmp(b).unwrap());

        let mut result = HashMap::new();
        for p in percentiles {
            let index = ((*p / 100.0) * (sorted.len() - 1) as f64).round() as usize;
            result.insert(*p, sorted[index]);
        }
        result
    }
}

/// Advanced Anomaly Detector / Zaawansowany Detektor Anomalii
pub struct AnomalyDetector {
    time_series: HashMap<String, TimeSeries>,
    detection_methods: Vec<DetectionMethod>,
    alerts: Vec<AdvancedAnomaly>,
    max_alerts: usize,
}

/// Detection method / Metoda detekcji
#[derive(Debug, Clone)]
enum DetectionMethod {
    ZScore { threshold: f64 },
    IQR { multiplier: f64 },
    MovingAverage { window: usize, threshold: f64 },
    ExponentialSmoothing { alpha: f64, threshold: f64 },
}

impl AnomalyDetector {
    /// Create new anomaly detector / Utwórz nowy detektor
    pub fn new() -> Self {
        let detector = Self {
            time_series: HashMap::new(),
            detection_methods: vec![
                DetectionMethod::ZScore { threshold: 3.0 },
                DetectionMethod::IQR { multiplier: 1.5 },
                DetectionMethod::MovingAverage {
                    window: 10,
                    threshold: 2.0,
                },
            ],
            alerts: Vec::new(),
            max_alerts: 10000,
        };

        info!("Initialized advanced anomaly detector with {} methods", detector.detection_methods.len());
        detector
    }

    /// Add metric to track / Dodaj metrykę do śledzenia
    pub fn track_metric(&mut self, metric_name: &str, max_history: usize) {
        self.time_series
            .insert(metric_name.to_string(), TimeSeries::new(metric_name, max_history));
    }

    /// Record metric value / Zapisz wartość metryki
    pub fn record(&mut self, metric_name: &str, value: f64) {
        if let Some(series) = self.time_series.get_mut(metric_name) {
            series.add_point(Utc::now(), value);
        }
    }

    /// Detect anomalies using all methods / Wykryj anomalie wszystkimi metodami
    pub fn detect(&mut self) -> Vec<AdvancedAnomaly> {
        let mut anomalies = Vec::new();

        for (metric_name, series) in &self.time_series {
            if series.data.len() < 10 {
                continue; // Need minimum data / Potrzeba minimum danych
            }

            for method in &self.detection_methods {
                if let Some(anomaly) = self.detect_with_method(metric_name, series, method) {
                    anomalies.push(anomaly);
                }
            }
        }

        // Store alerts / Zapisz alerty
        for anomaly in &anomalies {
            self.alerts.push(anomaly.clone());
            if self.alerts.len() > self.max_alerts {
                self.alerts.remove(0);
            }
        }

        if !anomalies.is_empty() {
            warn!("Detected {} anomalies", anomalies.len());
        }

        anomalies
    }

    /// Detect using specific method / Wykryj konkretną metodą
    fn detect_with_method(
        &self,
        metric_name: &str,
        series: &TimeSeries,
        method: &DetectionMethod,
    ) -> Option<AdvancedAnomaly> {
        match method {
            DetectionMethod::ZScore { threshold } => self.z_score_detection(metric_name, series, *threshold),
            DetectionMethod::IQR { multiplier } => self.iqr_detection(metric_name, series, *multiplier),
            DetectionMethod::MovingAverage { window, threshold } => {
                self.moving_average_detection(metric_name, series, *window, *threshold)
            }
            DetectionMethod::ExponentialSmoothing { alpha, threshold } => {
                self.exponential_smoothing_detection(metric_name, series, *alpha, *threshold)
            }
        }
    }

    /// Z-score based detection / Detekcja oparta na Z-score
    fn z_score_detection(
        &self,
        metric_name: &str,
        series: &TimeSeries,
        threshold: f64,
    ) -> Option<AdvancedAnomaly> {
        let values = series.get_values();
        let current = *values.last()?;
        let mean = series.mean();
        let std_dev = series.standard_deviation();

        if std_dev == 0.0 {
            return None;
        }

        let z_score = (current - mean).abs() / std_dev;

        if z_score > threshold {
            let severity = (z_score / (threshold * 2.0)).min(1.0);
            let confidence = (z_score / threshold).min(1.0);

            let anomaly_type = if current > mean {
                AnomalyType::Spike
            } else {
                AnomalyType::Drop
            };

            Some(AdvancedAnomaly {
                id: uuid::Uuid::new_v4().to_string(),
                timestamp: Utc::now(),
                metric_name: metric_name.to_string(),
                anomaly_type,
                severity,
                confidence,
                value: current,
                expected_range: (mean - threshold * std_dev, mean + threshold * std_dev),
                description: format!(
                    "Z-score anomaly: {:.2} (threshold: {:.2})",
                    z_score, threshold
                ),
                metadata: HashMap::new(),
            })
        } else {
            None
        }
    }

    /// IQR (Interquartile Range) detection / Detekcja IQR
    fn iqr_detection(
        &self,
        metric_name: &str,
        series: &TimeSeries,
        multiplier: f64,
    ) -> Option<AdvancedAnomaly> {
        let percentiles = series.percentiles(&[25.0, 50.0, 75.0]);
        let q1 = percentiles.get(&25.0)?;
        let q3 = percentiles.get(&75.0)?;
        let iqr = q3 - q1;

        let current = series.get_values().last()?;
        let lower_bound = q1 - multiplier * iqr;
        let upper_bound = q3 + multiplier * iqr;

        if current < &lower_bound || current > &upper_bound {
            let deviation = if current < &lower_bound {
                (lower_bound - current) / iqr
            } else {
                (current - upper_bound) / iqr
            };

            let severity = (deviation / (multiplier * 2.0)).min(1.0);
            let confidence = 0.8; // IQR is reliable / IQR jest wiarygodne

            let anomaly_type = if current > &upper_bound {
                AnomalyType::Spike
            } else {
                AnomalyType::Drop
            };

            Some(AdvancedAnomaly {
                id: uuid::Uuid::new_v4().to_string(),
                timestamp: Utc::now(),
                metric_name: metric_name.to_string(),
                anomaly_type,
                severity,
                confidence,
                value: *current,
                expected_range: (lower_bound, upper_bound),
                description: format!(
                    "IQR anomaly: value {:.2} outside [{:.2}, {:.2}]",
                    current, lower_bound, upper_bound
                ),
                metadata: HashMap::new(),
            })
        } else {
            None
        }
    }

    /// Moving average detection / Detekcja średniej kroczącej
    fn moving_average_detection(
        &self,
        metric_name: &str,
        series: &TimeSeries,
        window: usize,
        threshold: f64,
    ) -> Option<AdvancedAnomaly> {
        let values = series.get_values();
        if values.len() < window {
            return None;
        }

        let current = *values.last()?;
        let window_values = &values[values.len() - window..];
        let ma: f64 = window_values.iter().sum::<f64>() / window as f64;

        let deviation = (current - ma).abs() / ma;

        if deviation > threshold {
            let severity = (deviation / (threshold * 2.0)).min(1.0);
            let confidence = 0.7;

            let anomaly_type = if current > ma {
                AnomalyType::Spike
            } else {
                AnomalyType::Drop
            };

            Some(AdvancedAnomaly {
                id: uuid::Uuid::new_v4().to_string(),
                timestamp: Utc::now(),
                metric_name: metric_name.to_string(),
                anomaly_type,
                severity,
                confidence,
                value: current,
                expected_range: (ma * (1.0 - threshold), ma * (1.0 + threshold)),
                description: format!(
                    "Moving average anomaly: {:.2} dev from MA {:.2}",
                    deviation, ma
                ),
                metadata: HashMap::new(),
            })
        } else {
            None
        }
    }

    /// Exponential smoothing detection / Detekcja z wygładzaniem wykładniczym
    fn exponential_smoothing_detection(
        &self,
        metric_name: &str,
        series: &TimeSeries,
        alpha: f64,
        threshold: f64,
    ) -> Option<AdvancedAnomaly> {
        let values = series.get_values();
        if values.is_empty() {
            return None;
        }

        let mut smoothed = values[0];
        for &value in &values[1..] {
            smoothed = alpha * value + (1.0 - alpha) * smoothed;
        }

        let current = *values.last()?;
        let deviation = (current - smoothed).abs() / smoothed;

        if deviation > threshold {
            let severity = (deviation / (threshold * 2.0)).min(1.0);
            let confidence = 0.75;

            let anomaly_type = if current > smoothed {
                AnomalyType::Spike
            } else {
                AnomalyType::Drop
            };

            Some(AdvancedAnomaly {
                id: uuid::Uuid::new_v4().to_string(),
                timestamp: Utc::now(),
                metric_name: metric_name.to_string(),
                anomaly_type,
                severity,
                confidence,
                value: current,
                expected_range: (smoothed * (1.0 - threshold), smoothed * (1.0 + threshold)),
                description: format!(
                    "Exponential smoothing anomaly: {:.2} dev from smoothed {:.2}",
                    deviation, smoothed
                ),
                metadata: HashMap::new(),
            })
        } else {
            None
        }
    }

    /// Get recent alerts / Pobierz ostatnie alerty
    pub fn get_recent_alerts(&self, limit: usize) -> Vec<AdvancedAnomaly> {
        self.alerts.iter().rev().take(limit).cloned().collect()
    }

    /// Clear all alerts / Wyczyść wszystkie alerty
    pub fn clear_alerts(&mut self) {
        self.alerts.clear();
    }
}

impl Default for AnomalyDetector {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_z_score_detection() {
        let mut detector = AnomalyDetector::new();
        detector.track_metric("test_metric", 100);

        // Add normal values / Dodaj normalne wartości
        for _ in 0..20 {
            detector.record("test_metric", 10.0);
        }

        // Add outlier / Dodaj outlier
        detector.record("test_metric", 100.0);

        let anomalies = detector.detect();
        assert!(!anomalies.is_empty());
    }

    #[test]
    fn test_iqr_detection() {
        let mut detector = AnomalyDetector::new();
        detector.track_metric("test_metric", 100);

        // Add consistent values / Dodaj spójne wartości
        for _ in 0..20 {
            detector.record("test_metric", 50.0);
        }

        // Add extreme value / Dodaj ekstremalną wartość
        detector.record("test_metric", 200.0);

        let anomalies = detector.detect();
        assert!(!anomalies.is_empty());
    }

    #[test]
    fn test_time_series_statistics() {
        let mut series = TimeSeries::new("test", 100);
        for i in 0..10 {
            series.add_point(Utc::now(), i as f64);
        }

        assert_eq!(series.mean(), 4.5);
        assert!(series.standard_deviation() > 0.0);
    }
}
