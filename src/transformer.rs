//! Transformer module / Moduł transformacji
//! Validation, enrichment, filtering, deduplication

use std::collections::HashSet;

use lazy_static::lazy_static;
use regex::Regex;
use tracing::{debug, info, warn};

use crate::models::{Ticket, TicketPriority, TicketStatus, TicketType, TransferResult};
use crate::security::{AuditLogger, ChecksumGenerator};

lazy_static! {
    /// Keywords for auto-categorization / Słowa kluczowe do auto-kategoryzacji
    static ref CATEGORY_KEYWORDS: Vec<(&'static str, Vec<&'static str>)> = vec![
        ("billing", vec!["faktura", "płatność", "invoice", "payment", "billing", "rachunek", "cena"]),
        ("technical", vec!["błąd", "error", "crash", "awaria", "nie działa", "bug", "timeout", "500", "404"]),
        ("account", vec!["konto", "account", "logowanie", "login", "hasło", "password", "rejestracja"]),
        ("feature", vec!["proponuję", "sugestia", "feature", "request", "nowa funkcja", "dodaj"]),
        ("integration", vec!["api", "integracja", "webhook", "integration", "sdk", "połączenie"]),
        ("performance", vec!["wolno", "wydajność", "performance", "slow", "lag", "timeout"]),
    ];

    /// Keywords for prioritization / Słowa kluczowe do priorytetyzacji
    static ref PRIORITY_KEYWORDS: Vec<(TicketPriority, Vec<&'static str>)> = vec![
        (TicketPriority::Critical, vec![
            "krytyczny", "critical", "produkcja", "production", "awaria",
            "outage", "down", "nie działa", "emergency", "natychmiast"
        ]),
        (TicketPriority::High, vec![
            "ważne", "important", "pilne", "urgent", "szybko",
            "asap", "blokujące", "blocking"
        ]),
        (TicketPriority::Low, vec![
            "kiedyś", "someday", "niska", "low priority",
            "nie pilne", "not urgent", "pytanie", "question"
        ]),
    ];

    /// Regex for sensitive data detection / Regex do wykrywania wrażliwych danych
    static ref SENSITIVE_PATTERNS: Vec<Regex> = vec![
        Regex::new(r"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b").unwrap(), // Credit card
        Regex::new(r"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b").unwrap(), // Email
        Regex::new(r"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b").unwrap(), // Phone
    ];
}

/// Auto-categorize ticket based on content / Auto-kategoryzuj ticket
pub fn auto_categorize(ticket: &Ticket) -> String {
    let text = build_search_text(ticket);
    let text_lower = text.to_lowercase();

    let mut best_category = "general".to_string();
    let mut best_score = 0usize;

    for (category, keywords) in CATEGORY_KEYWORDS.iter() {
        let score = keywords.iter().filter(|kw| text_lower.contains(kw)).count();
        if score > best_score {
            best_score = score;
            best_category = category.to_string();
        }
    }

    debug!("Auto-categorized ticket {}: {} (score={})", ticket.ticket_id, best_category, best_score);
    best_category
}

/// Auto-prioritize ticket based on content / Auto-priorytetyzuj ticket
pub fn auto_prioritize(ticket: &Ticket) -> TicketPriority {
    let text = build_search_text(ticket);
    let text_lower = text.to_lowercase();

    let mut best_priority = TicketPriority::Medium;
    let mut best_score = 0usize;

    for (priority, keywords) in PRIORITY_KEYWORDS.iter() {
        let score = keywords.iter().filter(|kw| text_lower.contains(kw)).count();
        if score > best_score {
            best_score = score;
            best_priority = priority.clone();
        }
    }

    debug!("Auto-prioritized ticket {}: {:?} (score={})", ticket.ticket_id, best_priority, best_score);
    best_priority
}

/// Generate tags for ticket / Generuj tagi dla ticketu
pub fn generate_tags(ticket: &Ticket) -> Vec<String> {
    let mut tags: HashSet<String> = HashSet::new();
    tags.insert(ticket.ticket_type.to_string());

    if !ticket.category.is_empty() {
        tags.insert(ticket.category.clone());
    }

    if matches!(ticket.priority, TicketPriority::High | TicketPriority::Critical) {
        tags.insert("needs_attention".to_string());
    }

    if ticket.conversation.len() > 3 {
        tags.insert("multi_message".to_string());
    }

    // Detect sensitive data / Wykryj wrażliwe dane
    let text = build_search_text(ticket);
    for pattern in SENSITIVE_PATTERNS.iter() {
        if pattern.is_match(&text) {
            tags.insert("contains_sensitive_data".to_string());
            break;
        }
    }

    tags.into_iter().collect()
}

/// Enrich ticket with additional data / Wzbogać ticket
pub fn enrich_ticket(
    ticket: &mut Ticket,
    auto_categorize_flag: bool,
    auto_priority_flag: bool,
    checksum_enabled: bool,
) {
    if auto_categorize_flag && ticket.category.is_empty() {
        ticket.category = auto_categorize(ticket);
    }

    if auto_priority_flag && matches!(ticket.priority, TicketPriority::Medium) {
        ticket.priority = auto_prioritize(ticket);
    }

    ticket.tags = generate_tags(ticket);
    ticket.transferred_at = Some(chrono::Utc::now());

    if checksum_enabled {
        ticket.checksum = Some(ChecksumGenerator::generate_ticket_checksum(ticket));
    }
}

/// Filter tickets / Filtruj tickety
pub fn filter_tickets(
    tickets: Vec<Ticket>,
    filter_resolved: bool,
    filter_closed: bool,
    exclude_ids: &HashSet<String>,
) -> (Vec<Ticket>, usize) {
    let initial_count = tickets.len();

    let filtered: Vec<Ticket> = tickets
        .into_iter()
        .filter(|t| {
            if filter_resolved && matches!(t.status, TicketStatus::Resolved) {
                return false;
            }
            if filter_closed && matches!(t.status, TicketStatus::Closed) {
                return false;
            }
            if exclude_ids.contains(&t.ticket_id) {
                return false;
            }
            true
        })
        .collect();

    let excluded_count = initial_count - filtered.len();
    info!(
        "Filtered tickets: {} to transfer, {} excluded (resolved/closed/duplicate)",
        filtered.len(),
        excluded_count
    );

    (filtered, excluded_count)
}

/// Process a batch of tickets / Przetwarz batch ticketów
pub fn process_batch(
    tickets: Vec<Ticket>,
    auto_categorize_flag: bool,
    auto_priority_flag: bool,
    filter_resolved: bool,
    filter_closed: bool,
    exclude_ids: &HashSet<String>,
    checksum_enabled: bool,
    audit: &AuditLogger,
) -> (Vec<Ticket>, Vec<TransferResult>) {
    // 1. Filter / Filtruj
    let (filtered, _) = filter_tickets(tickets, filter_resolved, filter_closed, exclude_ids);

    // 2. Validate and enrich / Waliduj i wzbogać
    let mut valid_tickets = Vec::new();
    let mut failed_results = Vec::new();

    for mut ticket in filtered {
        // Validate / Waliduj
        match ticket.validate() {
            Ok(()) => {
                // Normalize / Normalizuj
                ticket.normalize();

                // Enrich / Wzbogać
                enrich_ticket(&mut ticket, auto_categorize_flag, auto_priority_flag, checksum_enabled);

                valid_tickets.push(ticket);
            }
            Err(errors) => {
                warn!("Ticket {} validation failed: {:?}", ticket.ticket_id, errors);
                audit.log_validation_error(&ticket.ticket_id, &errors);
                failed_results.push(TransferResult {
                    ticket_id: ticket.ticket_id,
                    success: false,
                    helpcenter_ticket_id: None,
                    error_message: Some(errors.join("; ")),
                    timestamp: chrono::Utc::now(),
                    retry_count: 0,
                });
            }
        }
    }

    info!(
        "Batch processing: {} valid, {} failed",
        valid_tickets.len(),
        failed_results.len()
    );

    (valid_tickets, failed_results)
}

fn build_search_text(ticket: &Ticket) -> String {
    let mut text = ticket.description.clone();
    for msg in &ticket.conversation {
        text.push(' ');
        text.push_str(&msg.content);
    }
    text
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::models::{ConversationMessage, MessageRole};

    fn make_test_ticket() -> Ticket {
        let mut ticket = Ticket::new("T-001", TicketType::Bug, "Aplikacja się crashuje");
        ticket.conversation.push(ConversationMessage::new(
            "Jan Kowalski",
            MessageRole::Client,
            "Nie mogę się zalogować, critical error",
        ));
        ticket
    }

    #[test]
    fn test_auto_categorize_technical() {
        let ticket = make_test_ticket();
        let category = auto_categorize(&ticket);
        assert_eq!(category, "technical");
    }

    #[test]
    fn test_auto_categorize_billing() {
        let mut ticket = Ticket::new("T-002", TicketType::Question, "Problem z fakturą");
        ticket.conversation.push(ConversationMessage::new(
            "Klient",
            MessageRole::Client,
            "Płatność nie przeszła, invoice nie opłacona",
        ));
        let category = auto_categorize(&ticket);
        assert_eq!(category, "billing");
    }

    #[test]
    fn test_auto_prioritize_critical() {
        let ticket = Ticket::new("T-003", TicketType::Bug, "Krytyczny błąd na produkcji");
        let priority = auto_prioritize(&ticket);
        assert_eq!(priority, TicketPriority::Critical);
    }

    #[test]
    fn test_ticket_validation() {
        let ticket = make_test_ticket();
        assert!(ticket.validate().is_ok());

        let mut invalid = Ticket::new("", TicketType::Other, "");
        let errors = invalid.validate().unwrap_err();
        assert!(!errors.is_empty());
    }

    #[test]
    fn test_filter_resolved() {
        let mut tickets = vec![make_test_ticket()];
        let mut resolved = make_test_ticket();
        resolved.status = TicketStatus::Resolved;
        tickets.push(resolved);

        let exclude = HashSet::new();
        let (filtered, excluded) = filter_tickets(tickets, true, true, &exclude);
        assert_eq!(filtered.len(), 1);
        assert_eq!(excluded, 1);
    }

    #[test]
    fn test_generate_tags() {
        let ticket = make_test_ticket();
        let tags = generate_tags(&ticket);
        assert!(tags.contains(&"bug".to_string()));
    }
}
