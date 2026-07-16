//! Security module / Moduł bezpieczeństwa
//! Encryption, audit logging, checksums

use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce,
};
use anyhow::Result;
use chrono::Utc;
use rand::Rng;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tracing::{info, warn};

use crate::models::Ticket;

/// Encryption manager / Manager szyfrowania
pub struct EncryptionManager {
    cipher: Aes256Gcm,
}

impl EncryptionManager {
    /// Create new encryption manager from key / Utwórz z klucza
    pub fn new(key_base64: &str) -> Result<Self> {
        let key_bytes = base64::decode(key_base64)?;
        if key_bytes.len() != 32 {
            anyhow::bail!("Encryption key must be 32 bytes (256 bits)");
        }
        let key = aes_gcm::Key::<Aes256Gcm>::from_slice(&key_bytes);
        let cipher = Aes256Gcm::new(key);
        Ok(Self { cipher })
    }

    /// Encrypt data / Szyfruj dane
    pub fn encrypt(&self, plaintext: &[u8]) -> Result<Vec<u8>> {
        let mut rng = rand::thread_rng();
        let nonce_bytes: [u8; 12] = rng.gen();
        let nonce = Nonce::from_slice(&nonce_bytes);

        let ciphertext = self
            .cipher
            .encrypt(nonce, plaintext)
            .map_err(|e| anyhow::anyhow!("Encryption failed: {}", e))?;

        // Prepend nonce to ciphertext / Dołącz nonce do ciphertext
        let mut result = Vec::with_capacity(12 + ciphertext.len());
        result.extend_from_slice(&nonce_bytes);
        result.extend_from_slice(&ciphertext);

        Ok(result)
    }

    /// Decrypt data / Deszyfruj dane
    pub fn decrypt(&self, data: &[u8]) -> Result<Vec<u8>> {
        if data.len() < 12 {
            anyhow::bail!("Invalid encrypted data: too short");
        }

        let (nonce_bytes, ciphertext) = data.split_at(12);
        let nonce = Nonce::from_slice(nonce_bytes);

        let plaintext = self
            .cipher
            .decrypt(nonce, ciphertext)
            .map_err(|e| anyhow::anyhow!("Decryption failed: {}", e))?;

        Ok(plaintext)
    }

    /// Encrypt ticket sensitive fields / Szyfruj wrażliwe pola ticketu
    pub fn encrypt_ticket(&self, ticket: &mut Ticket) -> Result<()> {
        // Encrypt description / Szyfruj opis
        if !ticket.description.is_empty() {
            let encrypted = self.encrypt(ticket.description.as_bytes())?;
            ticket.description = format!("enc:{}", base64::encode(&encrypted));
        }

        // Encrypt conversation messages / Szyfruj wiadomości
        for msg in &mut ticket.conversation {
            if !msg.content.is_empty() {
                let encrypted = self.encrypt(msg.content.as_bytes())?;
                msg.content = format!("enc:{}", base64::encode(&encrypted));
            }
        }

        Ok(())
    }

    /// Decrypt ticket sensitive fields / Deszyfruj wrażliwe pola
    pub fn decrypt_ticket(&self, ticket: &mut Ticket) -> Result<()> {
        // Decrypt description / Deszyfruj opis
        if ticket.description.starts_with("enc:") {
            let encrypted_b64 = &ticket.description[4..];
            let encrypted = base64::decode(encrypted_b64)?;
            let decrypted = self.decrypt(&encrypted)?;
            ticket.description = String::from_utf8(decrypted)?;
        }

        // Decrypt conversation messages / Deszyfruj wiadomości
        for msg in &mut ticket.conversation {
            if msg.content.starts_with("enc:") {
                let encrypted_b64 = &msg.content[4..];
                let encrypted = base64::decode(encrypted_b64)?;
                let decrypted = self.decrypt(&encrypted)?;
                msg.content = String::from_utf8(decrypted)?;
            }
        }

        Ok(())
    }
}

/// Audit logger / Logger audytowy
pub struct AuditLogger {
    enabled: bool,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct AuditEntry {
    pub timestamp: String,
    pub action: String,
    pub ticket_id: String,
    pub user: String,
    pub details: String,
    pub ip_address: Option<String>,
}

impl AuditLogger {
    pub fn new(enabled: bool) -> Self {
        Self { enabled }
    }

    /// Log action / Loguj akcję
    pub fn log(&self, action: &str, ticket_id: &str, user: &str, details: &str) {
        if !self.enabled {
            return;
        }

        let entry = AuditEntry {
            timestamp: Utc::now().to_rfc3339(),
            action: action.to_string(),
            ticket_id: ticket_id.to_string(),
            user: user.to_string(),
            details: details.to_string(),
            ip_address: None,
        };

        // In production, send to audit log service / W produkcji wyślij do serwisu audit
        info!("AUDIT: {}", serde_json::to_string(&entry).unwrap());
    }

    /// Log ticket transfer / Loguj transfer ticketu
    pub fn log_transfer(&self, ticket_id: &str, source: &str, destination: &str) {
        self.log(
            "ticket_transfer",
            ticket_id,
            "system",
            &format!("Transferred from {} to {}", source, destination),
        );
    }

    /// Log validation error / Loguj błąd walidacji
    pub fn log_validation_error(&self, ticket_id: &str, errors: &[String]) {
        self.log(
            "validation_error",
            ticket_id,
            "system",
            &format!("Validation failed: {:?}", errors),
        );
    }
}

/// Checksum generator / Generator sum kontrolnych
pub struct ChecksumGenerator;

impl ChecksumGenerator {
    /// Generate SHA-256 checksum / Generuj sumę kontrolną SHA-256
    pub fn generate(data: &str) -> String {
        let mut hasher = Sha256::new();
        hasher.update(data.as_bytes());
        let result = hasher.finalize();
        hex::encode(result)
    }

    /// Generate ticket checksum / Generuj sumę kontrolną ticketu
    pub fn generate_ticket_checksum(ticket: &Ticket) -> String {
        let data = format!(
            "{}:{}:{}",
            ticket.ticket_id, ticket.description, ticket.ticket_type
        );
        Self::generate(&data)
    }

    /// Verify ticket checksum / Weryfikuj sumę kontrolną
    pub fn verify_ticket_checksum(ticket: &Ticket) -> bool {
        if let Some(checksum) = &ticket.checksum {
            let computed = Self::generate_ticket_checksum(ticket);
            checksum == &computed
        } else {
            false
        }
    }
}

/// Rate limiter / Ogranicznik częstotliwości
pub struct RateLimiter {
    max_requests: u32,
    window_seconds: u64,
    current_count: std::sync::atomic::AtomicU32,
    window_start: std::sync::Mutex<std::time::Instant>,
}

impl RateLimiter {
    pub fn new(max_requests: u32, window_seconds: u64) -> Self {
        Self {
            max_requests,
            window_seconds,
            current_count: std::sync::atomic::AtomicU32::new(0),
            window_start: std::sync::Mutex::new(std::time::Instant::now()),
        }
    }

    /// Check if request is allowed / Sprawdź czy request jest dozwolony
    pub fn check(&self) -> bool {
        let mut start = self.window_start.lock().unwrap();
        let now = std::time::Instant::now();

        // Reset window if expired / Resetuj okno jeśli wygasło
        if now.duration_since(*start).as_secs() >= self.window_seconds {
            *start = now;
            self.current_count.store(0, std::sync::atomic::Ordering::SeqCst);
        }

        let count = self.current_count.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
        count < self.max_requests
    }

    /// Wait until request is allowed / Poczekaj aż request będzie dozwolony
    pub async fn wait(&self) {
        while !self.check() {
            warn!("Rate limit exceeded, waiting...");
            tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
        }
    }
}
