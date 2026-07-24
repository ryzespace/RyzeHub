//! Hub platform runtime.
//! Provides a unified in-memory platform layer for realtime events,
//! notifications, audit logging, permissions, sessions, devices,
//! presence, gateway, cache, activity, messaging, feature flags,
//! monitoring, telemetry, security and file transfer.

use std::collections::{HashMap, HashSet};
use std::sync::{Arc, Mutex};

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use uuid::Uuid;

use crate::config::HubPlatformConfig;

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "snake_case")]
pub enum RealtimeEventType {
    SupportStatusUpdated,
    PaymentCompleted,
    ServerActivated,
    SecurityWarning,
    NewMessage,
    LoginRecorded,
    SessionCreated,
    DeviceDetected,
    PermissionChanged,
    AuditRecorded,
    FileTransferred,
    HealthChanged,
    FeatureFlagUpdated,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "snake_case")]
pub enum NotificationChannel {
    MobilePush,
    Desktop,
    Email,
    Sms,
    DiscordWebhook,
    SlackWebhook,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "snake_case")]
pub enum NotificationPriority {
    Low,
    Medium,
    High,
    Critical,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "snake_case")]
pub enum PresenceStatus {
    Online,
    Offline,
    Away,
    Busy,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "snake_case")]
pub enum ClientPlatform {
    Web,
    Mobile,
    Desktop,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "snake_case")]
pub enum AuditCategory {
    Authentication,
    Settings,
    Administration,
    Finance,
    Permission,
    Security,
    Support,
    Messaging,
    Infrastructure,
}

#[derive(Debug, Clone, Copy, Serialize, Deserialize, PartialEq, Eq, Hash)]
#[serde(rename_all = "snake_case")]
pub enum SecuritySeverity {
    Info,
    Warning,
    Critical,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RealtimeEvent {
    pub event_id: String,
    pub topic: String,
    pub event_type: RealtimeEventType,
    pub actor_id: String,
    pub subject_id: String,
    pub recipients: Vec<String>,
    pub payload: Value,
    pub timestamp: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NotificationEndpoint {
    pub channel: NotificationChannel,
    pub enabled: bool,
    pub target: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct NotificationRecord {
    pub notification_id: String,
    pub user_id: String,
    pub title: String,
    pub message: String,
    pub channels: Vec<NotificationChannel>,
    pub priority: NotificationPriority,
    pub metadata: Value,
    pub delivered: bool,
    pub created_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AuditLogEntry {
    pub entry_id: String,
    pub actor_id: String,
    pub action: String,
    pub category: AuditCategory,
    pub resource: String,
    pub metadata: Value,
    pub timestamp: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RoleDefinition {
    pub name: String,
    pub permissions: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PresenceRecord {
    pub user_id: String,
    pub status: PresenceStatus,
    pub last_activity_at: DateTime<Utc>,
    pub active_devices: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct DeviceRecord {
    pub device_id: String,
    pub user_id: String,
    pub platform: ClientPlatform,
    pub device_name: String,
    pub trusted: bool,
    pub active: bool,
    pub detected_at: DateTime<Utc>,
    pub last_seen_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionRecord {
    pub session_id: String,
    pub user_id: String,
    pub device_id: String,
    pub platform: ClientPlatform,
    pub ip_address: String,
    pub active: bool,
    pub created_at: DateTime<Utc>,
    pub last_seen_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct GatewayRoute {
    pub route_id: String,
    pub path: String,
    pub upstream_service: String,
    pub auth_required: bool,
    pub cache_enabled: bool,
    pub rate_limit_per_minute: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CacheEntry {
    pub key: String,
    pub value: Value,
    pub ttl_seconds: u64,
    pub stored_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ActivityFeedEntry {
    pub entry_id: String,
    pub user_id: String,
    pub description: String,
    pub timestamp: DateTime<Utc>,
    pub metadata: Value,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InternalMessage {
    pub message_id: String,
    pub thread_id: String,
    pub from_user: String,
    pub to_user: String,
    pub body: String,
    pub created_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FeatureFlag {
    pub key: String,
    pub enabled: bool,
    pub description: String,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct MonitoredService {
    pub name: String,
    pub healthy: bool,
    pub latency_ms: u64,
    pub updated_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize, Default)]
pub struct TelemetryOverview {
    pub active_users: u64,
    pub api_requests: u64,
    pub api_errors: u64,
    pub emitted_events: u64,
    pub delivered_notifications: u64,
    pub logins: u64,
    pub file_transfers: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SecurityAlert {
    pub alert_id: String,
    pub severity: SecuritySeverity,
    pub title: String,
    pub description: String,
    pub user_id: Option<String>,
    pub created_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FileTransferRecord {
    pub transfer_id: String,
    pub owner_id: String,
    pub file_name: String,
    pub encrypted: bool,
    pub scanned: bool,
    pub versioned: bool,
    pub created_at: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EventSubscription {
    pub topic: String,
    pub consumers: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PlatformModuleDefinition {
    pub key: String,
    pub title: String,
    pub summary: String,
    pub examples: Vec<String>,
    pub benefits: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HubHealthStatus {
    pub status: String,
    pub monitored_services: usize,
    pub healthy_services: usize,
    pub degraded_services: usize,
    pub active_sessions: usize,
    pub queued_notifications: usize,
    pub emitted_events: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HubPlatformSnapshot {
    pub generated_at: DateTime<Utc>,
    pub enabled_modules: Vec<String>,
    pub module_catalog: Vec<PlatformModuleDefinition>,
    pub roles: Vec<RoleDefinition>,
    pub feature_flags: Vec<FeatureFlag>,
    pub subscriptions: Vec<EventSubscription>,
    pub gateway_routes: Vec<GatewayRoute>,
    pub services: Vec<MonitoredService>,
    pub telemetry: TelemetryOverview,
    pub health: HubHealthStatus,
    pub counters: HubPlatformCounters,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HubPlatformCounters {
    pub audit_entries: usize,
    pub notifications: usize,
    pub presence_records: usize,
    pub devices: usize,
    pub sessions: usize,
    pub activity_entries: usize,
    pub messages: usize,
    pub cache_entries: usize,
    pub security_alerts: usize,
    pub file_transfers: usize,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UserAccessProfile {
    pub user_id: String,
    pub roles: Vec<String>,
    pub direct_permissions: Vec<String>,
    pub effective_permissions: Vec<String>,
}

#[derive(Debug, Default)]
struct HubPlatformState {
    events: Vec<RealtimeEvent>,
    notification_endpoints: Vec<NotificationEndpoint>,
    notifications: Vec<NotificationRecord>,
    audit_logs: Vec<AuditLogEntry>,
    roles: HashMap<String, RoleDefinition>,
    user_roles: HashMap<String, HashSet<String>>,
    direct_permissions: HashMap<String, HashSet<String>>,
    presence: HashMap<String, PresenceRecord>,
    devices: HashMap<String, DeviceRecord>,
    sessions: HashMap<String, SessionRecord>,
    gateway_routes: Vec<GatewayRoute>,
    cache: HashMap<String, CacheEntry>,
    activity_feed: Vec<ActivityFeedEntry>,
    messages: Vec<InternalMessage>,
    feature_flags: HashMap<String, FeatureFlag>,
    services: HashMap<String, MonitoredService>,
    telemetry: TelemetryOverview,
    security_alerts: Vec<SecurityAlert>,
    file_transfers: Vec<FileTransferRecord>,
    subscriptions: Vec<EventSubscription>,
}

#[derive(Clone)]
pub struct HubPlatform {
    config: HubPlatformConfig,
    state: Arc<Mutex<HubPlatformState>>,
}

impl HubPlatform {
    pub fn new(config: HubPlatformConfig) -> Self {
        let platform = Self {
            config,
            state: Arc::new(Mutex::new(HubPlatformState::default())),
        };
        platform.bootstrap_standard_topology();
        platform
    }

    pub fn enabled_modules() -> Vec<String> {
        Self::module_catalog()
            .into_iter()
            .map(|module| module.key)
            .collect()
    }

    pub fn module_catalog() -> Vec<PlatformModuleDefinition> {
        vec![
            PlatformModuleDefinition {
                key: "real_time_event_system".to_string(),
                title: "Real Time Event System".to_string(),
                summary: "Dostarcza zdarzenia w czasie rzeczywistym do web, mobile i desktop."
                    .to_string(),
                examples: vec![
                    "status zgłoszenia support".to_string(),
                    "zakończenie płatności".to_string(),
                    "aktywacja serwera".to_string(),
                    "ostrzeżenia bezpieczeństwa".to_string(),
                    "nowe wiadomości".to_string(),
                ],
                benefits: vec![
                    "Wszystkie aplikacje dostają ten sam strumień zdarzeń".to_string(),
                    "Łatwiejsza synchronizacja dashboardów i paneli".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "notification_center".to_string(),
                title: "Notification Center".to_string(),
                summary: "Centralizuje wysyłkę i zarządzanie powiadomieniami dla wszystkich kanałów."
                    .to_string(),
                examples: vec![
                    "mobile push".to_string(),
                    "desktop notifications".to_string(),
                    "email".to_string(),
                    "sms".to_string(),
                    "discord webhooks".to_string(),
                    "slack webhooks".to_string(),
                ],
                benefits: vec![
                    "Użytkownik zarządza wszystkimi powiadomieniami z jednego miejsca"
                        .to_string(),
                    "Łatwiejsze reguły routingu i priorytetów".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "audit_log_engine".to_string(),
                title: "Audit Log Engine".to_string(),
                summary: "Rejestruje krytyczne działania użytkowników, systemów i administratorów."
                    .to_string(),
                examples: vec![
                    "logowania".to_string(),
                    "zmiany ustawień".to_string(),
                    "operacje administracyjne".to_string(),
                    "operacje finansowe".to_string(),
                    "zmiany uprawnień".to_string(),
                ],
                benefits: vec![
                    "Większe zaufanie do platformy".to_string(),
                    "Szybsze diagnozowanie incydentów i regresji".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "permission_role_hub".to_string(),
                title: "Permission & Role Hub".to_string(),
                summary: "Łączy role biznesowe z granularnymi uprawnieniami operacyjnymi."
                    .to_string(),
                examples: vec![
                    "User".to_string(),
                    "Seller".to_string(),
                    "Moderator".to_string(),
                    "Support".to_string(),
                    "Admin".to_string(),
                    "SuperAdmin".to_string(),
                    "server:create".to_string(),
                    "server:delete".to_string(),
                    "billing:view".to_string(),
                    "billing:manage".to_string(),
                ],
                benefits: vec![
                    "Skalowalny model dostępu dla nowych produktów".to_string(),
                    "Precyzyjna kontrola operacji administracyjnych i finansowych".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "presence_system".to_string(),
                title: "Presence System".to_string(),
                summary: "Śledzi stan użytkownika, aktywność i aktywne urządzenia.".to_string(),
                examples: vec![
                    "online/offline".to_string(),
                    "ostatnia aktywność".to_string(),
                    "aktywne urządzenia".to_string(),
                ],
                benefits: vec![
                    "Lepsza obsługa zgłoszeń i komunikacji".to_string(),
                    "Dodatkowy sygnał dla bezpieczeństwa konta".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "device_management".to_string(),
                title: "Device Management".to_string(),
                summary: "Zapewnia pełny panel zarządzania urządzeniami w stylu Google lub Steam."
                    .to_string(),
                examples: vec![
                    "lista urządzeń".to_string(),
                    "wylogowanie urządzenia".to_string(),
                    "zaufane urządzenia".to_string(),
                    "wykrywanie nowych urządzeń".to_string(),
                ],
                benefits: vec![
                    "Jedno miejsce kontroli urządzeń użytkownika".to_string(),
                    "Szybsza reakcja na podejrzane aktywności".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "session_manager".to_string(),
                title: "Session Manager".to_string(),
                summary: "Centralnie zarządza sesjami web, mobile i desktop.".to_string(),
                examples: vec![
                    "sesje web".to_string(),
                    "sesje mobile".to_string(),
                    "sesje desktop".to_string(),
                ],
                benefits: vec![
                    "Jedno miejsce kontroli dostępu".to_string(),
                    "Spójne wymuszanie polityk bezpieczeństwa".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "api_gateway".to_string(),
                title: "API Gateway".to_string(),
                summary: "Udostępnia hub jako pojedynczy punkt wejścia do mikroserwisów."
                    .to_string(),
                examples: vec![
                    "rate limiting".to_string(),
                    "auth".to_string(),
                    "monitoring".to_string(),
                    "caching".to_string(),
                ],
                benefits: vec![
                    "Uproszczony routing ruchu klienta".to_string(),
                    "Jednolite egzekwowanie autoryzacji i limitów".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "distributed_cache".to_string(),
                title: "Distributed Cache".to_string(),
                summary: "Przyspiesza platformę przez cache współdzielony między usługami."
                    .to_string(),
                examples: vec![
                    "redis".to_string(),
                    "sesje".to_string(),
                    "ustawienia".to_string(),
                    "często używane dane".to_string(),
                ],
                benefits: vec![
                    "Szybsze dashboardy i API".to_string(),
                    "Mniejsze obciążenie usług źródłowych".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "activity_feed".to_string(),
                title: "Activity Feed".to_string(),
                summary: "Buduje oś czasu działań użytkownika i systemu.".to_string(),
                examples: vec![
                    "utworzono VPS".to_string(),
                    "dodano metodę płatności".to_string(),
                    "wysłano zgłoszenie".to_string(),
                ],
                benefits: vec![
                    "Pełna przejrzystość działań na koncie".to_string(),
                    "Lepszy kontekst dla supportu i adminów".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "internal_messaging".to_string(),
                title: "Internal Messaging".to_string(),
                summary: "Udostępnia komunikację wewnętrzną bez opuszczania platformy."
                    .to_string(),
                examples: vec![
                    "Client ↔ Support".to_string(),
                    "Client ↔ Admin".to_string(),
                    "Admin ↔ Moderator".to_string(),
                ],
                benefits: vec![
                    "Komunikacja pozostaje w jednym ekosystemie".to_string(),
                    "Łatwiejsze powiązanie rozmów ze zgłoszeniami i audytem".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "feature_flags".to_string(),
                title: "Feature Flags".to_string(),
                summary: "Pozwala włączać i wyłączać funkcje bez nowego deployu.".to_string(),
                examples: vec![
                    "betaBilling=true".to_string(),
                    "newDashboard=false".to_string(),
                ],
                benefits: vec![
                    "Bezpieczne rollouty nowych funkcji".to_string(),
                    "Lepsze eksperymenty i kontrola beta testów".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "health_monitoring".to_string(),
                title: "Health Monitoring".to_string(),
                summary: "Monitoruje stan API, baz danych, mikroserwisów i kolejek."
                    .to_string(),
                examples: vec![
                    "API".to_string(),
                    "bazy danych".to_string(),
                    "mikroserwisy".to_string(),
                    "kolejki".to_string(),
                ],
                benefits: vec![
                    "Szybsze wykrywanie awarii".to_string(),
                    "Lepsza podstawa do automatycznych alertów".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "telemetry_analytics".to_string(),
                title: "Telemetry & Analytics".to_string(),
                summary: "Zbiera metryki użycia produktu i wydajności systemu.".to_string(),
                examples: vec![
                    "liczba aktywnych użytkowników".to_string(),
                    "obciążenie systemu".to_string(),
                    "czas odpowiedzi API".to_string(),
                    "błędy".to_string(),
                ],
                benefits: vec![
                    "Lepsza optymalizacja produktu".to_string(),
                    "Decyzje oparte na danych zamiast intuicji".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "security_center".to_string(),
                title: "Security Center".to_string(),
                summary: "Daje użytkownikowi pełny panel bezpieczeństwa konta.".to_string(),
                examples: vec![
                    "historia logowań".to_string(),
                    "aktywne sesje".to_string(),
                    "2FA".to_string(),
                    "klucze API".to_string(),
                    "alerty bezpieczeństwa".to_string(),
                ],
                benefits: vec![
                    "Większa kontrola nad kontem".to_string(),
                    "Szybsza reakcja na ryzykowne zdarzenia".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "event_bus".to_string(),
                title: "Event Bus".to_string(),
                summary: "Hub działa jako broker zdarzeń spinający luźno powiązane mikroserwisy."
                    .to_string(),
                examples: vec![
                    "PaymentCompleted → Billing Service".to_string(),
                    "PaymentCompleted → Notification Service".to_string(),
                    "PaymentCompleted → Admin Dashboard".to_string(),
                    "PaymentCompleted → User Dashboard".to_string(),
                ],
                benefits: vec![
                    "Luźniejsze powiązanie serwisów".to_string(),
                    "Łatwiejsze rozszerzanie ekosystemu RyzeSpace".to_string(),
                ],
            },
            PlatformModuleDefinition {
                key: "file_transfer_service".to_string(),
                title: "File Transfer Service".to_string(),
                summary: "Obsługuje bezpieczne przesyłanie plików między modułami platformy."
                    .to_string(),
                examples: vec![
                    "załączniki zgłoszeń".to_string(),
                    "dokumenty".to_string(),
                    "logi".to_string(),
                    "backupy".to_string(),
                    "skanowanie plików".to_string(),
                    "szyfrowanie".to_string(),
                    "wersjonowanie".to_string(),
                ],
                benefits: vec![
                    "Bezpieczny przepływ plików przez cały ekosystem".to_string(),
                    "Lepsza zgodność i śledzenie wersji danych".to_string(),
                ],
            },
        ]
    }

    pub fn bootstrap_standard_topology(&self) {
        let mut state = self.state.lock().unwrap();
        if !state.roles.is_empty() {
            return;
        }

        for endpoint in [
            NotificationEndpoint {
                channel: NotificationChannel::MobilePush,
                enabled: true,
                target: "mobile://push".to_string(),
            },
            NotificationEndpoint {
                channel: NotificationChannel::Desktop,
                enabled: true,
                target: "desktop://notifications".to_string(),
            },
            NotificationEndpoint {
                channel: NotificationChannel::Email,
                enabled: true,
                target: "smtp://primary".to_string(),
            },
            NotificationEndpoint {
                channel: NotificationChannel::Sms,
                enabled: true,
                target: "sms://primary".to_string(),
            },
            NotificationEndpoint {
                channel: NotificationChannel::DiscordWebhook,
                enabled: true,
                target: "discord://hub-notifications".to_string(),
            },
            NotificationEndpoint {
                channel: NotificationChannel::SlackWebhook,
                enabled: true,
                target: "slack://hub-notifications".to_string(),
            },
        ] {
            state.notification_endpoints.push(endpoint);
        }

        for role in default_roles() {
            state.roles.insert(role.name.clone(), role);
        }

        for flag in default_feature_flags() {
            state.feature_flags.insert(flag.key.clone(), flag);
        }

        state.gateway_routes = vec![
            GatewayRoute {
                route_id: Uuid::new_v4().to_string(),
                path: format!("{}/client", self.config.gateway_base_path),
                upstream_service: "RyzeSpace.Client".to_string(),
                auth_required: true,
                cache_enabled: true,
                rate_limit_per_minute: self.config.gateway_rate_limit,
            },
            GatewayRoute {
                route_id: Uuid::new_v4().to_string(),
                path: format!("{}/helpcenter", self.config.gateway_base_path),
                upstream_service: "RyzeSpace.HelpCenter".to_string(),
                auth_required: true,
                cache_enabled: true,
                rate_limit_per_minute: self.config.gateway_rate_limit,
            },
            GatewayRoute {
                route_id: Uuid::new_v4().to_string(),
                path: format!("{}/admin", self.config.gateway_base_path),
                upstream_service: "RyzeSpace.AdminPanel".to_string(),
                auth_required: true,
                cache_enabled: false,
                rate_limit_per_minute: self.config.gateway_rate_limit,
            },
        ];

        for service in [
            "api_gateway",
            "event_bus",
            "notification_center",
            "security_center",
            "distributed_cache",
            "RyzeSpace.Client",
            "RyzeSpace.HelpCenter",
        ] {
            state.services.insert(
                service.to_string(),
                MonitoredService {
                    name: service.to_string(),
                    healthy: true,
                    latency_ms: 0,
                    updated_at: Utc::now(),
                },
            );
        }

        state.subscriptions = vec![
            EventSubscription {
                topic: "payment.completed".to_string(),
                consumers: vec![
                    "billing_service".to_string(),
                    "notification_center".to_string(),
                    "admin_dashboard".to_string(),
                    "user_dashboard".to_string(),
                ],
            },
            EventSubscription {
                topic: "support.status_updated".to_string(),
                consumers: vec![
                    "helpcenter".to_string(),
                    "client_dashboard".to_string(),
                    "notification_center".to_string(),
                ],
            },
            EventSubscription {
                topic: "security.warning".to_string(),
                consumers: vec![
                    "security_center".to_string(),
                    "notification_center".to_string(),
                    "audit_log_engine".to_string(),
                ],
            },
            EventSubscription {
                topic: "server.activated".to_string(),
                consumers: vec![
                    "infrastructure_service".to_string(),
                    "notification_center".to_string(),
                    "activity_feed".to_string(),
                ],
            },
        ];
    }

    pub fn snapshot(&self) -> HubPlatformSnapshot {
        let state = self.state.lock().unwrap();
        let mut roles: Vec<RoleDefinition> = state.roles.values().cloned().collect();
        roles.sort_by(|a, b| a.name.cmp(&b.name));

        let mut feature_flags: Vec<FeatureFlag> = state.feature_flags.values().cloned().collect();
        feature_flags.sort_by(|a, b| a.key.cmp(&b.key));

        let mut services: Vec<MonitoredService> = state.services.values().cloned().collect();
        services.sort_by(|a, b| a.name.cmp(&b.name));

        HubPlatformSnapshot {
            generated_at: Utc::now(),
            enabled_modules: Self::enabled_modules(),
            module_catalog: Self::module_catalog(),
            roles,
            feature_flags,
            subscriptions: state.subscriptions.clone(),
            gateway_routes: state.gateway_routes.clone(),
            services,
            telemetry: state.telemetry.clone(),
            health: hub_health_from_state(&state),
            counters: HubPlatformCounters {
                audit_entries: state.audit_logs.len(),
                notifications: state.notifications.len(),
                presence_records: state.presence.len(),
                devices: state.devices.len(),
                sessions: state.sessions.len(),
                activity_entries: state.activity_feed.len(),
                messages: state.messages.len(),
                cache_entries: state.cache.len(),
                security_alerts: state.security_alerts.len(),
                file_transfers: state.file_transfers.len(),
            },
        }
    }

    pub fn health_status(&self) -> HubHealthStatus {
        let state = self.state.lock().unwrap();
        hub_health_from_state(&state)
    }

    pub fn list_module_catalog(&self) -> Vec<PlatformModuleDefinition> {
        Self::module_catalog()
    }

    pub fn list_events(&self, limit: usize) -> Vec<RealtimeEvent> {
        let state = self.state.lock().unwrap();
        recent_from_slice(&state.events, limit)
    }

    pub fn list_notification_endpoints(&self) -> Vec<NotificationEndpoint> {
        let state = self.state.lock().unwrap();
        let mut endpoints = state.notification_endpoints.clone();
        endpoints.sort_by(|a, b| channel_key(a.channel).cmp(channel_key(b.channel)));
        endpoints
    }

    pub fn list_notifications(
        &self,
        user_id: Option<&str>,
        limit: usize,
    ) -> Vec<NotificationRecord> {
        let state = self.state.lock().unwrap();
        recent_filtered(&state.notifications, limit, |record| {
            user_id.map(|value| record.user_id == value).unwrap_or(true)
        })
    }

    pub fn list_audit_logs(&self, actor_id: Option<&str>, limit: usize) -> Vec<AuditLogEntry> {
        let state = self.state.lock().unwrap();
        recent_filtered(&state.audit_logs, limit, |entry| {
            actor_id.map(|value| entry.actor_id == value).unwrap_or(true)
        })
    }

    pub fn list_roles(&self) -> Vec<RoleDefinition> {
        let state = self.state.lock().unwrap();
        let mut roles: Vec<RoleDefinition> = state.roles.values().cloned().collect();
        roles.sort_by(|a, b| a.name.cmp(&b.name));
        roles
    }

    pub fn access_profile(&self, user_id: &str) -> UserAccessProfile {
        let state = self.state.lock().unwrap();
        let mut roles = state
            .user_roles
            .get(user_id)
            .cloned()
            .unwrap_or_default()
            .into_iter()
            .collect::<Vec<_>>();
        roles.sort();

        let mut direct_permissions = state
            .direct_permissions
            .get(user_id)
            .cloned()
            .unwrap_or_default()
            .into_iter()
            .collect::<Vec<_>>();
        direct_permissions.sort();
        drop(state);

        UserAccessProfile {
            user_id: user_id.to_string(),
            roles,
            direct_permissions,
            effective_permissions: self.permissions_for_user(user_id),
        }
    }

    pub fn list_presence(&self, user_id: Option<&str>) -> Vec<PresenceRecord> {
        let state = self.state.lock().unwrap();
        let mut presence: Vec<PresenceRecord> = state
            .presence
            .values()
            .filter(|record| user_id.map(|value| record.user_id == value).unwrap_or(true))
            .cloned()
            .collect();
        presence.sort_by(|a, b| b.last_activity_at.cmp(&a.last_activity_at));
        presence
    }

    pub fn list_devices(&self, user_id: Option<&str>) -> Vec<DeviceRecord> {
        let state = self.state.lock().unwrap();
        let mut devices: Vec<DeviceRecord> = state
            .devices
            .values()
            .filter(|record| user_id.map(|value| record.user_id == value).unwrap_or(true))
            .cloned()
            .collect();
        devices.sort_by(|a, b| b.last_seen_at.cmp(&a.last_seen_at));
        devices
    }

    pub fn list_sessions(&self, user_id: Option<&str>) -> Vec<SessionRecord> {
        let state = self.state.lock().unwrap();
        let mut sessions: Vec<SessionRecord> = state
            .sessions
            .values()
            .filter(|record| user_id.map(|value| record.user_id == value).unwrap_or(true))
            .cloned()
            .collect();
        sessions.sort_by(|a, b| b.last_seen_at.cmp(&a.last_seen_at));
        sessions
    }

    pub fn list_gateway_routes(&self) -> Vec<GatewayRoute> {
        let state = self.state.lock().unwrap();
        let mut routes = state.gateway_routes.clone();
        routes.sort_by(|a, b| a.path.cmp(&b.path));
        routes
    }

    pub fn list_cache_entries(&self) -> Vec<CacheEntry> {
        let state = self.state.lock().unwrap();
        let mut entries: Vec<CacheEntry> = state.cache.values().cloned().collect();
        entries.sort_by(|a, b| a.key.cmp(&b.key));
        entries
    }

    pub fn list_activity_feed(
        &self,
        user_id: Option<&str>,
        limit: usize,
    ) -> Vec<ActivityFeedEntry> {
        let state = self.state.lock().unwrap();
        recent_filtered(&state.activity_feed, limit, |entry| {
            user_id.map(|value| entry.user_id == value).unwrap_or(true)
        })
    }

    pub fn list_messages(&self, user_id: Option<&str>, limit: usize) -> Vec<InternalMessage> {
        let state = self.state.lock().unwrap();
        recent_filtered(&state.messages, limit, |message| {
            user_id
                .map(|value| message.from_user == value || message.to_user == value)
                .unwrap_or(true)
        })
    }

    pub fn list_feature_flags(&self) -> Vec<FeatureFlag> {
        let state = self.state.lock().unwrap();
        let mut flags: Vec<FeatureFlag> = state.feature_flags.values().cloned().collect();
        flags.sort_by(|a, b| a.key.cmp(&b.key));
        flags
    }

    pub fn list_services(&self) -> Vec<MonitoredService> {
        let state = self.state.lock().unwrap();
        let mut services: Vec<MonitoredService> = state.services.values().cloned().collect();
        services.sort_by(|a, b| a.name.cmp(&b.name));
        services
    }

    pub fn list_security_alerts(
        &self,
        user_id: Option<&str>,
        limit: usize,
    ) -> Vec<SecurityAlert> {
        let state = self.state.lock().unwrap();
        recent_filtered(&state.security_alerts, limit, |alert| {
            user_id
                .map(|value| alert.user_id.as_deref() == Some(value))
                .unwrap_or(true)
        })
    }

    pub fn list_file_transfers(
        &self,
        owner_id: Option<&str>,
        limit: usize,
    ) -> Vec<FileTransferRecord> {
        let state = self.state.lock().unwrap();
        recent_filtered(&state.file_transfers, limit, |record| {
            owner_id.map(|value| record.owner_id == value).unwrap_or(true)
        })
    }

    pub fn list_subscriptions(&self) -> Vec<EventSubscription> {
        let state = self.state.lock().unwrap();
        let mut subscriptions = state.subscriptions.clone();
        subscriptions.sort_by(|a, b| a.topic.cmp(&b.topic));
        subscriptions
    }

    pub fn send_notification(
        &self,
        user_id: &str,
        title: &str,
        message: &str,
        channels: Vec<NotificationChannel>,
        priority: NotificationPriority,
        metadata: Value,
    ) -> NotificationRecord {
        let record = NotificationRecord {
            notification_id: Uuid::new_v4().to_string(),
            user_id: user_id.to_string(),
            title: title.to_string(),
            message: message.to_string(),
            channels,
            priority,
            metadata,
            delivered: true,
            created_at: Utc::now(),
        };

        let mut state = self.state.lock().unwrap();
        state.notifications.push(record.clone());
        state.telemetry.delivered_notifications += 1;
        record
    }

    pub fn set_notification_endpoint(
        &self,
        channel: NotificationChannel,
        target: &str,
        enabled: bool,
    ) {
        let mut state = self.state.lock().unwrap();
        if let Some(endpoint) = state
            .notification_endpoints
            .iter_mut()
            .find(|endpoint| endpoint.channel == channel)
        {
            endpoint.target = target.to_string();
            endpoint.enabled = enabled;
        } else {
            state.notification_endpoints.push(NotificationEndpoint {
                channel,
                enabled,
                target: target.to_string(),
            });
        }
    }

    pub fn seed_demo_data(&self, user_id: &str) -> HubPlatformSnapshot {
        self.assign_role(user_id, "User");
        self.assign_role("support-001", "Support");
        self.assign_role("admin-001", "Admin");
        self.assign_role("super-001", "SuperAdmin");
        self.grant_permission(user_id, "billing:view");
        self.grant_permission("admin-001", "billing:manage");

        let device_id = self.register_device(user_id, "MacBook Pro", ClientPlatform::Desktop, true);
        let _session_id =
            self.create_session(user_id, &device_id, ClientPlatform::Desktop, "203.0.113.42");
        self.update_presence(user_id, PresenceStatus::Online, Some(device_id.clone()));
        self.record_login(user_id, "203.0.113.42", Some(device_id.clone()));

        self.record_support_status_update(
            user_id,
            "ticket-1001",
            "in_progress",
            vec![NotificationChannel::MobilePush, NotificationChannel::Email],
        );
        self.record_payment_completed(
            user_id,
            "payment-4001",
            129.99,
            vec![NotificationChannel::Email, NotificationChannel::DiscordWebhook],
        );
        self.record_server_activated(
            user_id,
            "srv-01",
            vec![NotificationChannel::MobilePush, NotificationChannel::Desktop],
        );
        self.record_security_warning(
            Some(user_id),
            "Nowe logowanie z nieznanego urządzenia",
            vec![NotificationChannel::Email, NotificationChannel::Sms],
        );
        self.record_internal_message(user_id, "support-001", "Potrzebuję pomocy z serwerem.");
        self.record_file_transfer(user_id, "support-log.zip", true, true, true);
        self.put_cache(
            "session:user-001",
            json!({ "session_count": 1, "security_score": 92 }),
        );
        self.set_feature_flag("betaBilling", true, "Nowy billing dla użytkowników beta");
        self.update_service_health("redis-cache", true, 4);
        self.update_service_health("notification-center", true, 18);
        self.record_activity(
            user_id,
            "Utworzono VPS i zsynchronizowano centrum powiadomień",
            json!({ "source": "hub_demo" }),
        );

        self.snapshot()
    }

    pub fn record_support_status_update(
        &self,
        user_id: &str,
        ticket_id: &str,
        status: &str,
        channels: Vec<NotificationChannel>,
    ) {
        self.publish_event(
            RealtimeEventType::SupportStatusUpdated,
            "support-system",
            ticket_id,
            vec![user_id.to_string()],
            json!({ "ticket_id": ticket_id, "status": status }),
            channels,
            NotificationPriority::High,
            format!("Status zgłoszenia {} zmienił się na {}", ticket_id, status),
        );
        self.record_audit(
            "support-system",
            "support_status_updated",
            AuditCategory::Support,
            ticket_id,
            json!({ "user_id": user_id, "status": status }),
        );
        self.record_activity(
            user_id,
            format!("Zmieniono status zgłoszenia {} na {}", ticket_id, status),
            json!({ "ticket_id": ticket_id }),
        );
    }

    pub fn record_payment_completed(
        &self,
        user_id: &str,
        payment_id: &str,
        amount: f64,
        channels: Vec<NotificationChannel>,
    ) {
        self.publish_event(
            RealtimeEventType::PaymentCompleted,
            user_id,
            payment_id,
            vec![user_id.to_string(), "admin-001".to_string()],
            json!({ "payment_id": payment_id, "amount": amount }),
            channels,
            NotificationPriority::High,
            format!("Płatność {} została zakończona", payment_id),
        );
        self.record_audit(
            user_id,
            "payment_completed",
            AuditCategory::Finance,
            payment_id,
            json!({ "amount": amount }),
        );
        self.record_activity(
            user_id,
            format!("Zakończono płatność {} na kwotę {:.2}", payment_id, amount),
            json!({ "payment_id": payment_id, "amount": amount }),
        );
    }

    pub fn record_server_activated(
        &self,
        user_id: &str,
        server_id: &str,
        channels: Vec<NotificationChannel>,
    ) {
        self.publish_event(
            RealtimeEventType::ServerActivated,
            "infrastructure",
            server_id,
            vec![user_id.to_string()],
            json!({ "server_id": server_id, "state": "active" }),
            channels,
            NotificationPriority::Medium,
            format!("Serwer {} został aktywowany", server_id),
        );
        self.record_activity(
            user_id,
            format!("Aktywowano serwer {}", server_id),
            json!({ "server_id": server_id }),
        );
    }

    pub fn record_security_warning(
        &self,
        user_id: Option<&str>,
        description: &str,
        channels: Vec<NotificationChannel>,
    ) {
        let alert = SecurityAlert {
            alert_id: Uuid::new_v4().to_string(),
            severity: SecuritySeverity::Warning,
            title: "Ostrzeżenie bezpieczeństwa".to_string(),
            description: description.to_string(),
            user_id: user_id.map(|value| value.to_string()),
            created_at: Utc::now(),
        };

        let mut state = self.state.lock().unwrap();
        state.security_alerts.push(alert.clone());
        drop(state);

        self.publish_event(
            RealtimeEventType::SecurityWarning,
            "security-center",
            alert.alert_id.as_str(),
            user_id
                .map(|value| vec![value.to_string()])
                .unwrap_or_else(|| vec!["admin-001".to_string()]),
            json!({
                "alert_id": alert.alert_id,
                "severity": alert.severity,
                "description": alert.description
            }),
            channels,
            NotificationPriority::Critical,
            alert.title.clone(),
        );
        self.record_audit(
            "security-center",
            "security_warning",
            AuditCategory::Security,
            alert.alert_id.as_str(),
            json!({ "description": description }),
        );
    }

    pub fn record_login(&self, user_id: &str, ip_address: &str, device_id: Option<String>) {
        self.record_audit(
            user_id,
            "login",
            AuditCategory::Authentication,
            user_id,
            json!({ "ip_address": ip_address, "device_id": device_id }),
        );
        self.record_activity(
            user_id,
            format!("Zalogowano do huba z adresu {}", ip_address),
            json!({ "ip_address": ip_address }),
        );

        let mut state = self.state.lock().unwrap();
        state.telemetry.logins += 1;
        drop(state);

        self.publish_event(
            RealtimeEventType::LoginRecorded,
            user_id,
            user_id,
            vec![user_id.to_string()],
            json!({ "ip_address": ip_address, "device_id": device_id }),
            vec![NotificationChannel::Desktop],
            NotificationPriority::Low,
            "Zarejestrowano nowe logowanie".to_string(),
        );
    }

    pub fn assign_role(&self, user_id: &str, role_name: &str) {
        let mut state = self.state.lock().unwrap();
        state
            .user_roles
            .entry(user_id.to_string())
            .or_default()
            .insert(role_name.to_string());
        drop(state);

        self.record_audit(
            "permission-hub",
            "role_assigned",
            AuditCategory::Permission,
            user_id,
            json!({ "role": role_name }),
        );
    }

    pub fn grant_permission(&self, user_id: &str, permission: &str) {
        let mut state = self.state.lock().unwrap();
        state
            .direct_permissions
            .entry(user_id.to_string())
            .or_default()
            .insert(permission.to_string());
        drop(state);

        self.publish_event(
            RealtimeEventType::PermissionChanged,
            "permission-hub",
            user_id,
            vec!["admin-001".to_string()],
            json!({ "user_id": user_id, "permission": permission }),
            vec![NotificationChannel::SlackWebhook],
            NotificationPriority::Medium,
            format!("Nadano uprawnienie {}", permission),
        );
        self.record_audit(
            "permission-hub",
            "permission_granted",
            AuditCategory::Permission,
            user_id,
            json!({ "permission": permission }),
        );
    }

    pub fn permissions_for_user(&self, user_id: &str) -> Vec<String> {
        let state = self.state.lock().unwrap();
        let mut permissions = HashSet::new();

        if let Some(assigned_roles) = state.user_roles.get(user_id) {
            for role_name in assigned_roles {
                if let Some(role) = state.roles.get(role_name) {
                    for permission in &role.permissions {
                        permissions.insert(permission.clone());
                    }
                }
            }
        }

        if let Some(extra_permissions) = state.direct_permissions.get(user_id) {
            for permission in extra_permissions {
                permissions.insert(permission.clone());
            }
        }

        let mut list: Vec<String> = permissions.into_iter().collect();
        list.sort();
        list
    }

    pub fn register_device(
        &self,
        user_id: &str,
        device_name: &str,
        platform: ClientPlatform,
        trusted: bool,
    ) -> String {
        let device_id = Uuid::new_v4().to_string();
        let device = DeviceRecord {
            device_id: device_id.clone(),
            user_id: user_id.to_string(),
            platform,
            device_name: device_name.to_string(),
            trusted,
            active: true,
            detected_at: Utc::now(),
            last_seen_at: Utc::now(),
        };

        let mut state = self.state.lock().unwrap();
        state.devices.insert(device_id.clone(), device);
        drop(state);

        self.publish_event(
            RealtimeEventType::DeviceDetected,
            user_id,
            &device_id,
            vec![user_id.to_string()],
            json!({ "device_name": device_name, "platform": platform, "trusted": trusted }),
            vec![NotificationChannel::Email],
            NotificationPriority::Medium,
            format!("Wykryto urządzenie {}", device_name),
        );

        device_id
    }

    pub fn trust_device(&self, device_id: &str, trusted: bool) {
        let mut state = self.state.lock().unwrap();
        if let Some(device) = state.devices.get_mut(device_id) {
            device.trusted = trusted;
            device.last_seen_at = Utc::now();
        }
    }

    pub fn revoke_device(&self, device_id: &str) {
        let mut state = self.state.lock().unwrap();
        if let Some(device) = state.devices.get_mut(device_id) {
            device.active = false;
            device.last_seen_at = Utc::now();
        }
    }

    pub fn create_session(
        &self,
        user_id: &str,
        device_id: &str,
        platform: ClientPlatform,
        ip_address: &str,
    ) -> String {
        let session_id = Uuid::new_v4().to_string();
        let session = SessionRecord {
            session_id: session_id.clone(),
            user_id: user_id.to_string(),
            device_id: device_id.to_string(),
            platform,
            ip_address: ip_address.to_string(),
            active: true,
            created_at: Utc::now(),
            last_seen_at: Utc::now(),
        };

        let mut state = self.state.lock().unwrap();
        state.sessions.insert(session_id.clone(), session);
        state.telemetry.active_users = active_user_count(&state.sessions) as u64;
        drop(state);

        self.publish_event(
            RealtimeEventType::SessionCreated,
            user_id,
            &session_id,
            vec![user_id.to_string()],
            json!({ "device_id": device_id, "platform": platform, "ip_address": ip_address }),
            vec![NotificationChannel::Desktop],
            NotificationPriority::Low,
            "Utworzono nową sesję".to_string(),
        );

        session_id
    }

    pub fn terminate_session(&self, session_id: &str) {
        let mut state = self.state.lock().unwrap();
        if let Some(session) = state.sessions.get_mut(session_id) {
            session.active = false;
            session.last_seen_at = Utc::now();
        }
        state.telemetry.active_users = active_user_count(&state.sessions) as u64;
    }

    pub fn update_presence(
        &self,
        user_id: &str,
        status: PresenceStatus,
        device_id: Option<String>,
    ) {
        let mut state = self.state.lock().unwrap();
        let record = state.presence.entry(user_id.to_string()).or_insert(PresenceRecord {
            user_id: user_id.to_string(),
            status,
            last_activity_at: Utc::now(),
            active_devices: Vec::new(),
        });
        record.status = status;
        record.last_activity_at = Utc::now();
        if let Some(device_id) = device_id {
            if !record.active_devices.contains(&device_id) {
                record.active_devices.push(device_id);
            }
        }
    }

    pub fn put_cache(&self, key: &str, value: Value) {
        let mut state = self.state.lock().unwrap();
        state.cache.insert(
            key.to_string(),
            CacheEntry {
                key: key.to_string(),
                value,
                ttl_seconds: self.config.cache_ttl_seconds,
                stored_at: Utc::now(),
            },
        );
    }

    pub fn cache_get(&self, key: &str) -> Option<Value> {
        let state = self.state.lock().unwrap();
        state.cache.get(key).map(|entry| entry.value.clone())
    }

    pub fn record_activity(
        &self,
        user_id: &str,
        description: impl Into<String>,
        metadata: Value,
    ) {
        let mut state = self.state.lock().unwrap();
        state.activity_feed.push(ActivityFeedEntry {
            entry_id: Uuid::new_v4().to_string(),
            user_id: user_id.to_string(),
            description: description.into(),
            timestamp: Utc::now(),
            metadata,
        });
    }

    pub fn record_internal_message(&self, from_user: &str, to_user: &str, body: &str) {
        let thread_id = format!("{}:{}", from_user, to_user);
        let mut state = self.state.lock().unwrap();
        state.messages.push(InternalMessage {
            message_id: Uuid::new_v4().to_string(),
            thread_id,
            from_user: from_user.to_string(),
            to_user: to_user.to_string(),
            body: body.to_string(),
            created_at: Utc::now(),
        });
        drop(state);

        self.publish_event(
            RealtimeEventType::NewMessage,
            from_user,
            to_user,
            vec![to_user.to_string()],
            json!({ "from": from_user, "to": to_user }),
            vec![NotificationChannel::Desktop, NotificationChannel::MobilePush],
            NotificationPriority::Medium,
            "Nowa wiadomość wewnętrzna".to_string(),
        );
    }

    pub fn set_feature_flag(&self, key: &str, enabled: bool, description: &str) {
        let mut state = self.state.lock().unwrap();
        state.feature_flags.insert(
            key.to_string(),
            FeatureFlag {
                key: key.to_string(),
                enabled,
                description: description.to_string(),
                updated_at: Utc::now(),
            },
        );
        drop(state);

        self.publish_event(
            RealtimeEventType::FeatureFlagUpdated,
            "feature-flags",
            key,
            vec!["admin-001".to_string()],
            json!({ "feature_flag": key, "enabled": enabled }),
            vec![NotificationChannel::SlackWebhook],
            NotificationPriority::Low,
            format!("Zmieniono flagę {}", key),
        );
    }

    pub fn update_service_health(&self, service: &str, healthy: bool, latency_ms: u64) {
        let mut state = self.state.lock().unwrap();
        state.services.insert(
            service.to_string(),
            MonitoredService {
                name: service.to_string(),
                healthy,
                latency_ms,
                updated_at: Utc::now(),
            },
        );
        if self.config.telemetry_enabled {
            state.telemetry.api_requests += 1;
            if !healthy {
                state.telemetry.api_errors += 1;
            }
        }
        drop(state);

        self.publish_event(
            RealtimeEventType::HealthChanged,
            "health-monitor",
            service,
            vec!["admin-001".to_string()],
            json!({ "service": service, "healthy": healthy, "latency_ms": latency_ms }),
            vec![NotificationChannel::SlackWebhook],
            if healthy {
                NotificationPriority::Low
            } else {
                NotificationPriority::High
            },
            format!("Zmieniono stan zdrowia usługi {}", service),
        );
    }

    pub fn record_file_transfer(
        &self,
        owner_id: &str,
        file_name: &str,
        scanned: bool,
        encrypted: bool,
        versioned: bool,
    ) {
        let record = FileTransferRecord {
            transfer_id: Uuid::new_v4().to_string(),
            owner_id: owner_id.to_string(),
            file_name: file_name.to_string(),
            encrypted,
            scanned,
            versioned,
            created_at: Utc::now(),
        };

        let mut state = self.state.lock().unwrap();
        state.file_transfers.push(record.clone());
        state.telemetry.file_transfers += 1;
        drop(state);

        self.publish_event(
            RealtimeEventType::FileTransferred,
            owner_id,
            &record.transfer_id,
            vec![owner_id.to_string()],
            json!({
                "file_name": file_name,
                "scanned": scanned,
                "encrypted": encrypted,
                "versioned": versioned
            }),
            vec![NotificationChannel::Desktop],
            NotificationPriority::Low,
            format!("Przesłano plik {}", file_name),
        );
    }

    fn record_audit(
        &self,
        actor_id: &str,
        action: &str,
        category: AuditCategory,
        resource: &str,
        metadata: Value,
    ) {
        let mut state = self.state.lock().unwrap();
        state.audit_logs.push(AuditLogEntry {
            entry_id: Uuid::new_v4().to_string(),
            actor_id: actor_id.to_string(),
            action: action.to_string(),
            category,
            resource: resource.to_string(),
            metadata,
            timestamp: Utc::now(),
        });
    }

    fn publish_event(
        &self,
        event_type: RealtimeEventType,
        actor_id: &str,
        subject_id: &str,
        recipients: Vec<String>,
        payload: Value,
        channels: Vec<NotificationChannel>,
        priority: NotificationPriority,
        notification_message: String,
    ) {
        let topic = event_topic(event_type);
        let event = RealtimeEvent {
            event_id: Uuid::new_v4().to_string(),
            topic: topic.to_string(),
            event_type,
            actor_id: actor_id.to_string(),
            subject_id: subject_id.to_string(),
            recipients: recipients.clone(),
            payload: payload.clone(),
            timestamp: Utc::now(),
        };

        let mut state = self.state.lock().unwrap();
        state.events.push(event);
        state.telemetry.emitted_events += 1;
        if self.config.telemetry_enabled {
            state.telemetry.api_requests += 1;
        }

        for recipient in recipients {
            state.notifications.push(NotificationRecord {
                notification_id: Uuid::new_v4().to_string(),
                user_id: recipient,
                title: topic.to_string(),
                message: notification_message.clone(),
                channels: channels.clone(),
                priority,
                metadata: payload.clone(),
                delivered: true,
                created_at: Utc::now(),
            });
            state.telemetry.delivered_notifications += 1;
        }
    }
}

fn event_topic(event_type: RealtimeEventType) -> &'static str {
    match event_type {
        RealtimeEventType::SupportStatusUpdated => "support.status_updated",
        RealtimeEventType::PaymentCompleted => "payment.completed",
        RealtimeEventType::ServerActivated => "server.activated",
        RealtimeEventType::SecurityWarning => "security.warning",
        RealtimeEventType::NewMessage => "messaging.new_message",
        RealtimeEventType::LoginRecorded => "security.login_recorded",
        RealtimeEventType::SessionCreated => "session.created",
        RealtimeEventType::DeviceDetected => "device.detected",
        RealtimeEventType::PermissionChanged => "permission.changed",
        RealtimeEventType::AuditRecorded => "audit.recorded",
        RealtimeEventType::FileTransferred => "file.transferred",
        RealtimeEventType::HealthChanged => "health.changed",
        RealtimeEventType::FeatureFlagUpdated => "feature_flag.updated",
    }
}

fn default_roles() -> Vec<RoleDefinition> {
    vec![
        RoleDefinition {
            name: "User".to_string(),
            permissions: vec![
                "server:create".to_string(),
                "billing:view".to_string(),
                "messages:send".to_string(),
            ],
        },
        RoleDefinition {
            name: "Seller".to_string(),
            permissions: vec![
                "billing:view".to_string(),
                "billing:manage".to_string(),
                "offers:manage".to_string(),
            ],
        },
        RoleDefinition {
            name: "Moderator".to_string(),
            permissions: vec![
                "messages:moderate".to_string(),
                "support:view".to_string(),
                "activity:view".to_string(),
            ],
        },
        RoleDefinition {
            name: "Support".to_string(),
            permissions: vec![
                "support:view".to_string(),
                "support:manage".to_string(),
                "messages:send".to_string(),
                "security:alerts:view".to_string(),
            ],
        },
        RoleDefinition {
            name: "Admin".to_string(),
            permissions: vec![
                "server:create".to_string(),
                "server:delete".to_string(),
                "billing:view".to_string(),
                "billing:manage".to_string(),
                "settings:manage".to_string(),
                "users:manage".to_string(),
            ],
        },
        RoleDefinition {
            name: "SuperAdmin".to_string(),
            permissions: vec![
                "server:create".to_string(),
                "server:delete".to_string(),
                "billing:view".to_string(),
                "billing:manage".to_string(),
                "permissions:manage".to_string(),
                "gateway:manage".to_string(),
                "feature_flags:manage".to_string(),
                "security:manage".to_string(),
            ],
        },
    ]
}

fn default_feature_flags() -> Vec<FeatureFlag> {
    vec![
        FeatureFlag {
            key: "betaBilling".to_string(),
            enabled: true,
            description: "Nowy billing beta".to_string(),
            updated_at: Utc::now(),
        },
        FeatureFlag {
            key: "newDashboard".to_string(),
            enabled: false,
            description: "Nowy dashboard".to_string(),
            updated_at: Utc::now(),
        },
    ]
}

fn hub_health_from_state(state: &HubPlatformState) -> HubHealthStatus {
    let monitored_services = state.services.len();
    let healthy_services = state.services.values().filter(|service| service.healthy).count();
    let degraded_services = monitored_services.saturating_sub(healthy_services);
    let active_sessions = state.sessions.values().filter(|session| session.active).count();
    let status = if degraded_services == 0 {
        "healthy"
    } else if healthy_services > 0 {
        "degraded"
    } else {
        "unhealthy"
    };

    HubHealthStatus {
        status: status.to_string(),
        monitored_services,
        healthy_services,
        degraded_services,
        active_sessions,
        queued_notifications: state.notifications.len(),
        emitted_events: state.events.len(),
    }
}

fn recent_from_slice<T: Clone>(items: &[T], limit: usize) -> Vec<T> {
    let effective_limit = if limit == 0 { items.len() } else { limit };
    items.iter()
        .rev()
        .take(effective_limit)
        .cloned()
        .collect()
}

fn recent_filtered<T: Clone, F>(items: &[T], limit: usize, mut predicate: F) -> Vec<T>
where
    F: FnMut(&T) -> bool,
{
    let effective_limit = if limit == 0 { usize::MAX } else { limit };
    items.iter()
        .rev()
        .filter(|item| predicate(item))
        .take(effective_limit)
        .cloned()
        .collect()
}

fn channel_key(channel: NotificationChannel) -> &'static str {
    match channel {
        NotificationChannel::MobilePush => "mobile_push",
        NotificationChannel::Desktop => "desktop",
        NotificationChannel::Email => "email",
        NotificationChannel::Sms => "sms",
        NotificationChannel::DiscordWebhook => "discord_webhook",
        NotificationChannel::SlackWebhook => "slack_webhook",
    }
}

fn active_user_count(sessions: &HashMap<String, SessionRecord>) -> usize {
    sessions
        .values()
        .filter(|session| session.active)
        .map(|session| session.user_id.clone())
        .collect::<HashSet<_>>()
        .len()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_platform_bootstrap_contains_all_requested_modules() {
        let modules = HubPlatform::enabled_modules();
        assert_eq!(modules.len(), 17);
        assert!(modules.contains(&"real_time_event_system".to_string()));
        assert!(modules.contains(&"notification_center".to_string()));
        assert!(modules.contains(&"audit_log_engine".to_string()));
        assert!(modules.contains(&"event_bus".to_string()));
    }

    #[test]
    fn test_module_catalog_describes_requested_capabilities() {
        let catalog = HubPlatform::module_catalog();
        assert_eq!(catalog.len(), 17);

        let notification_center = catalog
            .iter()
            .find(|module| module.key == "notification_center")
            .expect("notification_center module");
        assert!(notification_center.examples.contains(&"mobile push".to_string()));
        assert!(
            notification_center
                .benefits
                .iter()
                .any(|value| value.contains("jednego miejsca") || value.contains("Jednego miejsca"))
        );
    }

    #[test]
    fn test_access_profile_contains_roles_and_permissions() {
        let platform = HubPlatform::new(HubPlatformConfig::default());
        platform.assign_role("user-1", "Support");
        platform.grant_permission("user-1", "billing:view");

        let profile = platform.access_profile("user-1");
        assert!(profile.roles.contains(&"Support".to_string()));
        assert!(profile.direct_permissions.contains(&"billing:view".to_string()));
        assert!(profile.effective_permissions.contains(&"support:manage".to_string()));
    }

    #[test]
    fn test_listing_notifications_can_filter_by_user() {
        let platform = HubPlatform::new(HubPlatformConfig::default());
        let _ = platform.send_notification(
            "user-a",
            "Test A",
            "Powiadomienie A",
            vec![NotificationChannel::Email],
            NotificationPriority::Medium,
            json!({ "source": "test" }),
        );
        let _ = platform.send_notification(
            "user-b",
            "Test B",
            "Powiadomienie B",
            vec![NotificationChannel::Desktop],
            NotificationPriority::Low,
            json!({ "source": "test" }),
        );

        let notifications = platform.list_notifications(Some("user-a"), 10);
        assert_eq!(notifications.len(), 1);
        assert_eq!(notifications[0].user_id, "user-a");
    }

    #[test]
    fn test_permissions_inherit_from_roles() {
        let platform = HubPlatform::new(HubPlatformConfig::default());
        platform.assign_role("user-1", "Admin");
        let permissions = platform.permissions_for_user("user-1");
        assert!(permissions.contains(&"server:create".to_string()));
        assert!(permissions.contains(&"billing:manage".to_string()));
    }

    #[test]
    fn test_demo_data_produces_sessions_and_notifications() {
        let platform = HubPlatform::new(HubPlatformConfig::default());
        let snapshot = platform.seed_demo_data("user-1");
        assert!(snapshot.counters.sessions >= 1);
        assert!(snapshot.counters.notifications >= 1);
        assert!(snapshot.counters.audit_entries >= 1);
    }
}
