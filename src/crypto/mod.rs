//! Advanced Cryptographic System / Zaawansowany System Kryptograficzny
//! Multi-layer encryption with key rotation, signatures, and secure storage

pub mod engine;
pub mod keys;
pub mod vault;
pub mod signatures;

pub use engine::CryptoEngine;
pub use keys::{KeyManager, KeyPair};
pub use vault::SecureVault;
pub use signatures::SignatureEngine;
