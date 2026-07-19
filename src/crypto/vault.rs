//! Secure Key Vault
//! Encrypted storage with access control

use anyhow::Result;
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::HashMap;
use std::fs;
use std::path::Path;
use tracing::{debug, info, warn};

/// Vault entry
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VaultEntry {
    pub key_id: String,
    pub encrypted_data: Vec<u8>,
    pub metadata: VaultMetadata,
    pub created_at: DateTime<Utc>,
    pub last_accessed: DateTime<Utc>,
    pub access_count: u64,
}

/// Vault metadata
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VaultMetadata {
    pub name: String,
    pub description: String,
    pub tags: Vec<String>,
    pub acl: AccessControlList,
}

/// Access control list
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AccessControlList {
    pub readers: Vec<String>,
    pub writers: Vec<String>,
    pub admins: Vec<String>,
}

impl AccessControlList {
    pub fn new() -> Self {
        Self {
            readers: Vec::new(),
            writers: Vec::new(),
            admins: Vec::new(),
        }
    }

    pub fn can_read(&self, user: &str) -> bool {
        self.readers.contains(&user.to_string())
            || self.writers.contains(&user.to_string())
            || self.admins.contains(&user.to_string())
    }

    pub fn can_write(&self, user: &str) -> bool {
        self.writers.contains(&user.to_string()) || self.admins.contains(&user.to_string())
    }

    pub fn can_admin(&self, user: &str) -> bool {
        self.admins.contains(&user.to_string())
    }
}

/// Secure Vault
pub struct SecureVault {
    entries: HashMap<String, VaultEntry>,
    vault_key: Vec<u8>,
    vault_path: String,
    current_user: String,
}

impl SecureVault {
    /// Create new vault
    pub fn new(vault_key_base64: &str, vault_path: &str, current_user: &str) -> Result<Self> {
        let vault_key = base64::decode(vault_key_base64)?;
        if vault_key.len() != 32 {
            anyhow::bail!("Vault key must be 32 bytes");
        }

        let mut vault = Self {
            entries: HashMap::new(),
            vault_key,
            vault_path: vault_path.to_string(),
            current_user: current_user.to_string(),
        };

        // Load existing vault if exists
        if Path::new(vault_path).exists() {
            vault.load()?;
        }

        info!("Initialized secure vault at {}", vault_path);
        Ok(vault)
    }

    /// Store key in vault
    pub fn store_key(
        &mut self,
        key_id: &str,
        key_data: &[u8],
        name: &str,
        description: &str,
        tags: Vec<String>,
    ) -> Result<()> {
        // Check permissions
        let acl = AccessControlList::new();
        if !acl.can_write(&self.current_user) && !acl.admins.is_empty() {
            anyhow::bail!("User {} does not have write permission", self.current_user);
        }

        // Encrypt key data
        let encrypted_data = self.encrypt_vault_data(key_data)?;

        let metadata = VaultMetadata {
            name: name.to_string(),
            description: description.to_string(),
            tags,
            acl,
        };

        let entry = VaultEntry {
            key_id: key_id.to_string(),
            encrypted_data,
            metadata,
            created_at: Utc::now(),
            last_accessed: Utc::now(),
            access_count: 0,
        };

        self.entries.insert(key_id.to_string(), entry);
        self.save()?;

        info!("Stored key {} in vault", key_id);
        Ok(())
    }

    /// Retrieve key from vault
    pub fn retrieve_key(&mut self, key_id: &str) -> Result<Vec<u8>> {
        let entry = self
            .entries
            .get_mut(key_id)
            .ok_or_else(|| anyhow::anyhow!("Key {} not found in vault", key_id))?;

        // Check permissions
        if !entry.metadata.acl.can_read(&self.current_user) && !entry.metadata.acl.readers.is_empty()
        {
            anyhow::bail!(
                "User {} does not have read permission for key {}",
                self.current_user,
                key_id
            );
        }

        // Decrypt key data
        let key_data = self.decrypt_vault_data(&entry.encrypted_data)?;

        // Update access metadata
        entry.last_accessed = Utc::now();
        entry.access_count += 1;
        self.save()?;

        debug!("Retrieved key {} (access #{})", key_id, entry.access_count);
        Ok(key_data)
    }

    /// Delete key from vault
    pub fn delete_key(&mut self, key_id: &str) -> Result<()> {
        let entry = self
            .entries
            .get(key_id)
            .ok_or_else(|| anyhow::anyhow!("Key {} not found in vault", key_id))?;

        // Check permissions
        if !entry.metadata.acl.can_admin(&self.current_user)
            && !entry.metadata.acl.admins.is_empty()
        {
            anyhow::bail!(
                "User {} does not have admin permission for key {}",
                self.current_user,
                key_id
            );
        }

        self.entries.remove(key_id);
        self.save()?;

        info!("Deleted key {} from vault", key_id);
        Ok(())
    }

    /// List all keys in vault
    pub fn list_keys(&self) -> Vec<String> {
        self.entries.keys().cloned().collect()
    }

    /// Get key metadata
    pub fn get_key_metadata(&self, key_id: &str) -> Option<&VaultMetadata> {
        self.entries.get(key_id).map(|e| &e.metadata)
    }

    /// Encrypt vault data
    fn encrypt_vault_data(&self, data: &[u8]) -> Result<Vec<u8>> {
        use aes_gcm::{
            aead::{Aead, KeyInit},
            Aes256Gcm, Nonce,
        };
        use rand::Rng;

        let key = aes_gcm::Key::<Aes256Gcm>::from_slice(&self.vault_key);
        let cipher = Aes256Gcm::new(key);

        let mut rng = rand::thread_rng();
        let nonce_bytes: [u8; 12] = rng.gen();
        let nonce = Nonce::from_slice(&nonce_bytes);

        let ciphertext = cipher
            .encrypt(nonce, data)
            .map_err(|e| anyhow::anyhow!("Vault encryption failed: {}", e))?;

        // Prepend nonce
        let mut result = Vec::with_capacity(12 + ciphertext.len());
        result.extend_from_slice(&nonce_bytes);
        result.extend_from_slice(&ciphertext);

        Ok(result)
    }

    /// Decrypt vault data
    fn decrypt_vault_data(&self, data: &[u8]) -> Result<Vec<u8>> {
        use aes_gcm::{
            aead::{Aead, KeyInit},
            Aes256Gcm, Nonce,
        };

        if data.len() < 12 {
            anyhow::bail!("Invalid encrypted data: too short");
        }

        let (nonce_bytes, ciphertext) = data.split_at(12);
        let nonce = Nonce::from_slice(nonce_bytes);

        let key = aes_gcm::Key::<Aes256Gcm>::from_slice(&self.vault_key);
        let cipher = Aes256Gcm::new(key);

        let plaintext = cipher
            .decrypt(nonce, ciphertext)
            .map_err(|e| anyhow::anyhow!("Vault decryption failed: {}", e))?;

        Ok(plaintext)
    }

    /// Save vault to disk
    fn save(&self) -> Result<()> {
        let vault_data = serde_json::to_string_pretty(&self.entries)?;
        fs::write(&self.vault_path, vault_data)?;
        debug!("Saved vault to {}", self.vault_path);
        Ok(())
    }

    /// Load vault from disk
    fn load(&mut self) -> Result<()> {
        let vault_data = fs::read_to_string(&self.vault_path)?;
        self.entries = serde_json::from_str(&vault_data)?;
        info!("Loaded {} keys from vault", self.entries.len());
        Ok(())
    }

    /// Generate vault integrity hash
    pub fn integrity_hash(&self) -> String {
        let mut hasher = Sha256::new();
        for (key_id, entry) in &self.entries {
            hasher.update(key_id.as_bytes());
            hasher.update(&entry.encrypted_data);
        }
        hex::encode(hasher.finalize())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn get_test_key() -> String {
        base64::encode(&[0u8; 32])
    }

    #[test]
    fn test_vault_store_retrieve() {
        let vault_path = "/tmp/test_vault.json";
        let mut vault = SecureVault::new(&get_test_key(), vault_path, "test_user").unwrap();

        let key_data = b"secret key data";
        vault
            .store_key("key-1", key_data, "Test Key", "Test description", vec![])
            .unwrap();

        let retrieved = vault.retrieve_key("key-1").unwrap();
        assert_eq!(key_data, retrieved.as_slice());

        // Cleanup
        let _ = fs::remove_file(vault_path);
    }

    #[test]
    fn test_vault_integrity() {
        let vault_path = "/tmp/test_vault_integrity.json";
        let mut vault = SecureVault::new(&get_test_key(), vault_path, "test_user").unwrap();

        vault
            .store_key("key-1", b"data1", "Key 1", "Desc", vec![])
            .unwrap();
        let hash1 = vault.integrity_hash();

        vault
            .store_key("key-2", b"data2", "Key 2", "Desc", vec![])
            .unwrap();
        let hash2 = vault.integrity_hash();

        assert_ne!(hash1, hash2);

        // Cleanup
        let _ = fs::remove_file(vault_path);
    }
}
