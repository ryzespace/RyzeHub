# Ticket Pipeline (Rust)

**High-performance ticket transfer pipeline between client dashboard and helpcenter.**

---

## New Features (vs Python)

### Performance
- **Async/await** — concurrent I/O operations
- **Connection pooling** — HTTP connection reuse
- **Zero-cost abstractions** — no runtime overhead
- **Native code compilation** — 10-100x faster than Python

### Security (NEW SYSTEM!)
- **Multi-layer encryption** — AES-256-GCM with key rotation
- **Hierarchical keys** — master key → derived keys → session keys
- **Digital signatures** — HMAC-SHA256 for data integrity
- **Secure Vault** — encrypted key storage with ACL
- **Key derivation** — HKDF/PBKDF2 for secure key derivation
- **Key rotation** — automatic key rotation every 30 days
- **Audit trail** — full cryptographic operation log

### Error Detection (NEW SYSTEM!)
- **Pattern recognition** — 5+ built-in error patterns
- **Anomaly detection** — Z-score, IQR, Moving Average, Exponential Smoothing
- **Error correlation** — error correlation in time window
- **Predictive analysis** — prediction of potential issues
- **Real-time monitoring** — real-time metrics and alerts
- **Custom patterns** — ability to add custom patterns

### Monitoring
- **Prometheus metrics** — metrics export
- **Health checks** — service status checking
- **Structured logging (JSON)** — logs in JSON format
- **Performance metrics** — transfer time, throughput

### Reliability
- **Exponential backoff retry** — intelligent retry
- **Pagination** — handling large datasets
- **Deduplication** — preventing duplicates
- **Validation** — data checking before transfer

---

## Project Structure

```
ticket-pipeline-rust/
├── Cargo.toml                 # Dependencies
├── Dockerfile                 # Container
├── .env.example               # Environment configuration
├── src/
│   ├── main.rs                # Entry point / CLI
│   ├── models.rs              # Data models
│   ├── config.rs              # Configuration
│   ├── transformer.rs         # Validate/Enrich/Filter
│   ├── source_client.rs       # Client Dashboard API
│   ├── destination_client.rs  # HelpCenter API + Circuit Breaker
│   ├── pipeline.rs            # Main orchestrator
│   ├── security.rs            # Encryption, Audit, Checksums
│   ├── metrics.rs             # Prometheus metrics
│   ├── errors.rs              # Error types
│   ├── dependency_manager.rs  # Dependency scanner
│   ├── docker_manager.rs      # Dockerfile generator
│   ├── github_manager.rs      # GitHub API client
│   └── hub_manager.rs         # Hub orchestrator
└── .github/workflows/
    ├── ci-pipeline.yml            # Lint + Test + Security Audit + Cross-compile
    ├── hub-auto-update.yml        # Hub update + Build Rust + Sync tickets
    ├── hub-manager.yml            # Full hub management + dependencies
    ├── dependency-analysis.yml    # Dependency analysis + license check
    ├── docker-build.yml           # Multi-arch Docker build + push to GHCR
    └── deploy-pipeline.yml        # Build → Docker → Staging → Production
```

---

## Quick Start

### Requirements
- Rust 1.70+ (or Docker)

### Build

```bash
# Debug build
cargo build

# Release build (optimized)
cargo build --release

# Binary is in: target/release/ticket-pipeline
```

### Configuration

```bash
cp .env.example .env
# Edit .env and fill in values
```

### Run

```bash
# One-time run
cargo run

# Or from binary
./target/release/ticket-pipeline

# Continuous mode
./target/release/ticket-pipeline --continuous --interval 300

# Health check
./target/release/ticket-pipeline health

# Metrics
./target/release/ticket-pipeline metrics

# Validate config
./target/release/ticket-pipeline validate

# Hub update
./target/release/ticket-pipeline hub --org my-org --token $GITHUB_TOKEN --dir github_hub --dockerize

# Dependency analysis
./target/release/ticket-pipeline deps --path ./my-repo --repos "RyzeSpace.Client,RyzeSpace.HelpCenter,RyzeSpace.AdminPanel,RyzeSpace.Mobile,RyzeSpace.Desktop"

# Generate Dockerfile
./target/release/ticket-pipeline docker --path ./my-repo

# ENCRYPTION
./target/release/ticket-pipeline encrypt --data "secret data" --key "$ENCRYPTION_KEY"
./target/release/ticket-pipeline decrypt --data "ENC:..." --key "$ENCRYPTION_KEY"

# DIGITAL SIGNATURES
./target/release/ticket-pipeline sign --data "data to sign" --key "$SIGNING_KEY"
./target/release/ticket-pipeline verify --data "data" --signature "..." --key "$SIGNING_KEY"

# SECURE VAULT
./target/release/ticket-pipeline vault store --key-id my-key --data "secret" --name "My Key"
./target/release/ticket-pipeline vault retrieve --key-id my-key
./target/release/ticket-pipeline vault list
./target/release/ticket-pipeline vault integrity

# ERROR DETECTION
./target/release/ticket-pipeline errors analyze --message "Request timeout" --source "api"
./target/release/ticket-pipeline errors stats
./target/release/ticket-pipeline errors anomalies
./target/release/ticket-pipeline errors test-patterns
```

### Docker

```bash
# Build
docker build -t ticket-pipeline .

# Run
docker run --env-file .env ticket-pipeline

# Continuous mode
docker run --env-file .env ticket-pipeline --continuous
```

---

## Security

### Encryption

Pipeline uses **multi-layer encryption system**:

```bash
# Generate key
openssl rand -base64 32

# Set in .env
ENCRYPTION_KEY=your-base64-key-here
```

**Cryptographic components:**
- **CryptoEngine** — AES-256-GCM encryption with context and metadata
- **KeyManager** — hierarchical key management (master → derived → session)
- **SecureVault** — encrypted key storage with access control (ACL)
- **SignatureEngine** — HMAC-SHA256 digital signatures

**Features:**
- Key rotation every 30 days (automatic)
- Key derivation from master key (HKDF)
- Session keys (24h lifetime)
- Data integrity (SHA-256 checksum)
- Encryption metadata (version, algorithm, key_id, timestamps)

### Secure Vault

```bash
# Store key
ticket-pipeline vault store --key-id api-key --data "secret123" --name "API Key"

# Retrieve key
ticket-pipeline vault retrieve --key-id api-key

# List keys
ticket-pipeline vault list

# Check integrity
ticket-pipeline vault integrity
```

### Digital Signatures

```bash
# Sign data
ticket-pipeline sign --data "important data" --key "$SIGNING_KEY"

# Verify signature
ticket-pipeline verify --data "important data" --signature "abc123..." --key "$SIGNING_KEY"
```

### Audit Log

Every operation is logged:
```
AUDIT: {"timestamp":"...","action":"ticket_transfer","ticket_id":"T-001",...}
AUDIT: {"timestamp":"...","action":"key_rotation","key_id":"...","algorithm":"AES-256-GCM"}
AUDIT: {"timestamp":"...","action":"vault_access","key_id":"...","user":"..."}
```

### Circuit Breaker

Automatically disables communication with helpcenter after 5 consecutive errors.
After 60 seconds, attempts to resume (half-open).

### Rate Limiting

Limits number of requests to API (default 100/min).

---

## Error Detection

### Detection System

**Built-in patterns:**
- `network_timeout` — connection timeouts
- `auth_failure` — authorization errors (401, 403)
- `rate_limit` — rate limit exceeded (429)
- `validation_error` — data validation errors
- `connection_error` — connection errors (503, connection refused)

### Error Analysis

```bash
# Analyze error message
ticket-pipeline errors analyze --message "Request timeout after 30s" --source "api_client"

# Result:
# Error ID: 550e8400-e29b-41d4-a716-446655440000
# Category: Timeout
# Severity: Medium
# Pattern: network_timeout
```

### Anomaly Detection

**Detection methods:**
- **Z-score** — statistical outlier detection (threshold: 3σ)
- **IQR** — Interquartile Range (multiplier: 1.5)
- **Moving Average** — moving average (window: 10)
- **Exponential Smoothing** — exponential smoothing (alpha: 0.3)

```bash
# Test anomaly detection
ticket-pipeline errors anomalies

# Result:
# Detected 1 anomalies
#   Z-score anomaly: 4.52 (threshold: 3.00)
#     Severity: 0.75
#     Confidence: 1.00
```

### Statistics & Monitoring

```bash
# Error statistics
ticket-pipeline errors stats

# Result:
# Timeout: 15
# Authentication: 3
# RateLimit: 7
# Validation: 2

# List patterns
ticket-pipeline errors test-patterns
```

### Error Correlation

System automatically correlates errors in time window:
- Groups errors by source
- Detects correlated failures
- Predicts potential issues

### Anomaly Metrics

Monitored metrics:
- `error_rate` — error frequency
- `transfer_duration` — transfer time
- `api_latency` — API latency
- `success_rate` — success rate
- `retry_count` — retry count

### Custom Patterns

Ability to add custom patterns:

```rust
let pattern = ErrorPattern {
    id: "custom_error".to_string(),
    name: "Custom Error".to_string(),
    description: "Custom error pattern".to_string(),
    regex_pattern: r"(?i)(custom.*error)".to_string(),
    category: ErrorCategory::Unknown,
    severity: ErrorSeverity::Medium,
    // ...
};

engine.add_pattern(pattern);
```

---

## Metrics

Pipeline exports metrics in Prometheus format:

```
pipeline_tickets_fetched_total        # Fetched tickets
pipeline_tickets_transferred_total    # Transferred tickets
pipeline_tickets_failed_total         # Transfer errors
pipeline_tickets_filtered_total       # Filtered
pipeline_runs_total                   # Pipeline runs
pipeline_transfer_duration_ms         # Transfer time
pipeline_active_connections           # Active connections
```

---

## CI/CD

### Workflows

| Workflow | Trigger | Description |
|----------|---------|-------------|
| **CI Pipeline** | push/PR | Lint + Test + Security Audit + Cross-compile |
| **Hub Auto-Update** | Cron (daily) | Hub update + Build Rust + Sync tickets |
| **Hub Manager** | Cron (every 6h) + manual | Clone repos → analyze deps → Dockerize → build |
| **Dependency Analysis** | push (deps files) + PR | Dependency scan + security audit + license check |
| **Docker Build** | push (main) + tag | Multi-arch Docker build → GHCR (amd64 + arm64) |
| **Security & Encryption** | push (crypto/**) + weekly | Crypto audit + encryption tests + key rotation check |
| **Error Detection** | push (error_detection/**) + every 15min | Error pattern tests + anomaly detection + monitoring |
| **Deploy** | push (src/**) | Build → Docker → Staging → Production |

### New CLI Commands

```bash
# Hub Management
ticket-pipeline hub --org my-org --token $TOKEN --dir github_hub --dockerize

# Dependency Analysis
ticket-pipeline deps --path ./repo --repos "RyzeSpace.Client,RyzeSpace.HelpCenter,RyzeSpace.AdminPanel,RyzeSpace.Mobile,RyzeSpace.Desktop"

# Docker Generation
ticket-pipeline docker --path ./repo

# Encryption
ticket-pipeline encrypt --data "secret" --key "$KEY"
ticket-pipeline decrypt --data "ENC:..." --key "$KEY"

# Digital Signatures
ticket-pipeline sign --data "data" --key "$KEY"
ticket-pipeline verify --data "data" --signature "sig" --key "$KEY"

# Secure Vault
ticket-pipeline vault store --key-id id --data data --name name
ticket-pipeline vault retrieve --key-id id
ticket-pipeline vault list
ticket-pipeline vault integrity

# Error Detection
ticket-pipeline errors analyze --message msg --source src
ticket-pipeline errors stats
ticket-pipeline errors anomalies
ticket-pipeline errors test-patterns
```

### Required secrets

```bash
# Pipeline
CLIENT_DASHBOARD_URL=https://...
CLIENT_DASHBOARD_API_KEY=...
HELPCENTER_URL=https://...
HELPCENTER_API_KEY=...

# Security
ENCRYPTION_KEY=<openssl rand -base64 32>
SIGNING_KEY=<openssl rand -base64 32>

# Hub Manager
HUB_GITHUB_TOKEN=ghp_...
HUB_ORG_NAME=my-org
```

---

## Tests

```bash
# Unit tests
cargo test

# With output
cargo test -- --nocapture
```

---

## Dependency Manager

### Features

- **Multi-format scanning** — requirements.txt, package.json, Cargo.toml, go.mod, pyproject.toml
- **Dependency graph** — builds internal dependency graph between repos
- **Topological sort** — determines optimal build order
- **Cycle detection** — reports cyclic dependencies

### Usage

```bash
# Analyze dependencies in repository
ticket-pipeline deps --path ./github_hub/RyzeSpace.Client --repos "RyzeSpace.Client,RyzeSpace.HelpCenter,RyzeSpace.AdminPanel,RyzeSpace.Mobile,RyzeSpace.Desktop"

# Result:
# Found 2 internal dependencies:
#   RyzeSpace.HelpCenter
#   RyzeSpace.Mobile
```

---

## Docker Manager

### Features

- **Language auto-detection** — recognizes Python, Node.js, Rust, Go, .NET
- **Dockerfile generation** — optimal templates per language
- **Multi-stage builds** — for Rust (builder + runtime)
- **docker-compose.yml** — generates configuration for multiple repos

### Supported Languages

| Language | Image | Features |
|---|---|---|
| Python | `python:3.11-slim` | pip install + requirements.txt |
| Node.js | `node:20-slim` | npm install + package.json |
| Rust | `rust:1.75-slim` | Multi-stage build, ~15MB final image |
| Go | `golang:1.21-slim` | go build + binary |
| .NET | `.net:8-slim` | dotnet build |
| Generic | `ubuntu:latest` | Fallback |

---

## Hub Manager

### Flow

```
1. Clone all repos from org
2. Analyze dependencies (scan Cargo.toml, package.json, etc.)
3. Build dependency graph + topological sort
4. Generate Dockerfiles (auto-detect language)
5. Generate docker-compose.yml
6. Output build order
```

### Usage

```bash
ticket-pipeline hub \
  --org my-org \
  --token $GITHUB_TOKEN \
  --dir github_hub \
  --dockerize

# Result:
# Cloned: 15 repos
# Dependencies: 23 edges
# Dockerized: 15 repos
# Build order: RyzeSpace.HelpCenter -> RyzeSpace.Client -> RyzeSpace.AdminPanel -> RyzeSpace.Mobile -> RyzeSpace.Desktop
```

---

## Comparison with Python

| Feature | Python | Rust |
|---|---|---|
| **Startup Time** | ~200ms | ~5ms |
| **Memory** | ~50MB | ~3MB |
| **Binary size** | N/A | ~5MB |
| **Encryption** | AES-256-GCM (basic) | **Multi-layer** (AES-256-GCM + Key Derivation + Vault + Signatures) |
| **Key Management** | None | **Hierarchical** (master → derived → session) |
| **Key Rotation** | None | ✓ Automatic (30 days) |
| **Digital Signatures** | None | ✓ HMAC-SHA256 |
| **Secure Vault** | None | ✓ Encrypted storage with ACL |
| **Error Detection** | None | **Full system** (5+ patterns, 4 detection methods) |
| **Anomaly Detection** | None | ✓ Z-score, IQR, Moving Average, Exp. Smoothing |
| **Error Correlation** | None | ✓ Time-based correlation |
| **Predictive Analysis** | None | ✓ Issue prediction |
| **Circuit breaker** | None | ✓ |
| **Rate limiting** | None | ✓ |
| **Audit log** | None | ✓ Full cryptographic audit trail |
| **Prometheus metrics** | None | ✓ |
| **Health checks** | None | ✓ |
| **Async** | None | ✓ (tokio) |
| **Connection pool** | None | ✓ |
| **Cross-compile** | None | Linux, ARM, musl |
| **Docker** | ~1GB | ~15MB |
| **Dependency Management** | None | ✓ Multi-language scanner |
| **Docker Manager** | None | ✓ Auto-detect + generate |
| **Hub Manager** | None | ✓ Full orchestration |

---

## System Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    TICKET PIPELINE (Rust)                      │
├─────────────────────────────────────────────────────────────┤
│                                                                │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │ Client       │    │   Pipeline   │    │  HelpCenter  │   │
│  │ Dashboard    │───▶│  Orchestrator│───▶│  Dashboard   │   │
│  │ (REST API)   │    │              │    │  (REST API)  │   │
│  └──────────────┘    └──────┬───────┘    └──────────────┘   │
│                              │                                │
│         ┌────────────────────┼────────────────────┐          │
│         │                    │                    │          │
│         ▼                    ▼                    ▼          │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐   │
│  │   Crypto     │    │    Error     │    │   Security   │   │
│  │   Engine     │    │  Detection   │    │   Manager    │   │
│  │              │    │              │    │              │   │
│  │ • AES-256    │    │ • Patterns   │    │ • Rate Limit │   │
│  │ • Key Mgmt   │    │ • Anomalies  │    │ • Circuit Brk│   │
│  │ • Vault      │    │ • Statistics │    │ • Audit Log  │   │
│  │ • Signatures │    │ • Predictive │    │ • Checksums  │   │
│  └──────────────┘    └──────────────┘    └──────────────┘   │
│                                                                │
└─────────────────────────────────────────────────────────────┘
```

---

## Usage Examples

### 1. Encrypt ticket data

```bash
# Generate key
export ENCRYPTION_KEY=$(openssl rand -base64 32)

# Encrypt description
ENCRYPTED=$(./ticket-pipeline encrypt --data "Sensitive ticket data" --key "$ENCRYPTION_KEY")

# Encryption is automatic in pipeline
./ticket-pipeline --continuous
```

### 2. Sign API requests

```bash
export SIGNING_KEY=$(openssl rand -base64 32)

# Sign request
SIGNATURE=$(./ticket-pipeline sign --data "POST:/api/tickets:{\"data\":1}" --key "$SIGNING_KEY")

# Verify signature
./ticket-pipeline verify --data "POST:/api/tickets:{\"data\":1}" --signature "$SIGNATURE" --key "$SIGNING_KEY"
```

### 3. Error monitoring

```bash
# Analyze error
./ticket-pipeline errors analyze --message "Connection timeout after 30s" --source "helpcenter_api"

# Check statistics
./ticket-pipeline errors stats

# Detect anomalies
./ticket-pipeline errors anomalies
```

### 4. Key management

```bash
# Store API key
./ticket-pipeline vault store --key-id "helpcenter-api" --data "secret-api-key" --name "HelpCenter API Key"

# Retrieve key
./ticket-pipeline vault retrieve --key-id "helpcenter-api"

# Check integrity
./ticket-pipeline vault integrity
```

---

## Deployment

### Docker Compose

```yaml
version: '3.8'
services:
  ticket-pipeline:
    image: ghcr.io/your-org/ticket-pipeline:latest
    environment:
      - CLIENT_DASHBOARD_URL=https://client.example.com/api
      - CLIENT_DASHBOARD_API_KEY=${CLIENT_API_KEY}
      - HELPCENTER_URL=https://helpcenter.example.com/api
      - HELPCENTER_API_KEY=${HC_API_KEY}
      - ENCRYPTION_KEY=${ENCRYPTION_KEY}
      - SIGNING_KEY=${SIGNING_KEY}
    command: --continuous --interval 300
    restart: always
```

### Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: ticket-pipeline
spec:
  replicas: 2
  selector:
    matchLabels:
      app: ticket-pipeline
  template:
    metadata:
      labels:
        app: ticket-pipeline
    spec:
      containers:
      - name: ticket-pipeline
        image: ghcr.io/your-org/ticket-pipeline:latest
        command: ["/app/ticket-pipeline", "--continuous"]
        envFrom:
        - secretRef:
            name: pipeline-secrets
        resources:
          requests:
            memory: "64Mi"
            cpu: "100m"
          limits:
            memory: "256Mi"
            cpu: "500m"
```

---

## License

MIT

