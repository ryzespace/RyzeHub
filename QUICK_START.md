# QUICK START GUIDE

## Installation

```bash
# Install Rust (if you don't have it)
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh

# Clone repository
cd ticket-pipeline-rust

# Build project
cargo build --release

# Binary is in: target/release/ticket-pipeline
```

---

## Configuration

```bash
# Copy template
cp .env.example .env

# Edit .env and fill in
```

### Required Variables:

```bash
# Pipeline
CLIENT_DASHBOARD_URL=https://client.example.com/api/v1
CLIENT_DASHBOARD_API_KEY=your-client-api-key
HELPCENTER_URL=https://helpcenter.example.com/api/v1
HELPCENTER_API_KEY=your-helpcenter-api-key

# Security (GENERATE KEYS)
ENCRYPTION_KEY=$(openssl rand -base64 32)
SIGNING_KEY=$(openssl rand -base64 32)
```

### Generate Keys:

```bash
# Encryption key
openssl rand -base64 32
# Result: e.g. aBcDeFgHiJkLmNoPqRsTuVwXyZ1234567890AbCdEfG=

# Signing key
openssl rand -base64 32
# Result: e.g. 1234567890AbCdEfGhIjKlMnOpQrStUvWxYzAbCdEf=
```

---

## First Run

```bash
# Validate configuration
./target/release/ticket-pipeline validate

# Health check
./target/release/ticket-pipeline health

# One-time transfer
./target/release/ticket-pipeline

# Continuous mode (every 5 min)
./target/release/ticket-pipeline --continuous --interval 300
```

---

## Test Encryption

```bash
# Set key
export ENCRYPTION_KEY="your-base64-key-here"

# Encrypt data
ENCRYPTED=$(./target/release/ticket-pipeline encrypt \
  --data "Sensitive ticket information" \
  --key "$ENCRYPTION_KEY")

echo "Encrypted: $ENCRYPTED"

# Decrypt data
./target/release/ticket-pipeline decrypt \
  --data "$ENCRYPTED" \
  --key "$ENCRYPTION_KEY"
```

---

## Test Signatures

```bash
# Set key
export SIGNING_KEY="your-base64-signing-key"

# Sign data
SIGNATURE=$(./target/release/ticket-pipeline sign \
  --data "Important API request" \
  --key "$SIGNING_KEY")

echo "Signature: $SIGNATURE"

# Verify signature
./target/release/ticket-pipeline verify \
  --data "Important API request" \
  --signature "$SIGNATURE" \
  --key "$SIGNING_KEY"
```

---

## Secure Vault

```bash
# Set variables
export ENCRYPTION_KEY="your-base64-key"
export VAULT_PATH="/tmp/my_vault.json"
export VAULT_USER="admin"

# Store key
./target/release/ticket-pipeline vault store \
  --key-id "api-key-123" \
  --data "secret-api-key-value" \
  --name "Production API Key"

# List keys
./target/release/ticket-pipeline vault list

# Retrieve key
./target/release/ticket-pipeline vault retrieve \
  --key-id "api-key-123"

# Check integrity
./target/release/ticket-pipeline vault integrity
```

---

## Error Detection

```bash
# Analyze error
./target/release/ticket-pipeline errors analyze \
  --message "Request timeout after 30s" \
  --source "helpcenter_api"

# Statistics
./target/release/ticket-pipeline errors stats

# Detect anomalies
./target/release/ticket-pipeline errors anomalies

# List patterns
./target/release/ticket-pipeline errors test-patterns
```

---

## Docker

```bash
# Build
docker build -t ticket-pipeline .

# Run
docker run --env-file .env ticket-pipeline --continuous

# Run with vault
docker run \
  --env-file .env \
  -v /path/to/vault:/vault \
  -e VAULT_PATH=/vault/vault.json \
  ticket-pipeline --continuous
```

---

## Hub Manager (optional)

```bash
# Set GitHub token
export GITHUB_TOKEN="ghp_your_token_here"

# Update hub
./target/release/ticket-pipeline hub \
  --org my-org \
  --token "$GITHUB_TOKEN" \
  --dir github_hub \
  --dockerize

# Analyze dependencies
./target/release/ticket-pipeline deps \
  --path github_hub/RyzeSpace.Client \
  --repos "RyzeSpace.Client,RyzeSpace.HelpCenter,RyzeSpace.AdminPanel,RyzeSpace.Mobile,RyzeSpace.Desktop"
```

---

## Monitoring

### Prometheus Metrics

```bash
# Get metrics
./target/release/ticket-pipeline metrics

# Result:
# pipeline_tickets_fetched_total 150
# pipeline_tickets_transferred_total 145
# pipeline_tickets_failed_total 5
# pipeline_runs_total 10
# pipeline_transfer_duration_ms 2500
```

### Logs

```bash
# Debug mode
./target/release/ticket-pipeline --log-level debug

# JSON logs (for ELK/Loki)
./target/release/ticket-pipeline --log-level info
```

---

## Additional Resources

- **Full docs**: `README.md`
- **Security docs**: `SECURITY_DOCS.md`
- **Project summary**: `PROJECT_SUMMARY.md`

---

## Troubleshooting

### Problem: "ENCRYPTION_KEY not set"
```bash
export ENCRYPTION_KEY=$(openssl rand -base64 32)
```

### Problem: "Connection refused"
- Check URLs in .env
- Check firewall
- Check if API is accessible

### Problem: "Signature verification failed"
- Make sure you use the same key
- Check if data was modified

### Problem: "Vault permission denied"
- Check VAULT_USER
- Check ACL permissions

---

## Pre-Production Checklist

- [ ] All env variables configured
- [ ] Strong keys generated
- [ ] Encryption tested
- [ ] Signatures tested
- [ ] Vault tested
- [ ] Error detection tested
- [ ] Monitoring configured
- [ ] Logging configured
- [ ] Tested in Docker
- [ ] Reviewed SECURITY_DOCS.md

---

**Good luck!**
