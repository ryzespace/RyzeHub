use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum TicketType {
    Bug,
    FeatureRequest,
    Question,
    Complaint,
    TechnicalIssue,
    Other,
}

impl std::fmt::Display for TicketType {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            TicketType::Bug => write!(f, "bug"),
            TicketType::FeatureRequest => write!(f, "feature_request"),
            TicketType::Question => write!(f, "question"),
            TicketType::Complaint => write!(f, "complaint"),
            TicketType::TechnicalIssue => write!(f, "technical_issue"),
            TicketType::Other => write!(f, "other"),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum TicketPriority {
    Low,
    Medium,
    High,
    Critical,
}

impl std::fmt::Display for TicketPriority {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            TicketPriority::Low => write!(f, "low"),
            TicketPriority::Medium => write!(f, "medium"),
            TicketPriority::High => write!(f, "high"),
            TicketPriority::Critical => write!(f, "critical"),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum TicketStatus {
    New,
    InProgress,
    Waiting,
    Transferred,
    Resolved,
    Closed,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "snake_case")]
pub enum MessageRole {
    Client,
    Agent,
    System,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ConversationMessage {
    pub sender: String,
    pub role: MessageRole,
    pub content: String,
    pub timestamp: DateTime<Utc>,
    #[serde(default)]
    pub message_id: Option<String>,
}

impl ConversationMessage {
    pub fn new(sender: impl Into<String>, role: MessageRole, content: impl Into<String>) -> Self {
        Self {
            sender: sender.into(),
            role,
            content: content.into(),
            timestamp: Utc::now(),
            message_id: Some(Uuid::new_v4().to_string()),
        }
    }

    pub fn validate(&self) -> Vec<String> {
        let mut errors = Vec::new();
        if self.content.trim().is_empty() {
            errors.push("Empty message content".to_string());
        }
        if self.sender.trim().is_empty() {
            errors.push("Empty sender".to_string());
        }
        errors
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Ticket {
    pub ticket_id: String,
    pub ticket_type: TicketType,
    pub description: String,
    #[serde(default)]
    pub conversation: Vec<ConversationMessage>,
    #[serde(default = "default_priority")]
    pub priority: TicketPriority,
    #[serde(default = "default_status")]
    pub status: TicketStatus,
    #[serde(default)]
    pub created_at: Option<DateTime<Utc>>,
    #[serde(default)]
    pub updated_at: Option<DateTime<Utc>>,
    #[serde(default)]
    pub client_id: String,
    #[serde(default)]
    pub client_name: String,
    #[serde(default)]
    pub category: String,
    #[serde(default)]
    pub tags: Vec<String>,
    #[serde(default)]
    pub transferred_at: Option<DateTime<Utc>>,
    #[serde(default)]
    pub helpcenter_ticket_id: Option<String>,
    #[serde(default)]
    pub checksum: Option<String>,
}

fn default_priority() -> TicketPriority {
    TicketPriority::Medium
}

fn default_status() -> TicketStatus {
    TicketStatus::New
}

impl Ticket {
    pub fn new(
        ticket_id: impl Into<String>,
        ticket_type: TicketType,
        description: impl Into<String>,
    ) -> Self {
        Self {
            ticket_id: ticket_id.into(),
            ticket_type,
            description: description.into(),
            conversation: Vec::new(),
            priority: TicketPriority::Medium,
            status: TicketStatus::New,
            created_at: Some(Utc::now()),
            updated_at: Some(Utc::now()),
            client_id: String::new(),
            client_name: String::new(),
            category: String::new(),
            tags: Vec::new(),
            transferred_at: None,
            helpcenter_ticket_id: None,
            checksum: None,
        }
    }

    pub fn validate(&self) -> Result<(), Vec<String>> {
        let mut errors = Vec::new();

        if self.ticket_id.is_empty() {
            errors.push("Missing ticket_id".to_string());
        }
        if self.description.trim().is_empty() {
            errors.push("Missing description".to_string());
        }
        if self.conversation.is_empty() {
            errors.push("Missing conversation".to_string());
        }

        for (i, msg) in self.conversation.iter().enumerate() {
            let msg_errors = msg.validate();
            for err in msg_errors {
                errors.push(format!("Message #{}: {}", i + 1, err));
            }
        }

        if errors.is_empty() {
            Ok(())
        } else {
            Err(errors)
        }
    }

    pub fn normalize(&mut self) {
        self.description = self.description.trim().to_string();
        self.conversation.sort_by(|a, b| a.timestamp.cmp(&b.timestamp));
        self.client_name = self
            .client_name
            .split_whitespace()
            .map(|word| {
                let mut chars = word.chars();
                match chars.next() {
                    None => String::new(),
                    Some(first) => {
                        first.to_uppercase().to_string() + &chars.as_str().to_lowercase()
                    }
                }
            })
            .collect::<Vec<_>>()
            .join(" ");
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TransferResult {
    pub ticket_id: String,
    pub success: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub helpcenter_ticket_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error_message: Option<String>,
    pub timestamp: DateTime<Utc>,
    #[serde(default)]
    pub retry_count: u32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BatchTransferResult {
    pub results: Vec<TransferResult>,
    pub total: usize,
    pub successful: usize,
    pub failed: usize,
    pub duration_ms: u64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HealthStatus {
    pub status: String,
    pub timestamp: DateTime<Utc>,
    pub source: ServiceHealth,
    pub destination: ServiceHealth,
    pub pipeline: PipelineHealth,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ServiceHealth {
    pub name: String,
    pub status: String,
    pub latency_ms: u64,
    pub last_check: DateTime<Utc>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PipelineHealth {
    pub status: String,
    pub uptime_seconds: u64,
    pub tickets_processed: u64,
    pub error_rate: f64,
}
