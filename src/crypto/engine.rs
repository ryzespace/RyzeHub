//! Cryptographic Engine
//! Multi-layer encryption system

use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce,
};
use anyhow::Result;
use chrono::{DateTime, Duration, Utc};
use rand::Rng;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::HashMap;
use tracing::{debug, info, warn};

/// Encryption context with metadata
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EncryptionContext {
    pub version: u8,
    pub algorithm: String,
    pub key_id: String,
    pub nonce: Vec<u8>,
    pub created_at: DateTime<Utc>,
    pub expires_at: Option<DateTime<Utc>>,
    pub metadata: HashMap<String, String>,
}

impl EncryptionContext {
    pub fn new(key_id: String) -> Self {
        let mut rng = rand::thread_rng();
        let nonce: [u8; 12] = rng.gen();

        Self {
            version: 1,
            algorithm: "AES-256-GCM".to_string(),
            key_id,
            nonce: nonce.to_vec(),
            created_at: Utc::now(),
            expires_at: Some(Utc::now() + Duration::days(90)),
            metadata: HashMap::new(),
        }
    }

    pub fn is_expired(&self) -> bool {
        if let Some(expires) = self.expires_at {
            Utc::now() > expires
        } else {
            false
        }
    }
}

/// Encrypted data packet
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct EncryptedPacket {
    pub context: EncryptionContext,
    pub ciphertext: Vec<u8>,
    pub auth_tag: Vec<u8>,
    pub checksum: String,
}

/// Cryptographic Engine
pub struct CryptoEngine {
    ciphers: HashMap<String, Aes256Gcm>,
    current_key_id: String,
    key_rotation_interval: Duration,
    last_rotation: DateTime<Utc>,
}

impl CryptoEngine {
    /// Create new crypto engine
    pub fn new(master_key_base64: &str) -> Result<Self> {
        let master_key = base64::decode(master_key_base64)?;
        if master_key.len() != 32 {
            anyhow::bail!("Master key must be 32 bytes (256 bits)");
        }

        let key = aes_gcm::Key::<Aes256Gcm>::from_slice(&master_key);
        let cipher = Aes256Gcm::new(key);

        let key_id = Self::generate_key_id(&master_key);
        let mut ciphers = HashMap::new();
        ciphers.insert(key_id.clone(), cipher);

        info!("Initialized crypto engine with key ID: {}", key_id);

        Ok(Self {
            ciphers,
            current_key_id: key_id,
            key_rotation_interval: Duration::days(30),
            last_rotation: Utc::now(),
        })
    }

    /// Generate unique key ID
    fn generate_key_id(key: &[u8]) -> String {
        let mut hasher = Sha256::new();
        hasher.update(key);
        hasher.update(Utc::now().to_rfc3339().as_bytes());
        let hash = hasher.finalize();
        hex::encode(&hash[..16])
    }

    /// Encrypt data with context
    pub fn encrypt(&self, plaintext: &[u8]) -> Result<EncryptedPacket> {
        let context = EncryptionContext::new(self.current_key_id.clone());

        let cipher = self
            .ciphers
            .get(&self.current_key_id)
            .ok_or_else(|| anyhow::anyhow!("Cipher not found for key {}", self.current_key_id))?;

        let nonce = Nonce::from_slice(&context.nonce);
        let ciphertext = cipher
            .encrypt(nonce, plaintext)
            .map_err(|e| anyhow::anyhow!("Encryption failed: {}", e))?;

        // Generate checksum
        let checksum = Self::calculate_checksum(&ciphertext);

        debug!(
            "Encrypted {} bytes with key {}",
            plaintext.len(),
            self.current_key_id
        );

        Ok(EncryptedPacket {
            context,
            ciphertext,
            auth_tag: Vec::new(), // AES-GCM includes auth tag in ciphertext
            checksum,
        })
    }

    /// Decrypt data
    pub fn decrypt(&self, packet: &EncryptedPacket) -> Result<Vec<u8>> {
        // Check expiration
        if packet.context.is_expired() {
            warn!("Attempting to decrypt expired packet with key {}", packet.context.key_id);
        }

        let cipher = self
            .ciphers
            .get(&packet.context.key_id)
            .ok_or_else(|| {
                anyhow::anyhow!("Cipher not found for key {}", packet.context.key_id)
            })?;

        let nonce = Nonce::from_slice(&packet.context.nonce);
        let plaintext = cipher
            .decrypt(nonce, packet.ciphertext.as_ref())
            .map_err(|e| anyhow::anyhow!("Decryption failed: {}", e))?;

        // Verify checksum
        let computed_checksum = Self::calculate_checksum(&packet.ciphertext);
        if computed_checksum != packet.checksum {
            anyhow::bail!("Checksum mismatch - data may be corrupted");
        }

        debug!(
            "Decrypted {} bytes with key {}",
            plaintext.len(),
            packet.context.key_id
        );

        Ok(plaintext)
    }

    /// Calculate checksum
    fn calculate_checksum(data: &[u8]) -> String {
        let mut hasher = Sha256::new();
        hasher.update(data);
        hex::encode(hasher.finalize())
    }

    /// Rotate encryption key
    pub fn rotate_key(&mut self, new_key_base64: &str) -> Result<String> {
        let new_key = base64::decode(new_key_base64)?;
        if new_key.len() != 32 {
            anyhow::bail!("New key must be 32 bytes");
        }

        let key = aes_gcm::Key::<Aes256Gcm>::from_slice(&new_key);
        let cipher = Aes256Gcm::new(key);
        let key_id = Self::generate_key_id(&new_key);

        self.ciphers.insert(key_id.clone(), cipher);
        self.current_key_id = key_id.clone();
        self.last_rotation = Utc::now();

        info!("Rotated to new key: {}", key_id);
        Ok(key_id)
    }

    /// Check if rotation needed
    pub fn needs_rotation(&self) -> bool {
        Utc::now() - self.last_rotation > self.key_rotation_interval
    }

    /// Encrypt ticket fields
    pub fn encrypt_ticket_fields(
        &self,
        description: &str,
        conversation: &[(String, String)],
    ) -> Result<(String, Vec<(String, String)>)> {
        // Encrypt description
        let desc_bytes = description.as_bytes();
        let packet = self.encrypt(desc_bytes)?;
        let encrypted_desc = format!(
            "ENC:{}:{}:{}",
            packet.context.key_id,
            base64::encode(&packet.context.nonce),
            base64::encode(&packet.ciphertext)
        );

        // Encrypt conversation
        let mut encrypted_conv = Vec::new();
        for (sender, content) in conversation {
            let content_bytes = content.as_bytes();
            let packet = self.encrypt(content_bytes)?;
            let encrypted_content = format!(
                "ENC:{}:{}:{}",
                packet.context.key_id,
                base64::encode(&packet.context.nonce),
                base64::encode(&packet.ciphertext)
            );
            encrypted_conv.push((sender.clone(), encrypted_content));
        }

        Ok((encrypted_desc, encrypted_conv))
    }

    /// Decrypt ticket fields
    pub fn decrypt_ticket_fields(
        &self,
        encrypted_desc: &str,
        encrypted_conv: &[(String, String)],
    ) -> Result<(String, Vec<(String, String)>)> {
        // Decrypt description
        let description = if encrypted_desc.starts_with("ENC:") {
            let packet = self.parse_encrypted_packet(encrypted_desc)?;
            let plaintext = self.decrypt(&packet)?;
            String::from_utf8(plaintext)?
        } else {
            encrypted_desc.to_string()
        };

        // Decrypt conversation
        let mut conversation = Vec::new();
        for (sender, encrypted_content) in encrypted_conv {
            let content = if encrypted_content.starts_with("ENC:") {
                let packet = self.parse_encrypted_packet(encrypted_content)?;
                let plaintext = self.decrypt(&packet)?;
                String::from_utf8(plaintext)?
            } else {
                encrypted_content.clone()
            };
            conversation.push((sender.clone(), content));
        }

        Ok((description, conversation))
    }

    /// Parse encrypted packet from string
    fn parse_encrypted_packet(&self, data: &str) -> Result<EncryptedPacket> {
        let parts: Vec<&str> = data.split(':').collect();
        if parts.len() != 4 || parts[0] != "ENC" {
            anyhow::bail!("Invalid encrypted data format");
        }

        let key_id = parts[1].to_string();
        let nonce = base64::decode(parts[2])?;
        let ciphertext = base64::decode(parts[3])?;
        let checksum = Self::calculate_checksum(&ciphertext);

        Ok(EncryptedPacket {
            context: EncryptionContext {
                version: 1,
                algorithm: "AES-256-GCM".to_string(),
                key_id,
                nonce,
                created_at: Utc::now(),
                expires_at: None,
                metadata: HashMap::new(),
            },
            ciphertext,
            auth_tag: Vec::new(),
            checksum,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn get_test_key() -> String {
        base64::encode(&[0u8; 32]) // 32 zero bytes
    }

    #[test]
    fn test_encrypt_decrypt() {
        let engine = CryptoEngine::new(&get_test_key()).unwrap();
        let plaintext = b"Hello, World!";
        
        let packet = engine.encrypt(plaintext).unwrap();
        let decrypted = engine.decrypt(&packet).unwrap();
        
        assert_eq!(plaintext, decrypted.as_slice());
    }

    #[test]
    fn test_key_rotation() {
        let mut engine = CryptoEngine::new(&get_test_key()).unwrap();
        let new_key = base64::encode(&[1u8; 32]);
        
        let new_key_id = engine.rotate_key(&new_key).unwrap();
        assert!(!new_key_id.is_empty());
    }

    #[test]
    fn test_ticket_fields_encryption() {
        let engine = CryptoEngine::new(&get_test_key()).unwrap();
        let description = "Test ticket description";
        let conversation = vec![
            ("Alice".to_string(), "Hello".to_string()),
            ("Bob".to_string(), "Hi there".to_string()),
        ];
        
        let (enc_desc, enc_conv) = engine.encrypt_ticket_fields(description, &conversation).unwrap();
        let (dec_desc, dec_conv) = engine.decrypt_ticket_fields(&enc_desc, &enc_conv).unwrap();
        
        assert_eq!(description, dec_desc);
        assert_eq!(conversation, dec_conv);
    }
}
