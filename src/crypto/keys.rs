//! Key Management System
//! Hierarchical key derivation and rotation

use anyhow::Result;
use chrono::{DateTime, Duration, Utc};
use hmac::{Hmac, Mac};
use rand::Rng;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256, Sha512};
use std::collections::HashMap;
use tracing::{debug, info};

type HmacSha256 = Hmac<Sha256>;

/// Key metadata
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct KeyMetadata {
    pub key_id: String,
    pub created_at: DateTime<Utc>,
    pub expires_at: Option<DateTime<Utc>>,
    pub algorithm: String,
    pub purpose: KeyPurpose,
    pub parent_key_id: Option<String>,
    pub version: u32,
}

/// Key purpose
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub enum KeyPurpose {
    Master,
    Encryption,
    Signing,
    KeyDerivation,
    Session,
}

/// Key pair
#[derive(Debug, Clone)]
pub struct KeyPair {
    pub public_key: Vec<u8>,
    pub private_key: Vec<u8>,
    pub metadata: KeyMetadata,
}

/// Key Manager
pub struct KeyManager {
    master_key: Vec<u8>,
    keys: HashMap<String, KeyPair>,
    current_key_id: String,
    rotation_interval: Duration,
}

impl KeyManager {
    /// Create new key manager
    pub fn new(master_key_base64: &str) -> Result<Self> {
        let master_key = base64::decode(master_key_base64)?;
        if master_key.len() < 32 {
            anyhow::bail!("Master key must be at least 32 bytes");
        }

        let key_id = Self::generate_key_id(&master_key);
        let metadata = KeyMetadata {
            key_id: key_id.clone(),
            created_at: Utc::now(),
            expires_at: None,
            algorithm: "HMAC-SHA256".to_string(),
            purpose: KeyPurpose::Master,
            parent_key_id: None,
            version: 1,
        };

        let master_pair = KeyPair {
            public_key: master_key.clone(),
            private_key: master_key.clone(),
            metadata,
        };

        let mut keys = HashMap::new();
        keys.insert(key_id.clone(), master_pair);

        info!("Initialized key manager with master key ID: {}", key_id);

        Ok(Self {
            master_key,
            keys,
            current_key_id: key_id,
            rotation_interval: Duration::days(30),
        })
    }

    /// Generate key ID
    fn generate_key_id(key: &[u8]) -> String {
        let mut hasher = Sha256::new();
        hasher.update(key);
        hasher.update(Utc::now().to_rfc3339().as_bytes());
        let hash = hasher.finalize();
        hex::encode(&hash[..16])
    }

    /// Derive child key
    pub fn derive_key(&mut self, purpose: KeyPurpose, info: &str) -> Result<String> {
        let mut mac = HmacSha256::new_from_slice(&self.master_key)
            .map_err(|e| anyhow::anyhow!("HMAC initialization failed: {}", e))?;
        
        mac.update(info.as_bytes());
        mac.update(purpose_to_bytes(&purpose));
        mac.update(Utc::now().to_rfc3339().as_bytes());
        
        let result = mac.finalize();
        let derived_key = result.into_bytes().to_vec();

        let key_id = Self::generate_key_id(&derived_key);
        let metadata = KeyMetadata {
            key_id: key_id.clone(),
            created_at: Utc::now(),
            expires_at: Some(Utc::now() + Duration::days(90)),
            algorithm: "AES-256-GCM".to_string(),
            purpose: purpose.clone(),
            parent_key_id: Some(self.current_key_id.clone()),
            version: 1,
        };

        let key_pair = KeyPair {
            public_key: derived_key.clone(),
            private_key: derived_key,
            metadata,
        };

        self.keys.insert(key_id.clone(), key_pair);
        debug!("Derived key {} for purpose {:?}", key_id, purpose);

        Ok(key_id)
    }

    /// Get key by ID
    pub fn get_key(&self, key_id: &str) -> Option<&KeyPair> {
        self.keys.get(key_id)
    }

    /// Get current encryption key
    pub fn get_current_key(&self) -> &KeyPair {
        self.keys.get(&self.current_key_id).unwrap()
    }

    /// Rotate master key
    pub fn rotate_master_key(&mut self, new_master_key_base64: &str) -> Result<String> {
        let new_master_key = base64::decode(new_master_key_base64)?;
        if new_master_key.len() < 32 {
            anyhow::bail!("New master key must be at least 32 bytes");
        }

        let key_id = Self::generate_key_id(&new_master_key);
        let metadata = KeyMetadata {
            key_id: key_id.clone(),
            created_at: Utc::now(),
            expires_at: None,
            algorithm: "HMAC-SHA256".to_string(),
            purpose: KeyPurpose::Master,
            parent_key_id: Some(self.current_key_id.clone()),
            version: self.get_key(&self.current_key_id).unwrap().metadata.version + 1,
        };

        let master_pair = KeyPair {
            public_key: new_master_key.clone(),
            private_key: new_master_key,
            metadata,
        };

        self.keys.insert(key_id.clone(), master_pair);
        self.master_key = self.keys.get(&key_id).unwrap().private_key.clone();
        self.current_key_id = key_id.clone();

        info!("Rotated master key to: {}", key_id);
        Ok(key_id)
    }

    /// Check if rotation needed
    pub fn needs_rotation(&self) -> bool {
        let current_key = self.get_current_key();
        if let Some(expires) = current_key.metadata.expires_at {
            Utc::now() > expires - Duration::days(7) // 7 days before expiration
        } else {
            false
        }
    }

    /// Generate session key
    pub fn generate_session_key(&mut self) -> Result<String> {
        let mut rng = rand::thread_rng();
        let mut session_key = vec![0u8; 32];
        rng.fill(&mut session_key[..]);

        let key_id = Self::generate_key_id(&session_key);
        let metadata = KeyMetadata {
            key_id: key_id.clone(),
            created_at: Utc::now(),
            expires_at: Some(Utc::now() + Duration::hours(24)), // Short-lived
            algorithm: "AES-256-GCM".to_string(),
            purpose: KeyPurpose::Session,
            parent_key_id: Some(self.current_key_id.clone()),
            version: 1,
        };

        let key_pair = KeyPair {
            public_key: session_key.clone(),
            private_key: session_key,
            metadata,
        };

        self.keys.insert(key_id.clone(), key_pair);
        debug!("Generated session key: {}", key_id);

        Ok(key_id)
    }

    /// Clean expired keys
    pub fn clean_expired_keys(&mut self) -> usize {
        let now = Utc::now();
        let expired: Vec<String> = self
            .keys
            .iter()
            .filter(|(_, key)| {
                key.metadata.purpose != KeyPurpose::Master
                    && key.metadata.expires_at.map_or(false, |exp| now > exp)
            })
            .map(|(id, _)| id.clone())
            .collect();

        let count = expired.len();
        for key_id in expired {
            self.keys.remove(&key_id);
            debug!("Removed expired key: {}", key_id);
        }

        if count > 0 {
            info!("Cleaned {} expired keys", count);
        }

        count
    }

    /// Export key metadata
    pub fn export_metadata(&self) -> Vec<KeyMetadata> {
        self.keys.values().map(|k| k.metadata.clone()).collect()
    }
}

fn purpose_to_bytes(purpose: &KeyPurpose) -> Vec<u8> {
    match purpose {
        KeyPurpose::Master => b"master".to_vec(),
        KeyPurpose::Encryption => b"encryption".to_vec(),
        KeyPurpose::Signing => b"signing".to_vec(),
        KeyPurpose::KeyDerivation => b"key_derivation".to_vec(),
        KeyPurpose::Session => b"session".to_vec(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn get_test_key() -> String {
        base64::encode(&[0u8; 32])
    }

    #[test]
    fn test_key_derivation() {
        let mut manager = KeyManager::new(&get_test_key()).unwrap();
        let key_id = manager.derive_key(KeyPurpose::Encryption, "test").unwrap();
        assert!(!key_id.is_empty());
        assert!(manager.get_key(&key_id).is_some());
    }

    #[test]
    fn test_session_key_generation() {
        let mut manager = KeyManager::new(&get_test_key()).unwrap();
        let key_id = manager.generate_session_key().unwrap();
        let key = manager.get_key(&key_id).unwrap();
        assert_eq!(key.metadata.purpose, KeyPurpose::Session);
    }

    #[test]
    fn test_master_key_rotation() {
        let mut manager = KeyManager::new(&get_test_key()).unwrap();
        let new_key = base64::encode(&[1u8; 32]);
        let new_key_id = manager.rotate_master_key(&new_key).unwrap();
        assert!(!new_key_id.is_empty());
    }
}
