# Security & Error Detection System Documentation

## Table of Contents

1. [Encryption System](#encryption-system)
2. [Key Management](#key-management)
3. [Secure Vault](#secure-vault)
4. [Digital Signatures](#digital-signatures)
5. [Error Detection](#error-detection)
6. [Anomaly Detection](#anomaly-detection)
7. [CI/CD Integration](#cicd-integration)

---

## Encryption System

### Architecture

```
┌─────────────────────────────────────────┐
│         CryptoEngine                     │
├─────────────────────────────────────────┤
│                                          │
│  ┌──────────────┐   ┌──────────────┐   │
│  │ AES-256-GCM  │   │  Context     │   │
│  │  Encryption  │   │  Metadata    │   │
│  └──────────────┘   └──────────────┘   │
│                                          │
│  Features:                               │
│  • Authenticated encryption              │
│  • Nonce management                      │
│  • Checksum verification                 │
│  • Key rotation support                  │
│  • Expiration tracking                   │
│                                          │
└─────────────────────────────────────────┘
```

### Data Encryption

```rust
// Create engine
let engine = CryptoEngine::new(&base64_key)?;

// Encrypt
let packet = engine.encrypt(plaintext)?;
// packet contains: context, ciphertext, checksum

// Decrypt
let decrypted = engine.decrypt(&packet)?;
```

### Encrypted Data Format

```
ENC:<key_id>:<nonce_base64>:<ciphertext_base64>
```

Example:
```
ENC:a1b2c3d4e5f6...:dGhpcyBpcyBhIG5vbmNl:Y2lwaGVydGV4dA==
```

### Key Rotation

```rust
// Check if rotation needed
if engine.needs_rotation() {
    let new_key_id = engine.rotate_key(&new_key_base64)?;
    info!("Rotated to key: {}", new_key_id);
}
```

**Automatic rotation:**
- Interval: 30 days
- Old keys retained for decryption
- New data encrypted with current key

---

## Key Management

### Key Hierarchy

```
Master Key (32 bytes, base64)
    │
    ├─► Derived Key: Encryption (AES-256-GCM)
    │
    ├─► Derived Key: Signing (HMAC-SHA256)
    │
    └─► Session Keys (24h lifetime)
```

### KeyManager API

```rust
// Create manager
let mut manager = KeyManager::new(&master_key_base64)?;

// Derive key
let enc_key_id = manager.derive_key(KeyPurpose::Encryption, "ticket-data")?;
let sign_key_id = manager.derive_key(KeyPurpose::Signing, "api-requests")?;

// Generate session key
let session_key_id = manager.generate_session_key()?;

// Rotate master key
let new_master_id = manager.rotate_master_key(&new_master_key_base64)?;

// Clean expired keys
let cleaned = manager.clean_expired_keys();
```

### Key Metadata

```rust
pub struct KeyMetadata {
    pub key_id: String,
    pub created_at: DateTime<Utc>,
    pub expires_at: Option<DateTime<Utc>>,
    pub algorithm: String,
    pub purpose: KeyPurpose,  // Master, Encryption, Signing, KeyDerivation, Session
    pub parent_key_id: Option<String>,
    pub version: u32,
}
```

---

## Secure Vault

### Architecture

```
┌─────────────────────────────────────────┐
│         SecureVault                      │
├─────────────────────────────────────────┤
│                                          │
│  ┌──────────────┐   ┌──────────────┐   │
│  │  Encrypted   │   │  Access      │   │
│  │  Storage     │   │  Control     │   │
│  └──────────────┘   └──────────────┘   │
│                                          │
│  Features:                               │
│  • AES-256-GCM encryption                │
│  • ACL (read/write/admin)                │
│  • Access logging                        │
│  • Integrity verification                │
│  • Persistent storage (JSON)             │
│                                          │
└─────────────────────────────────────────┘
```

### Vault Operations

```bash
# Store key
ticket-pipeline vault store \
  --key-id "api-key-123" \
  --data "secret-api-key-value" \
  --name "Production API Key"

# Retrieve key
ticket-pipeline vault retrieve --key-id "api-key-123"

# List keys
ticket-pipeline vault list

# Check integrity
ticket-pipeline vault integrity
```

### Access Control

```rust
pub struct AccessControlList {
    pub readers: Vec<String>,   // Can read
    pub writers: Vec<String>,   // Can write
    pub admins: Vec<String>,    // Full control
}

// Check permissions
if acl.can_read("user-123") { /* ... */ }
if acl.can_write("user-123") { /* ... */ }
if acl.can_admin("user-123") { /* ... */ }
```

---

## Digital Signatures

### SignatureEngine API

```rust
// Create engine
let engine = SignatureEngine::new(&signing_key_base64)?;

// Sign data
let signature = engine.sign(data)?;

// Verify signature
let valid = engine.verify(data, &signature.signature)?
```

### Signing API Requests

```rust
// Sign request
let signature = engine.sign_request(
    "POST",
    "/api/tickets",
    "{\"ticket_id\":\"T-001\"}",
    "2024-01-01T00:00:00Z"
)?;

// Verify
let valid = engine.verify_request_signature(
    "POST",
    "/api/tickets",
    "{\"ticket_id\":\"T-001\"}",
    "2024-01-01T00:00:00Z",
    &signature.signature
)?;
```

### Signature Format

```json
{
  "signature": "a1b2c3d4e5f6...",
  "algorithm": "HMAC-SHA256",
  "key_id": "...",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

---

## Error Detection

### Architecture

```
┌─────────────────────────────────────────┐
│    ErrorDetectionEngine                  │
├─────────────────────────────────────────┤
│                                          │
│  ┌──────────────┐   ┌──────────────┐   │
│  │   Pattern    │   │  Statistics  │   │
│  │  Recognition │   │  & Tracking  │   │
│  └──────────────┘   └──────────────┘   │
│                                          │
│  ┌──────────────┐   ┌──────────────┐   │
│  │  Correlation │   │  Prediction  │   │
│  │   Engine     │   │   Engine     │   │
│  └──────────────┘   └──────────────┘   │
│                                          │
└─────────────────────────────────────────┘
```

### Built-in Patterns

| Pattern ID | Category | Severity | Regex |
|------------|----------|----------|-------|
| `network_timeout` | Timeout | Medium | `(timeout\|timed out\|deadline exceeded)` |
| `auth_failure` | Authentication | High | `(unauthorized\|forbidden\|401\|403)` |
| `rate_limit` | RateLimit | Medium | `(rate.?limit\|too many requests\|429)` |
| `validation_error` | Validation | Low | `(validation.*fail\|invalid.*data)` |
| `connection_error` | Network | High | `(connection.*refused\|503)` |

### Error Analysis

```bash
# Analyze message
ticket-pipeline errors analyze \
  --message "Request timeout after 30s" \
  --source "helpcenter_api"

# Result:
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "timestamp": "2024-01-01T10:30:00Z",
  "message": "Request timeout after 30s",
  "category": "Timeout",
  "severity": "Medium",
  "source": "helpcenter_api",
  "pattern_id": "network_timeout"
}
```

### Adding Custom Patterns

```rust
let pattern = ErrorPattern {
    id: "custom_db_error".to_string(),
    name: "Database Error".to_string(),
    description: "Database connection or query error".to_string(),
    regex_pattern: r"(?i)(database|sql|postgres|mysql).*error".to_string(),
    category: ErrorCategory::Database,
    severity: ErrorSeverity::High,
    occurrence_count: 0,
    first_seen: Utc::now(),
    last_seen: Utc::now(),
    auto_resolve: false,
};

engine.add_pattern(pattern);
```

### Statistics

```bash
# Show statistics
ticket-pipeline errors stats

# Result:
Timeout: 15
Authentication: 3
RateLimit: 7
Validation: 2
Network: 5
```

### Error Correlation

```rust
// Correlate errors in 5-minute window
let correlations = engine.correlate_errors(Duration::minutes(5));

for group in correlations {
    println!("Correlated errors from {}: {}", group[0].source, group.len());
}
```

---

## Anomaly Detection

### Detection Methods

#### 1. Z-Score Detection

```rust
// Threshold: 3 standard deviations
let anomaly = detector.z_score_detection(metric_name, series, 3.0);
```

**Formula:** `z = |x - μ| / σ`

**Usage:** Statistical outlier detection

#### 2. IQR (Interquartile Range)

```rust
// Multiplier: 1.5 * IQR
let anomaly = detector.iqr_detection(metric_name, series, 1.5);
```

**Formula:** `bounds = [Q1 - 1.5*IQR, Q3 + 1.5*IQR]`

**Usage:** Robust outlier detection

#### 3. Moving Average

```rust
// Window: 10, Threshold: 2.0
let anomaly = detector.moving_average_detection(metric_name, series, 10, 2.0);
```

**Formula:** `deviation = |current - MA| / MA`

**Usage:** Sudden change detection

#### 4. Exponential Smoothing

```rust
// Alpha: 0.3, Threshold: 2.0
let anomaly = detector.exponential_smoothing_detection(metric_name, series, 0.3, 2.0);
```

**Formula:** `smoothed = α * current + (1-α) * previous`

**Usage:** Noise smoothing, trend detection

### Anomaly Types

```rust
pub enum AnomalyType {
    Spike,        // Sudden increase
    Drop,         // Sudden decrease
    Trend,        // Gradual change
    Seasonality,  // Seasonal pattern
    Outlier,      // Statistical outlier
}
```

### Monitoring Metrics

```rust
// Track metric
detector.track_metric("error_rate", 1000);

// Record value
detector.record("error_rate", 5.0);

// Detect anomalies
let anomalies = detector.detect();
```

### Usage Example

```bash
# Test anomaly detection
ticket-pipeline errors anomalies

# Result:
Detected 1 anomalies

  Z-score anomaly: 4.52 (threshold: 3.00)
    Metric: error_rate
    Current value: 50.0
    Expected range: [2.5, 7.5]
    Severity: 0.75
    Confidence: 1.00
```

---

## CI/CD Integration

### Workflow: Security & Encryption

**File:** `.github/workflows/security-encryption.yml`

**Triggers:**
- Push to `src/crypto/**`
- Pull request
- Weekly schedule (Monday 3:00 AM)
- Manual

**Jobs:**
1. Crypto audit
2. Encryption/decryption tests
3. Signing/verification tests
4. Vault operations tests
5. Key rotation check
6. Best practices verification

### Workflow: Error Detection

**File:** `.github/workflows/error-detection.yml`

**Triggers:**
- Push to `src/error_detection/**`
- Pull request
- Every 15 minutes
- Manual (unit/integration/full)

**Jobs:**
1. Error detection tests
2. Pattern recognition tests
3. Anomaly detection tests
4. Pipeline integration test
5. Monitoring report generation
6. Error analytics

### GitHub Secrets

```bash
# Encryption
ENCRYPTION_KEY=<base64 32 bytes>
SIGNING_KEY=<base64 32 bytes>

# Pipeline
CLIENT_DASHBOARD_URL=https://...
CLIENT_DASHBOARD_API_KEY=...
HELPCENTER_URL=https://...
HELPCENTER_API_KEY=...

# Hub Manager
HUB_GITHUB_TOKEN=ghp_...
HUB_ORG_NAME=my-org
```

### Generating Keys

```bash
# Encryption key
openssl rand -base64 32

# Signing key
openssl rand -base64 32

# Master key (for hierarchical management)
openssl rand -base64 32
```

---

## Metrics and Monitoring

### Prometheus Metrics

```
# Crypto metrics
crypto_encryption_operations_total{type="encrypt|decrypt"}
crypto_key_rotations_total
crypto_vault_operations_total{operation="store|retrieve|delete"}
crypto_signature_operations_total{type="sign|verify"}

# Error detection metrics
error_detection_total{category="timeout|auth|validation|..."}
error_detection_patterns_matched_total{pattern_id="..."}
anomaly_detection_anomalies_total{type="spike|drop|trend|..."}
anomaly_detection_metrics_tracked{metric_name="..."}
```

### Audit Logs

```json
{
  "timestamp": "2024-01-01T10:30:00Z",
  "level": "INFO",
  "message": "AUDIT",
  "fields": {
    "action": "ticket_encrypt",
    "ticket_id": "T-001",
    "key_id": "a1b2c3d4...",
    "algorithm": "AES-256-GCM",
    "user": "system"
  }
}
```

---

## Best Practices

### 1. Key Management

- Use strong keys (min. 32 bytes)
- Rotate keys regularly (every 30 days)
- Store keys in Secure Vault
- Never commit keys to git
- Use environment variables

### 2. Encryption

- Encrypt all sensitive data
- Use authenticated encryption (AES-GCM)
- Verify checksums after decryption
- Track encryption metadata

### 3. Error Detection

- Monitor all I/O operations
- Set appropriate anomaly thresholds
- Correlate errors in time window
- Automatically alert on critical errors
- Regularly review statistics

### 4. Security

- Use HTTPS for all API calls
- Verify digital signatures
- Implement rate limiting
- Use circuit breaker pattern
- Log all audit operations

---

## Additional Resources

- [Rust Crypto Documentation](https://docs.rs/aes-gcm)
- [OWASP Cryptographic Guidelines](https://owasp.org/www-project-cheat-sheets/)
- [Prometheus Best Practices](https://prometheus.io/docs/practices/)

---

**Version:** 2.0
**Last updated:** 2024-01-01
**Author:** Ticket Pipeline Team
