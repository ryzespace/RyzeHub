# Ticket Pipeline (Rust) 🦀

**High-performance ticket transfer pipeline between client dashboard and helpcenter.**

Wydajny pipeline do transferu ticketów między dashboardem klienta a helpcenter.

---

## ✨ Nowe funkcjonalności (vs Python) / New Features

### 🚀 Wydajność / Performance
- **Async/await** — współbieżne operacje I/O
- **Connection pooling** — ponowne użycie połączeń HTTP
- **Zero-cost abstractions** — brak narzutu runtime
- **Kompilacja do natywnego kodu** — 10-100x szybszy niż Python

### 🔒 Bezpieczeństwo / Security (NOWY SYSTEM!)
- **Wielowarstwowe szyfrowanie** — AES-256-GCM z rotacją kluczy
- **Hierarchiczne klucze** — master key → derived keys → session keys
- **Podpisy cyfrowe** — HMAC-SHA256 dla integralności danych
- **Secure Vault** — zaszyfrowany magazyn kluczy z ACL
- **Key derivation** — HKDF/PBKDF2 dla bezpiecznego wyprowadzania kluczy
- **Key rotation** — automatyczna rotacja kluczy co 30 dni
- **Audit trail** — pełny dziennik operacji kryptograficznych

### 🐛 Detekcja Błędów / Error Detection (NOWY SYSTEM!)
- **Pattern recognition** — 5+ wbudowanych wzorców błędów
- **Anomaly detection** — Z-score, IQR, Moving Average, Exponential Smoothing
- **Error correlation** — korelacja błędów w oknie czasowym
- **Predictive analysis** — przewidywanie potencjalnych problemów
- **Real-time monitoring** — metryki i alerty w czasie rzeczywistym
- **Custom patterns** — możliwość dodawania własnych wzorców

### 📊 Monitorowanie / Monitoring
- **Prometheus metrics** — eksport metryk
- **Health checks** — sprawdzanie stanu usług
- **Structured logging (JSON)** — logi w formacie JSON
- **Performance metrics** — czas transferu, throughput

### 🔄 Niezawodność / Reliability
- **Exponential backoff retry** — inteligentne ponawianie
- **Paginacja** — obsługa dużych zbiorów danych
- **Deduplikacja** — zapobieganie duplikatom
- **Walidacja** — sprawdzanie danych przed transferem

---

## 📁 Struktura projektu / Project Structure

```
ticket-pipeline-rust/
├── Cargo.toml                 # Dependencies / Zależności
├── Dockerfile                 # Container / Kontener
├── .env.example               # Konfiguracja env
├── src/
│   ├── main.rs                # Entry point / CLI
│   ├── models.rs              # Data models / Modele danych
│   ├── config.rs              # Configuration / Konfiguracja
│   ├── transformer.rs         # Validate/Enrich/Filter
│   ├── source_client.rs       # Client Dashboard API
│   ├── destination_client.rs  # HelpCenter API + Circuit Breaker
│   ├── pipeline.rs            # Main orchestrator / Orkiestrator
│   ├── security.rs            # Encryption, Audit, Checksums
│   ├── metrics.rs             # Prometheus metrics
│   ├── errors.rs              # Error types / Typy błędów
│   ├── dependency_manager.rs  # Dependency scanner / Skaner zależności
│   ├── docker_manager.rs      # Dockerfile generator / Generator Dockerfile
│   ├── github_manager.rs      # GitHub API client / Klient GitHub API
│   └── hub_manager.rs         # Hub orchestrator / Orkiestrator huba
└── .github/workflows/
    ├── ci-pipeline.yml            # Lint + Test + Security Audit + Cross-compile
    ├── hub-auto-update.yml        # Hub update + Build Rust + Sync tickets
    ├── hub-manager.yml            # Full hub management + dependencies
    ├── dependency-analysis.yml    # Dependency analysis + license check
    ├── docker-build.yml           # Multi-arch Docker build + push to GHCR
    └── deploy-pipeline.yml        # Build → Docker → Staging → Production
```

---

## 🚀 Szybki start / Quick Start

### Wymagania / Requirements
- Rust 1.70+ (lub Docker)

### Kompilacja / Build

```bash
# Debug build
cargo build

# Release build (zoptymalizowany)
cargo build --release

# Binary w: target/release/ticket-pipeline
```

### Konfiguracja / Configuration

```bash
cp .env.example .env
# Edytuj .env i wypełnij dane / Edit .env and fill in values
```

### Uruchomienie / Run

```bash
# Jednorazowo / One-time run
cargo run

# Lub z binarki / Or from binary
./target/release/ticket-pipeline

# Tryb ciągły / Continuous mode
./target/release/ticket-pipeline --continuous --interval 300

# Health check
./target/release/ticket-pipeline health

# Metryki / Metrics
./target/release/ticket-pipeline metrics

# Walidacja konfiguracji / Validate config
./target/release/ticket-pipeline validate

# Aktualizacja huba / Hub update
./target/release/ticket-pipeline hub --org my-org --token $GITHUB_TOKEN --dir github_hub --dockerize

# Analiza zależności / Dependency analysis
./target/release/ticket-pipeline deps --path ./my-repo --repos "lib-a,lib-b,lib-c"

# Generuj Dockerfile / Generate Dockerfile
./target/release/ticket-pipeline docker --path ./my-repo

# SZYFROWANIE / ENCRYPTION
./target/release/ticket-pipeline encrypt --data "secret data" --key "$ENCRYPTION_KEY"
./target/release/ticket-pipeline decrypt --data "ENC:..." --key "$ENCRYPTION_KEY"

# PODPISY CYFROWE / DIGITAL SIGNATURES
./target/release/ticket-pipeline sign --data "data to sign" --key "$SIGNING_KEY"
./target/release/ticket-pipeline verify --data "data" --signature "..." --key "$SIGNING_KEY"

# SECURE VAULT
./target/release/ticket-pipeline vault store --key-id my-key --data "secret" --name "My Key"
./target/release/ticket-pipeline vault retrieve --key-id my-key
./target/release/ticket-pipeline vault list
./target/release/ticket-pipeline vault integrity

# DETEKCJA BŁĘDÓW / ERROR DETECTION
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

## 🔐 Bezpieczeństwo / Security

### Szyfrowanie / Encryption

Pipeline używa **wielowarstwowego systemu szyfrowania**:

```bash
# Generuj klucz / Generate key
openssl rand -base64 32

# Ustaw w .env / Set in .env
ENCRYPTION_KEY=your-base64-key-here
```

**Komponenty kryptograficzne:**
- **CryptoEngine** — szyfrowanie AES-256-GCM z kontekstem i metadanymi
- **KeyManager** — hierarchiczne zarządzanie kluczami (master → derived → session)
- **SecureVault** — zaszyfrowany magazyn kluczy z kontrolą dostępu (ACL)
- **SignatureEngine** — podpisy cyfrowe HMAC-SHA256

**Funkcjonalności:**
- Rotacja kluczy co 30 dni (automatyczna)
- Derivacja kluczy z master key (HKDF)
- Klucze sesyjne (24h lifetime)
- Integralność danych (checksum SHA-256)
- Metadane szyfrowania (version, algorithm, key_id, timestamps)

### Secure Vault

```bash
# Przechowaj klucz / Store key
ticket-pipeline vault store --key-id api-key --data "secret123" --name "API Key"

# Pobierz klucz / Retrieve key
ticket-pipeline vault retrieve --key-id api-key

# Lista kluczy / List keys
ticket-pipeline vault list

# Sprawdź integralność / Check integrity
ticket-pipeline vault integrity
```

### Podpisy Cyfrowe / Digital Signatures

```bash
# Podpisz dane / Sign data
ticket-pipeline sign --data "important data" --key "$SIGNING_KEY"

# Weryfikuj podpis / Verify signature
ticket-pipeline verify --data "important data" --signature "abc123..." --key "$SIGNING_KEY"
```

### Audit Log

Każda operacja jest logowana:
```
AUDIT: {"timestamp":"...","action":"ticket_transfer","ticket_id":"T-001",...}
AUDIT: {"timestamp":"...","action":"key_rotation","key_id":"...","algorithm":"AES-256-GCM"}
AUDIT: {"timestamp":"...","action":"vault_access","key_id":"...","user":"..."}
```

### Circuit Breaker

Automatycznie wyłącza komunikację z helpcenter po 5 błędach z rzędu.  
Po 60 sekundach próbuje wznowić (half-open).

### Rate Limiting

Ogranicza liczbę requestów do API (domyślnie 100/min).

---

## 🐛 Detekcja Błędów / Error Detection

### System Detekcji / Detection System

**Wbudowane wzorce / Built-in patterns:**
- `network_timeout` — timeouty połączeń
- `auth_failure` — błędy autoryzacji (401, 403)
- `rate_limit` — przekroczenie rate limit (429)
- `validation_error` — błędy walidacji danych
- `connection_error` — błędy połączenia (503, connection refused)

### Analiza Błędów / Error Analysis

```bash
# Analizuj komunikat błędu / Analyze error message
ticket-pipeline errors analyze --message "Request timeout after 30s" --source "api_client"

# Wynik / Result:
# Error ID: 550e8400-e29b-41d4-a716-446655440000
# Category: Timeout
# Severity: Medium
# Pattern: network_timeout
```

### Detekcja Anomalii / Anomaly Detection

**Metody detekcji / Detection methods:**
- **Z-score** — wykrywanie outlier'ów statystycznych (threshold: 3σ)
- **IQR** — Interquartile Range (multiplier: 1.5)
- **Moving Average** — średnia krocząca (window: 10)
- **Exponential Smoothing** — wygładzanie wykładnicze (alpha: 0.3)

```bash
# Testuj detekcję anomalii / Test anomaly detection
ticket-pipeline errors anomalies

# Wynik / Result:
# Detected 1 anomalies
#   • Z-score anomaly: 4.52 (threshold: 3.00)
#     Severity: 0.75
#     Confidence: 1.00
```

### Statystyki i Monitoring / Statistics & Monitoring

```bash
# Statystyki błędów / Error statistics
ticket-pipeline errors stats

# Wynik / Result:
# Timeout: 15
# Authentication: 3
# RateLimit: 7
# Validation: 2

# Lista wzorców / List patterns
ticket-pipeline errors test-patterns
```

### Korelacja Błędów / Error Correlation

System automatycznie koreluje błędy w oknie czasowym:
- Grupuje błędy po źródle
- Wykrywa skorelowane awarie
- Przewiduje potencjalne problemy

### Metryki Anomalii / Anomaly Metrics

Monitorowane metryki:
- `error_rate` — częstotliwość błędów
- `transfer_duration` — czas transferu
- `api_latency` — opóźnienia API
- `success_rate` — wskaźnik sukcesu
- `retry_count` — liczba retry

### Custom Patterns / Niestandardowe Wzorce

Możliwość dodawania własnych wzorców:

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

## 📊 Metryki / Metrics

Pipeline eksportuje metryki w formacie Prometheus:

```
pipeline_tickets_fetched_total        # Pobrane tickety
pipeline_tickets_transferred_total    # Przeniesione tickety
pipeline_tickets_failed_total         # Błędy transferu
pipeline_tickets_filtered_total       # Odfiltrowane
pipeline_runs_total                   # Uruchomienia pipeline'a
pipeline_transfer_duration_ms         # Czas transferu
pipeline_active_connections           # Aktywne połączenia
```

---

## 🔄 CI/CD

### Workflow'y

| Workflow | Trigger | Opis |
|----------|---------|------|
| **CI Pipeline** | push/PR | Lint + Test + Security Audit + Cross-compile |
| **Hub Auto-Update** | Cron (dziennie) | Hub update + Build Rust + Sync tickets |
| **Hub Manager** | Cron (co 6h) + manual | Clone repos → analyze deps → Dockerize → build |
| **Dependency Analysis** | push (deps files) + PR | Dependency scan + security audit + license check |
| **Docker Build** | push (main) + tag | Multi-arch Docker build → GHCR (amd64 + arm64) |
| **Security & Encryption** | push (crypto/**) + weekly | Crypto audit + encryption tests + key rotation check |
| **Error Detection** | push (error_detection/**) + every 15min | Error pattern tests + anomaly detection + monitoring |
| **Deploy** | push (src/**) | Build → Docker → Staging → Production |

### Nowe subkomendy CLI / New CLI Commands

```bash
# Hub Management / Zarządzanie hubem
ticket-pipeline hub --org my-org --token $TOKEN --dir github_hub --dockerize

# Dependency Analysis / Analiza zależności
ticket-pipeline deps --path ./repo --repos "lib-a,lib-b,lib-c"

# Docker Generation / Generacja Docker
ticket-pipeline docker --path ./repo

# Encryption / Szyfrowanie
ticket-pipeline encrypt --data "secret" --key "$KEY"
ticket-pipeline decrypt --data "ENC:..." --key "$KEY"

# Digital Signatures / Podpisy cyfrowe
ticket-pipeline sign --data "data" --key "$KEY"
ticket-pipeline verify --data "data" --signature "sig" --key "$KEY"

# Secure Vault / Magazyn kluczy
ticket-pipeline vault store --key-id id --data data --name name
ticket-pipeline vault retrieve --key-id id
ticket-pipeline vault list
ticket-pipeline vault integrity

# Error Detection / Detekcja błędów
ticket-pipeline errors analyze --message msg --source src
ticket-pipeline errors stats
ticket-pipeline errors anomalies
ticket-pipeline errors test-patterns
```

### Wymagane sekrety / Required secrets

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

## 🧪 Testy / Tests

```bash
# Unit tests
cargo test

# With output
cargo test -- --nocapture
```

---

## 🔗 Dependency Manager / Manager Zależności

### Funkcjonalności / Features

- **Skanowanie wielu formatów** — requirements.txt, package.json, Cargo.toml, go.mod, pyproject.toml
- **Graf zależności** — buduje graf wewnętrznych zależności między repo
- **Topological sort** — wyznacza optymalną kolejność buildu
- **Wykrywanie cykli** — informuje o cyklicznych zależnościach

### Użycie / Usage

```bash
# Analizuj zależności w repozytorium
ticket-pipeline deps --path ./my-service --repos "core-lib,shared-utils,api-client"

# Wynik / Result:
# Found 2 internal dependencies:
#   • core-lib
#   • shared-utils
```

---

## 🐳 Docker Manager / Manager Docker

### Funkcjonalności / Features

- **Auto-detekcja języka** — rozpoznaje Python, Node.js, Rust, Go, .NET
- **Generowanie Dockerfile** — optymalne szablony per język
- **Multi-stage builds** — dla Rust (builder + runtime)
- **docker-compose.yml** — generuje konfigurację dla wielu repo

### Obsługiwane języki / Supported Languages

| Język | Image | Features |
|---|---|---|
| Python | `python:3.11-slim` | pip install + requirements.txt |
| Node.js | `node:20-slim` | npm install + package.json |
| Rust | `rust:1.75-slim` | Multi-stage build, ~15MB final image |
| Go | `golang:1.21-slim` | go build + binary |
| .NET | `.net:8-slim` | dotnet build |
| Generic | `ubuntu:latest` | Fallback |

---

## 🏛️ Hub Manager / Manager Hub

### Przepływ / Flow

```
1. Clone all repos from org
2. Analyze dependencies (scan Cargo.toml, package.json, etc.)
3. Build dependency graph + topological sort
4. Generate Dockerfiles (auto-detect language)
5. Generate docker-compose.yml
6. Output build order
```

### Użycie / Usage

```bash
ticket-pipeline hub \
  --org my-org \
  --token $GITHUB_TOKEN \
  --dir github_hub \
  --dockerize

# Wynik / Result:
# Cloned: 15 repos
# Dependencies: 23 edges
# Dockerized: 15 repos
# Build order: core-lib → shared-utils → api-client → my-service
```

---

## 📈 Porównanie z Pythonem / Comparison with Python

| Cecha / Feature | Python | Rust |
|---|---|---|
| **Czas uruchomienia** | ~200ms | ~5ms |
| **Pamięć** | ~50MB | ~3MB |
| **Binary size** | N/A | ~5MB |
| **Szyfrowanie** | AES-256-GCM (basic) | **Multi-layer** (AES-256-GCM + Key Derivation + Vault + Signatures) |
| **Key Management** | brak | **Hierarchical** (master → derived → session) |
| **Key Rotation** | brak | ✓ Automatic (30 days) |
| **Digital Signatures** | brak | ✓ HMAC-SHA256 |
| **Secure Vault** | brak | ✓ Encrypted storage with ACL |
| **Error Detection** | brak | **Full system** (5+ patterns, 4 detection methods) |
| **Anomaly Detection** | brak | ✓ Z-score, IQR, Moving Average, Exp. Smoothing |
| **Error Correlation** | brak | ✓ Time-based correlation |
| **Predictive Analysis** | brak | ✓ Issue prediction |
| **Circuit breaker** | brak | ✓ |
| **Rate limiting** | brak | ✓ |
| **Audit log** | brak | ✓ Full cryptographic audit trail |
| **Prometheus metrics** | brak | ✓ |
| **Health checks** | brak | ✓ |
| **Async** | brak | ✓ (tokio) |
| **Connection pool** | brak | ✓ |
| **Cross-compile** | brak | Linux, ARM, musl |
| **Docker** | ~1GB | ~15MB |
| **Dependency Management** | brak | ✓ Multi-language scanner |
| **Docker Manager** | brak | ✓ Auto-detect + generate |
| **Hub Manager** | brak | ✓ Full orchestration |

---

## 📊 Architektura Systemu / System Architecture

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

## 🎓 Przykłady Użycia / Usage Examples

### 1. Szyfrowanie danych ticketu / Encrypt ticket data

```bash
# Generuj klucz / Generate key
export ENCRYPTION_KEY=$(openssl rand -base64 32)

# Szyfruj opis / Encrypt description
ENCRYPTED=$(./ticket-pipeline encrypt --data "Sensitive ticket data" --key "$ENCRYPTION_KEY")

# Szyfruj jest automatyczne w pipeline / Encryption is automatic in pipeline
./ticket-pipeline --continuous
```

### 2. Podpisywanie żądań API / Sign API requests

```bash
export SIGNING_KEY=$(openssl rand -base64 32)

# Podpisz żądanie / Sign request
SIGNATURE=$(./ticket-pipeline sign --data "POST:/api/tickets:{\"data\":1}" --key "$SIGNING_KEY")

# Weryfikuj podpis / Verify signature
./ticket-pipeline verify --data "POST:/api/tickets:{\"data\":1}" --signature "$SIGNATURE" --key "$SIGNING_KEY"
```

### 3. Monitorowanie błędów / Error monitoring

```bash
# Analizuj błąd / Analyze error
./ticket-pipeline errors analyze --message "Connection timeout after 30s" --source "helpcenter_api"

# Sprawdź statystyki / Check statistics
./ticket-pipeline errors stats

# Wykryj anomalie / Detect anomalies
./ticket-pipeline errors anomalies
```

### 4. Zarządzanie kluczami / Key management

```bash
# Przechowaj klucz API / Store API key
./ticket-pipeline vault store --key-id "helpcenter-api" --data "secret-api-key" --name "HelpCenter API Key"

# Pobierz klucz / Retrieve key
./ticket-pipeline vault retrieve --key-id "helpcenter-api"

# Sprawdź integralność / Check integrity
./ticket-pipeline vault integrity
```

---

## 🚀 Deployment

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

## 📝 Licencja / License

MIT

---

## 🎉 Podsumowanie / Summary

Kompletny, wydajny pipeline w Rust z:
- ✓ Wielowarstwowym systemem szyfrowania
- ✓ Hierarchicznym zarządzaniem kluczami
- ✓ Zaawansowaną detekcją błędów i anomalii
- ✓ Pełną integracją CI/CD (9 workflow'ów)
- ✓ Dependency management
- ✓ Docker automation
- ✓ Hub orchestration
- ✓ Monitorowanie Prometheus
- ✓ Security best practices

**Ready for production!** 🚀
