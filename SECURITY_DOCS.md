# Security & Error Detection System Documentation
# Dokumentacja Systemu Bezpieczeństwa i Detekcji Błędów

## 📋 Spis treści / Table of Contents

1. [System Szyfrowania / Encryption System](#system-szyfrowania)
2. [Zarządzanie Kluczami / Key Management](#zarządzanie-kluczami)
3. [Secure Vault](#secure-vault)
4. [Podpisy Cyfrowe / Digital Signatures](#podpisy-cyfrowe)
5. [Detekcja Błędów / Error Detection](#detekcja-błędów)
6. [Detekcja Anomalii / Anomaly Detection](#detekcja-anomalii)
7. [Integracja CI/CD](#integracja-cicd)

---

## 🔐 System Szyfrowania

### Architektura / Architecture

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

### Szyfrowanie danych / Data Encryption

```rust
// Utwórz silnik / Create engine
let engine = CryptoEngine::new(&base64_key)?;

// Szyfruj / Encrypt
let packet = engine.encrypt(plaintext)?;
// packet zawiera: context, ciphertext, checksum

// Deszyfruj / Decrypt
let decrypted = engine.decrypt(&packet)?;
```

### Format zaszyfrowanych danych / Encrypted Data Format

```
ENC:<key_id>:<nonce_base64>:<ciphertext_base64>
```

Przykład / Example:
```
ENC:a1b2c3d4e5f6...:dGhpcyBpcyBhIG5vbmNl:Y2lwaGVydGV4dA==
```

### Rotacja kluczy / Key Rotation

```rust
// Sprawdź czy rotacja potrzebna / Check if rotation needed
if engine.needs_rotation() {
    let new_key_id = engine.rotate_key(&new_key_base64)?;
    info!("Rotated to key: {}", new_key_id);
}
```

**Automatyczna rotacja:**
- Interwał: 30 dni
- Stare klucze zachowywane dla deszyfracji
- Nowe dane szyfrowane aktualnym kluczem

---

## 🔑 Zarządzanie Kluczami

### Hierarchia kluczy / Key Hierarchy

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
// Utwórz manager / Create manager
let mut manager = KeyManager::new(&master_key_base64)?;

// Wyprowadź klucz / Derive key
let enc_key_id = manager.derive_key(KeyPurpose::Encryption, "ticket-data")?;
let sign_key_id = manager.derive_key(KeyPurpose::Signing, "api-requests")?;

// Generuj klucz sesyjny / Generate session key
let session_key_id = manager.generate_session_key()?;

// Rotuj master key / Rotate master key
let new_master_id = manager.rotate_master_key(&new_master_key_base64)?;

// Wyczyść wygasłe klucze / Clean expired keys
let cleaned = manager.clean_expired_keys();
```

### Metadane klucza / Key Metadata

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

## 🔒 Secure Vault

### Architektura / Architecture

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

### Operacje na vault / Vault Operations

```bash
# Przechowaj klucz / Store key
ticket-pipeline vault store \
  --key-id "api-key-123" \
  --data "secret-api-key-value" \
  --name "Production API Key"

# Pobierz klucz / Retrieve key
ticket-pipeline vault retrieve --key-id "api-key-123"

# Lista kluczy / List keys
ticket-pipeline vault list

# Sprawdź integralność / Check integrity
ticket-pipeline vault integrity
```

### Kontrola dostępu / Access Control

```rust
pub struct AccessControlList {
    pub readers: Vec<String>,   // Mogą odczytywać / Can read
    pub writers: Vec<String>,   // Mogą zapisywać / Can write
    pub admins: Vec<String>,    // Pełna kontrola / Full control
}

// Sprawdź uprawnienia / Check permissions
if acl.can_read("user-123") { /* ... */ }
if acl.can_write("user-123") { /* ... */ }
if acl.can_admin("user-123") { /* ... */ }
```

---

## ✍️ Podpisy Cyfrowe

### SignatureEngine API

```rust
// Utwórz silnik / Create engine
let engine = SignatureEngine::new(&signing_key_base64)?;

// Podpisz dane / Sign data
let signature = engine.sign(data)?;

// Weryfikuj podpis / Verify signature
let valid = engine.verify(data, &signature.signature)?;
```

### Podpisywanie żądań API / Signing API Requests

```rust
// Podpisz żądanie / Sign request
let signature = engine.sign_request(
    "POST",
    "/api/tickets",
    "{\"ticket_id\":\"T-001\"}",
    "2024-01-01T00:00:00Z"
)?;

// Weryfikuj / Verify
let valid = engine.verify_request_signature(
    "POST",
    "/api/tickets",
    "{\"ticket_id\":\"T-001\"}",
    "2024-01-01T00:00:00Z",
    &signature.signature
)?;
```

### Format podpisu / Signature Format

```json
{
  "signature": "a1b2c3d4e5f6...",
  "algorithm": "HMAC-SHA256",
  "key_id": "...",
  "timestamp": "2024-01-01T00:00:00Z"
}
```

---

## 🐛 Detekcja Błędów

### Architektura / Architecture

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

### Wbudowane wzorce / Built-in Patterns

| Pattern ID | Category | Severity | Regex |
|------------|----------|----------|-------|
| `network_timeout` | Timeout | Medium | `(timeout\|timed out\|deadline exceeded)` |
| `auth_failure` | Authentication | High | `(unauthorized\|forbidden\|401\|403)` |
| `rate_limit` | RateLimit | Medium | `(rate.?limit\|too many requests\|429)` |
| `validation_error` | Validation | Low | `(validation.*fail\|invalid.*data)` |
| `connection_error` | Network | High | `(connection.*refused\|503)` |

### Analiza błędów / Error Analysis

```bash
# Analizuj komunikat / Analyze message
ticket-pipeline errors analyze \
  --message "Request timeout after 30s" \
  --source "helpcenter_api"

# Wynik / Result:
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

### Dodawanie custom patterns / Adding Custom Patterns

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

### Statystyki / Statistics

```bash
# Pokaż statystyki / Show statistics
ticket-pipeline errors stats

# Wynik / Result:
Timeout: 15
Authentication: 3
RateLimit: 7
Validation: 2
Network: 5
```

### Korelacja błędów / Error Correlation

```rust
// Koreluj błędy w oknie 5 minut / Correlate errors in 5-minute window
let correlations = engine.correlate_errors(Duration::minutes(5));

for group in correlations {
    println!("Correlated errors from {}: {}", group[0].source, group.len());
}
```

---

## 📊 Detekcja Anomalii

### Metody detekcji / Detection Methods

#### 1. Z-Score Detection

```rust
// Threshold: 3 standard deviations
let anomaly = detector.z_score_detection(metric_name, series, 3.0);
```

**Formula:** `z = |x - μ| / σ`

**Użycie / Usage:** Wykrywanie outlier'ów statystycznych

#### 2. IQR (Interquartile Range)

```rust
// Multiplier: 1.5 * IQR
let anomaly = detector.iqr_detection(metric_name, series, 1.5);
```

**Formula:** `bounds = [Q1 - 1.5*IQR, Q3 + 1.5*IQR]`

**Użycie / Usage:** Robustna detekcja outliers

#### 3. Moving Average

```rust
// Window: 10, Threshold: 2.0
let anomaly = detector.moving_average_detection(metric_name, series, 10, 2.0);
```

**Formula:** `deviation = |current - MA| / MA`

**Użycie / Usage:** Wykrywanie nagłych zmian

#### 4. Exponential Smoothing

```rust
// Alpha: 0.3, Threshold: 2.0
let anomaly = detector.exponential_smoothing_detection(metric_name, series, 0.3, 2.0);
```

**Formula:** `smoothed = α * current + (1-α) * previous`

**Użycie / Usage:** Wygładzanie szumu, wykrywanie trendów

### Typy anomalii / Anomaly Types

```rust
pub enum AnomalyType {
    Spike,        // Nagły wzrost / Sudden increase
    Drop,         // Nagły spadek / Sudden decrease
    Trend,        // Stopniowa zmiana / Gradual change
    Seasonality,  // Wzorzec sezonowy / Seasonal pattern
    Outlier,      // Statystyczny outlier / Statistical outlier
}
```

### Monitorowanie metryk / Monitoring Metrics

```rust
// Śledź metrykę / Track metric
detector.track_metric("error_rate", 1000);

// Zapisz wartość / Record value
detector.record("error_rate", 5.0);

// Wykryj anomalie / Detect anomalies
let anomalies = detector.detect();
```

### Przykład użycia / Usage Example

```bash
# Testuj detekcję anomalii / Test anomaly detection
ticket-pipeline errors anomalies

# Wynik / Result:
Detected 1 anomalies

  • Z-score anomaly: 4.52 (threshold: 3.00)
    Metric: error_rate
    Current value: 50.0
    Expected range: [2.5, 7.5]
    Severity: 0.75
    Confidence: 1.00
```

---

## 🔄 Integracja CI/CD

### Workflow: Security & Encryption

**Plik:** `.github/workflows/security-encryption.yml`

**Triggery:**
- Push do `src/crypto/**`
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

**Plik:** `.github/workflows/error-detection.yml`

**Triggery:**
- Push do `src/error_detection/**`
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

### Sekrety GitHub / GitHub Secrets

```bash
# Szyfrowanie / Encryption
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

### Generowanie kluczy / Generating Keys

```bash
# Encryption key
openssl rand -base64 32

# Signing key
openssl rand -base64 32

# Master key (for hierarchical management)
openssl rand -base64 32
```

---

## 📈 Metryki i Monitoring

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

### Logi audytowe / Audit Logs

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

## 🎯 Best Practices

### 1. Zarządzanie kluczami / Key Management

- ✓ Używaj silnych kluczy (min. 32 bytes)
- ✓ Rotuj klucze regularnie (co 30 dni)
- ✓ Przechowuj klucze w Secure Vault
- ✓ Nigdy nie commituj kluczy do git
- ✓ Używaj zmiennych środowiskowych

### 2. Szyfrowanie / Encryption

- ✓ Szyfruj wszystkie wrażliwe dane
- ✓ Używaj authenticated encryption (AES-GCM)
- ✓ Weryfikuj checksumy po deszyfracji
- ✓ Śledź metadane szyfrowania

### 3. Detekcja błędów / Error Detection

- ✓ Monitoruj wszystkie operacje I/O
- ✓ Ustaw odpowiednie progi anomalii
- ✓ Koreluj błędy w oknie czasowym
- ✓ Automatycznie alertuj przy critical errors
- ✓ Regularnie przeglądaj statystyki

### 4. Bezpieczeństwo / Security

- ✓ Używaj HTTPS dla wszystkich API calls
- ✓ Weryfikuj podpisy cyfrowe
- ✓ Implementuj rate limiting
- ✓ Używaj circuit breaker pattern
- ✓ Loguj wszystkie operacje audytowe

---

## 📚 Dodatkowe zasoby / Additional Resources

- [Rust Crypto Documentation](https://docs.rs/aes-gcm)
- [OWASP Cryptographic Guidelines](https://owasp.org/www-project-cheat-sheets/)
- [Prometheus Best Practices](https://prometheus.io/docs/practices/)

---

**Dokumentacja wersji / Version:** 2.0  
**Data aktualizacji / Last updated:** 2024-01-01  
**Autor / Author:** Ticket Pipeline Team
