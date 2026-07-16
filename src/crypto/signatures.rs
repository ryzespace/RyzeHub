//! Digital Signature Engine / Silnik Podpisów Cyfrowych
//! HMAC-based signatures and verification

use anyhow::Result;
use hmac::{Hmac, Mac};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use tracing::{debug, info};

type HmacSha256 = Hmac<Sha256>;

/// Signature result / Wynik podpisu
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SignatureResult {
    pub signature: String,
    pub algorithm: String,
    pub key_id: String,
    pub timestamp: String,
}

/// Signature Engine / Silnik Podpisów
pub struct SignatureEngine {
    signing_key: Vec<u8>,
    key_id: String,
}

impl SignatureEngine {
    /// Create new signature engine / Utwórz nowy silnik podpisów
    pub fn new(signing_key_base64: &str) -> Result<Self> {
        let signing_key = base64::decode(signing_key_base64)?;
        if signing_key.len() < 32 {
            anyhow::bail!("Signing key must be at least 32 bytes");
        }

        let key_id = Self::generate_key_id(&signing_key);
        info!("Initialized signature engine with key ID: {}", key_id);

        Ok(Self {
            signing_key,
            key_id,
        })
    }

    /// Generate key ID / Generuj ID klucza
    fn generate_key_id(key: &[u8]) -> String {
        let mut hasher = Sha256::new();
        hasher.update(key);
        let hash = hasher.finalize();
        hex::encode(&hash[..16])
    }

    /// Sign data / Podpisz dane
    pub fn sign(&self, data: &[u8]) -> Result<SignatureResult> {
        let mut mac = HmacSha256::new_from_slice(&self.signing_key)
            .map_err(|e| anyhow::anyhow!("HMAC initialization failed: {}", e))?;

        mac.update(data);
        let result = mac.finalize();
        let signature = hex::encode(result.into_bytes());

        debug!("Signed {} bytes with key {}", data.len(), self.key_id);

        Ok(SignatureResult {
            signature,
            algorithm: "HMAC-SHA256".to_string(),
            key_id: self.key_id.clone(),
            timestamp: chrono::Utc::now().to_rfc3339(),
        })
    }

    /// Verify signature / Weryfikuj podpis
    pub fn verify(&self, data: &[u8], signature: &str) -> Result<bool> {
        let mut mac = HmacSha256::new_from_slice(&self.signing_key)
            .map_err(|e| anyhow::anyhow!("HMAC initialization failed: {}", e))?;

        mac.update(data);
        let result = mac.finalize();
        let computed_signature = hex::encode(result.into_bytes());

        let valid = computed_signature == signature;
        debug!(
            "Signature verification: {} (key: {})",
            if valid { "valid" } else { "invalid" },
            self.key_id
        );

        Ok(valid)
    }

    /// Sign ticket / Podpisz ticket
    pub fn sign_ticket(&self, ticket_id: &str, description: &str, ticket_type: &str) -> Result<SignatureResult> {
        let data = format!("{}:{}:{}", ticket_id, description, ticket_type);
        self.sign(data.as_bytes())
    }

    /// Verify ticket signature / Weryfikuj podpis ticketu
    pub fn verify_ticket_signature(
        &self,
        ticket_id: &str,
        description: &str,
        ticket_type: &str,
        signature: &str,
    ) -> Result<bool> {
        let data = format!("{}:{}:{}", ticket_id, description, ticket_type);
        self.verify(data.as_bytes(), signature)
    }

    /// Sign API request / Podpisz żądanie API
    pub fn sign_request(&self, method: &str, path: &str, body: &str, timestamp: &str) -> Result<SignatureResult> {
        let data = format!("{}:{}:{}:{}", method, path, body, timestamp);
        self.sign(data.as_bytes())
    }

    /// Verify API request signature / Weryfikuj podpis żądania API
    pub fn verify_request_signature(
        &self,
        method: &str,
        path: &str,
        body: &str,
        timestamp: &str,
        signature: &str,
    ) -> Result<bool> {
        let data = format!("{}:{}:{}:{}", method, path, body, timestamp);
        self.verify(data.as_bytes(), signature)
    }

    /// Get key ID / Pobierz ID klucza
    pub fn key_id(&self) -> &str {
        &self.key_id
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn get_test_key() -> String {
        base64::encode(&[0u8; 32])
    }

    #[test]
    fn test_sign_verify() {
        let engine = SignatureEngine::new(&get_test_key()).unwrap();
        let data = b"test data";

        let signature = engine.sign(data).unwrap();
        let valid = engine.verify(data, &signature.signature).unwrap();

        assert!(valid);
    }

    #[test]
    fn test_invalid_signature() {
        let engine = SignatureEngine::new(&get_test_key()).unwrap();
        let data = b"test data";

        let valid = engine.verify(data, "invalid_signature").unwrap();
        assert!(!valid);
    }

    #[test]
    fn test_ticket_signature() {
        let engine = SignatureEngine::new(&get_test_key()).unwrap();

        let signature = engine.sign_ticket("T-001", "Test description", "bug").unwrap();
        let valid = engine
            .verify_ticket_signature("T-001", "Test description", "bug", &signature.signature)
            .unwrap();

        assert!(valid);
    }

    #[test]
    fn test_request_signature() {
        let engine = SignatureEngine::new(&get_test_key()).unwrap();

        let signature = engine
            .sign_request("POST", "/api/tickets", "{\"data\":1}", "2024-01-01T00:00:00Z")
            .unwrap();

        let valid = engine
            .verify_request_signature(
                "POST",
                "/api/tickets",
                "{\"data\":1}",
                "2024-01-01T00:00:00Z",
                &signature.signature,
            )
            .unwrap();

        assert!(valid);
    }
}
