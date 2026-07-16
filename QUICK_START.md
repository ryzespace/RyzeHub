# 🚀 SZYBKI START / QUICK START GUIDE

## 1️⃣ Instalacja / Installation

```bash
# Zainstaluj Rust (jeśli jeszcze nie masz) / Install Rust
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# Sklonuj repozytorium / Clone repository
cd ticket-pipeline-rust

# Zbuduj projekt / Build project
cargo build --release

# Binary jest w: target/release/ticket-pipeline
```

---

## 2️⃣ Konfiguracja / Configuration

```bash
# Skopiuj template / Copy template
cp .env.example .env

# Edytuj .env i wypełnij / Edit .env and fill in
```

### Wymagane zmienne / Required Variables:

```bash
# Pipeline
CLIENT_DASHBOARD_URL=https://client.example.com/api/v1
CLIENT_DASHBOARD_API_KEY=your-client-api-key
HELPCENTER_URL=https://helpcenter.example.com/api/v1
HELPCENTER_API_KEY=your-helpcenter-api-key

# Security (GENERUJ KLUCZE / GENERATE KEYS)
ENCRYPTION_KEY=$(openssl rand -base64 32)
SIGNING_KEY=$(openssl rand -base64 32)
```

### Generowanie kluczy / Generate Keys:

```bash
# Encryption key
openssl rand -base64 32
# Wynik: np. aBcDeFgHiJkLmNoPqRsTuVwXyZ1234567890AbCdEfG=

# Signing key
openssl rand -base64 32
# Wynik: np. 1234567890AbCdEfGhIjKlMnOpQrStUvWxYzAbCdEf=
```

---

## 3️⃣ Pierwsze uruchomienie / First Run

```bash
# Walidacja konfiguracji / Validate configuration
./target/release/ticket-pipeline validate

# Health check
./target/release/ticket-pipeline health

# Jednorazowy transfer / One-time transfer
./target/release/ticket-pipeline

# Tryb ciągły (co 5 min) / Continuous mode (every 5 min)
./target/release/ticket-pipeline --continuous --interval 300
```

---

## 4️⃣ Testowanie szyfrowania / Test Encryption

```bash
# Ustaw klucz / Set key
export ENCRYPTION_KEY="your-base64-key-here"

# Szyfruj dane / Encrypt data
ENCRYPTED=$(./target/release/ticket-pipeline encrypt \
  --data "Sensitive ticket information" \
  --key "$ENCRYPTION_KEY")

echo "Encrypted: $ENCRYPTED"

# Deszyfruj dane / Decrypt data
./target/release/ticket-pipeline decrypt \
  --data "$ENCRYPTED" \
  --key "$ENCRYPTION_KEY"
```

---

## 5️⃣ Testowanie podpisów / Test Signatures

```bash
# Ustaw klucz / Set key
export SIGNING_KEY="your-base64-signing-key"

# Podpisz dane / Sign data
SIGNATURE=$(./target/release/ticket-pipeline sign \
  --data "Important API request" \
  --key "$SIGNING_KEY")

echo "Signature: $SIGNATURE"

# Weryfikuj podpis / Verify signature
./target/release/ticket-pipeline verify \
  --data "Important API request" \
  --signature "$SIGNATURE" \
  --key "$SIGNING_KEY"
```

---

## 6️⃣ Secure Vault

```bash
# Ustaw zmienne / Set variables
export ENCRYPTION_KEY="your-base64-key"
export VAULT_PATH="/tmp/my_vault.json"
export VAULT_USER="admin"

# Przechowaj klucz / Store key
./target/release/ticket-pipeline vault store \
  --key-id "api-key-123" \
  --data "secret-api-key-value" \
  --name "Production API Key"

# Lista kluczy / List keys
./target/release/ticket-pipeline vault list

# Pobierz klucz / Retrieve key
./target/release/ticket-pipeline vault retrieve \
  --key-id "api-key-123"

# Sprawdź integralność / Check integrity
./target/release/ticket-pipeline vault integrity
```

---

## 7️⃣ Detekcja błędów / Error Detection

```bash
# Analizuj błąd / Analyze error
./target/release/ticket-pipeline errors analyze \
  --message "Request timeout after 30s" \
  --source "helpcenter_api"

# Statystyki / Statistics
./target/release/ticket-pipeline errors stats

# Wykryj anomalie / Detect anomalies
./target/release/ticket-pipeline errors anomalies

# Lista wzorców / List patterns
./target/release/ticket-pipeline errors test-patterns
```

---

## 8️⃣ Docker

```bash
# Build
docker build -t ticket-pipeline .

# Run
docker run --env-file .env ticket-pipeline --continuous

# Run z vault / Run with vault
docker run \
  --env-file .env \
  -v /path/to/vault:/vault \
  -e VAULT_PATH=/vault/vault.json \
  ticket-pipeline --continuous
```

---

## 9️⃣ Hub Manager (opcjonalne / optional)

```bash
# Ustaw token GitHub / Set GitHub token
export GITHUB_TOKEN="ghp_your_token_here"

# Aktualizuj hub / Update hub
./target/release/ticket-pipeline hub \
  --org my-org \
  --token "$GITHUB_TOKEN" \
  --dir github_hub \
  --dockerize

# Analizuj zależności / Analyze dependencies
./target/release/ticket-pipeline deps \
  --path github_hub/service-a \
  --repos "lib-core,shared-utils,api-client"
```

---

## 🔟 Monitorowanie / Monitoring

### Prometheus Metrics

```bash
# Pobierz metryki / Get metrics
./target/release/ticket-pipeline metrics

# Wynik / Result:
# pipeline_tickets_fetched_total 150
# pipeline_tickets_transferred_total 145
# pipeline_tickets_failed_total 5
# pipeline_runs_total 10
# pipeline_transfer_duration_ms 2500
```

### Logi / Logs

```bash
# Debug mode
./target/release/ticket-pipeline --log-level debug

# JSON logs (dla ELK/Loki)
./target/release/ticket-pipeline --log-level info
```

---

## 📚 Dodatkowe zasoby / Additional Resources

- **Pełna dokumentacja / Full docs**: `README.md`
- **Dokumentacja bezpieczeństwa / Security docs**: `SECURITY_DOCS.md`
- **Podsumowanie projektu / Project summary**: `PROJECT_SUMMARY.md`

---

## 🐛 Rozwiązywanie problemów / Troubleshooting

### Problem: "ENCRYPTION_KEY not set"
```bash
export ENCRYPTION_KEY=$(openssl rand -base64 32)
```

### Problem: "Connection refused"
- Sprawdź URL-e w .env / Check URLs in .env
- Sprawdź firewall / Check firewall
- Sprawdź czy API jest dostępne / Check if API is accessible

### Problem: "Signature verification failed"
- Upewnij się że używasz tego samego klucza / Make sure you use the same key
- Sprawdź czy dane nie zostały zmodyfikowane / Check if data was modified

### Problem: "Vault permission denied"
- Sprawdź VAULT_USER / Check VAULT_USER
- Sprawdź uprawnienia ACL / Check ACL permissions

---

## ✅ Checklist przed produkcją / Pre-Production Checklist

- [ ] Skonfigurowano wszystkie zmienne env / All env variables configured
- [ ] Wygenerowano silne klucze / Strong keys generated
- [ ] Przetestowano szyfrowanie / Encryption tested
- [ ] Przetestowano podpisy / Signatures tested
- [ ] Przetestowano vault / Vault tested
- [ ] Przetestowano detekcję błędów / Error detection tested
- [ ] Skonfigurowano monitoring / Monitoring configured
- [ ] Skonfigurowano logowanie / Logging configured
- [ ] Przetestowano w Docker / Tested in Docker
- [ ] Przeglądnieto SECURITY_DOCS.md / Reviewed SECURITY_DOCS.md

---

**Powodzenia! / Good luck!** 🚀
