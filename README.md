# RyzeHub

**Ticket transfer pipeline and platform layer for RyzeSpace, built on .NET 10.**

RyzeHub moves support tickets from the client dashboard into the help center, and exposes a
platform layer (realtime events, notifications, audit, RBAC, presence, sessions, gateway, cache,
telemetry, security) on top of it.

RyzeHub does **not** implement its own login engine. Identity, tokens, API keys and the audit
authority live in [`ryzespace/RyzeAuth`](https://github.com/ryzespace/RyzeAuth) — RyzeHub is a
resource server in front of it.

---

## Architecture

```text
Browser / mobile / desktop / service
          | Authorization Code + PKCE / client_credentials
          v
 Keycloak (ryzespace realm) <---- managed by ---- RyzeAuth
          | JWT (EdDSA / ES256 / RS256, JWKS + kid)
          v
   RyzeHub API (.NET 10) ---- gRPC ApiKeyIntrospection ----> RyzeAuth API
          |                ---- REST token introspection --->
          |                ---- REST audit forwarding ------>
          |
          +-- Ticket pipeline: Client Dashboard -> HelpCenter
          +-- Hub platform layer (17 modules, in-memory runtime)
          +-- Crypto: AES-256-GCM, HMAC-SHA256, hierarchical keys, secure vault
          +-- Error detection: pattern matching, Z-score / IQR / MA / exp. smoothing
          +-- OpenTelemetry: OTLP traces, Prometheus metrics
```

The solution follows Clean Architecture, matching the RyzeAuth conventions:
`Api -> Application -> Domain <- Infrastructure`, with `Contracts` holding the shared protobuf.

---

## Solution layout

```text
RyzeHub/
├── RyzeHub.sln
├── global.json                     # pinned .NET 10 SDK
├── Directory.Build.props           # net10.0, nullable, warnings-as-errors
├── Directory.Packages.props        # central package management
├── docker-compose.yml
├── src/
│   ├── RyzeHub.Domain/             # tickets, platform records, diagnostics, error types
│   ├── RyzeHub.Application/
│   │   ├── Abstractions/           # one interface group per file
│   │   ├── Configuration/          # options
│   │   ├── Diagnostics/            # error detection, anomaly detection
│   │   ├── Hub/                    # catalog, dependency graph, docker, github
│   │   ├── Pipeline/               # orchestrator, transfer, outcome, health, metrics
│   │   ├── Platform/Stores/        # one store per hub module
│   │   ├── Security/               # crypto, signatures, keys, vault, audit
│   │   └── Tickets/                # transformer, checksums
│   ├── RyzeHub.Contracts/          # api_keys.proto (mirrors RyzeAuth)
│   ├── RyzeHub.Infrastructure/
│   │   ├── Clients/                # source + destination HTTP clients
│   │   ├── DependencyInjection/    # focused registration extensions
│   │   └── RyzeAuth/               # control-plane client, tokens, role sync
│   ├── RyzeHub.Api/
│   │   ├── Configuration/          # auth, observability setup
│   │   ├── Endpoints/              # one file per route group
│   │   ├── Middleware/             # correlation id, security headers, problem details
│   │   └── Security/               # RyzeAuth API-key scheme
│   └── RyzeHub.Cli/Commands/       # one handler per verb
├── tests/
│   ├── RyzeHub.UnitTests/
│   └── RyzeHub.IntegrationTests/   # WebApplicationFactory API tests
├── scripts/install-workflows.sh    # one-shot workflow installer
└── docs/
    ├── ARCHITECTURE.md             # layering and responsibility split
    ├── RYZEAUTH-INTEGRATION.md     # control-plane contract and realm setup
    └── ci/                         # workflows pending installation, see docs/ci/README.md
```

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for how responsibilities are split.

---

## RyzeAuth integration

RyzeHub accepts two credential types, both validated against RyzeAuth.

### 1. User and service tokens (JWT)

Bearer tokens issued by the `ryzespace` Keycloak realm. RyzeHub validates issuer, audience,
lifetime and signature against the realm JWKS. On every successful validation
`RyzeAuthRoleSynchronizer` projects the token onto the hub RBAC model:

| RyzeAuth realm role | RyzeHub platform role |
| --- | --- |
| `ryzehub-user` | `User` |
| `ryzehub-seller` | `Seller` |
| `ryzehub-moderator` | `Moderator` |
| `ryzehub-support` | `Support` |
| `ryzehub-admin` | `Admin` |
| `ryzehub-superadmin` | `SuperAdmin` |

Scopes of the form `hub:<resource>:<action>` become granular hub permissions
(`hub:tickets:transfer` → `tickets:transfer`). Mappings are configurable via
`RyzeAuth:RoleMappings`.

### 2. Scoped API keys (gRPC introspection)

Machine-to-machine callers send `X-RyzeHub-Api-Key`. `RyzeAuthApiKeyHandler` calls the
`ryzeauth.v1.ApiKeyIntrospection/Introspect` gRPC method on RyzeAuth. RyzeHub never stores or
hashes API keys itself — RyzeAuth owns them. Positive results are cached briefly
(`IntrospectionCacheSeconds`) keyed by a SHA-256 digest, never by the raw key.

The resulting principal carries `organization_id`, `key_id` and the granted scopes, so tickets
are tagged with the owning organization and the rate limiter partitions per organization.

### Audit forwarding

Every RyzeHub audit entry is mirrored into the immutable RyzeAuth security audit trail via
`POST /internal/audit/events`, authenticated with a `client_credentials` service token. Forwarding
is fire-and-forget: a RyzeAuth outage never fails a pipeline run.

### Authorization policies

| Policy | Requirement |
| --- | --- |
| Default / fallback | Authenticated via JWT **or** RyzeAuth API key |
| `TicketTransfer` | Scope `hub:tickets:transfer`, or `Admin` / `SuperAdmin` role |
| `PlatformWrite` | Scope `hub:platform:write`, or `Admin` / `SuperAdmin` role |
| `PlatformAdmin` | Scope `hub:platform:admin`, or `SuperAdmin` role |

### Required RyzeAuth setup

In the `ryzespace` realm, provision:

1. A confidential client `ryzehub-service` with service accounts enabled (client credentials).
2. A client scope `ryzehub-api` and audience mapper so RyzeHub tokens carry `aud: ryzehub-api`.
3. Realm roles `ryzehub-user` … `ryzehub-superadmin`.
4. Client scopes `hub:tickets:transfer`, `hub:platform:write`, `hub:platform:admin`.
5. API keys created through RyzeAuth carrying the `hub:tickets:transfer` scope.

---

## Quick start

Requirements: the .NET SDK pinned in [`global.json`](global.json), plus Docker for the
containerised flow.

```bash
cp .env.example .env
openssl rand -base64 32   # -> ENCRYPTION_KEY
openssl rand -base64 32   # -> SIGNING_KEY
```

```bash
dotnet restore RyzeHub.sln
dotnet build RyzeHub.sln -c Release
dotnet test RyzeHub.sln -c Release
```

Run the API (expects RyzeAuth on `:8080`/`:8081`):

```bash
dotnet run --project src/RyzeHub.Api
# https://localhost:8082/scalar/v1  - API reference
# http://localhost:8082/health/live - liveness
# http://localhost:8082/metrics     - Prometheus
```

Or with Compose:

```bash
docker compose up -d --build
```

---

## CLI

```bash
dotnet run --project src/RyzeHub.Cli -- <command>
# or, after `dotnet publish`:
./ryzehub <command>
```

| Command | Description |
| --- | --- |
| `run [--continuous]` | Execute the ticket pipeline once or on a poll loop |
| `health` | Pipeline + RyzeAuth health check |
| `validate` | Validate configuration and print the effective settings |
| `hub [--dockerize]` | Clone the org, analyse dependencies, compute build order |
| `deps --path <p>` | Report internal dependencies of a repository |
| `docker --path <p>` | Detect the language and generate a Dockerfile |
| `encrypt / decrypt --data` | AES-256-GCM envelope operations |
| `sign / verify --data` | HMAC-SHA256 signatures |
| `vault store\|retrieve\|list\|integrity` | Secure vault operations |
| `errors analyze\|stats\|anomalies\|patterns\|predictions` | Error detection engine |
| `platform <action>` | Inspect and drive the hub platform modules |
| `auth health\|introspect-key\|introspect-token` | RyzeAuth control plane |

Examples:

```bash
ryzehub platform snapshot --seed-demo-user user-001
ryzehub auth introspect-key --api-key rk_live_... --scope hub:tickets:transfer
ryzehub errors analyze --message "401 Unauthorized" --source api_client
```

---

## HTTP API

| Route | Auth | Description |
| --- | --- | --- |
| `GET /health/live`, `/health/ready` | anonymous | Probes |
| `GET /metrics` | anonymous | Prometheus scrape |
| `GET /scalar/v1`, `/openapi/v1.json` | anonymous | API reference |
| `POST /api/pipeline/run` | `TicketTransfer` | Run the pipeline once (rate limited) |
| `GET /api/pipeline/health` | anonymous | Pipeline + upstream + RyzeAuth health |
| `GET /api/platform/*` | authenticated | Snapshot, events, notifications, audit, RBAC, … |
| `POST /api/platform/*` | `PlatformWrite` / `PlatformAdmin` | Mutations |
| `GET /api/diagnostics/*` | authenticated | Error detection and anomaly analysis |
| `GET/POST /api/hub/*` | `PlatformAdmin` | Repository catalog and hub updates |

---

## Hub platform modules

All 17 modules are active and exposed under `/api/platform`:

Real Time Event System · Notification Center · Audit Log Engine · Permission & Role Hub ·
Presence System · Device Management · Session Manager · API Gateway · Distributed Cache ·
Activity Feed · Internal Messaging · Feature Flags · Health Monitoring · Telemetry & Analytics ·
Security Center · Event Bus · File Transfer Service

---

## Security

- **AES-256-GCM** payload encryption with per-packet nonces, auth tags and SHA-256 checksums.
- **Key rotation** — old key ids stay decryptable after a rotation.
- **HMAC-SHA256** signatures for tickets and outbound requests, verified in constant time.
- **Hierarchical keys** — master → derived (per purpose) → 24h session keys.
- **Secure vault** — AES-GCM encrypted at rest with an ACL and an integrity hash.
- **No credential ownership** — passwords, MFA, sessions and API keys belong to RyzeAuth.
- Security headers, strict CORS, per-organization rate limiting and correlation ids on every request.

---

## CI/CD

| Workflow | Trigger | Purpose |
| --- | --- | --- |
| `ci.yml` | push / PR | `dotnet format`, build, unit + integration tests, CLI smoke, Docker build |
| `security.yml` | push / PR / weekly | Vulnerable-package gate, CodeQL, crypto verification, secret scan |
| `ryzeauth-integration.yml` | push / PR / nightly | Proto parity with RyzeAuth + live Compose ecosystem test |
| `hub-manager.yml` | nightly / manual | Org scan, dependency graph, build order, RyzeAuth dependency gate |
| `release.yml` | tags / manual | Multi-RID binaries, multi-arch GHCR images, gated deploy |

All workflows use `actions/checkout@v4` with NuGet caching, least-privilege `permissions`,
concurrency groups and job timeouts.

### Required secrets

| Secret | Used by | Purpose |
| --- | --- | --- |
| `RYZEAUTH_REPO_TOKEN` | `ryzeauth-integration` | Read access to `ryzespace/RyzeAuth` if private |
| `RYZEHUB_CLIENT_SECRET` | `ryzeauth-integration` | `ryzehub-service` client secret in CI |
| `HUB_GITHUB_TOKEN`, `HUB_ORG_NAME` | `hub-manager` | Organization scanning |
| `RYZEAUTH_AUTHORITY`, `RYZEAUTH_API_BASE_URL`, `RYZEAUTH_CLIENT_SECRET`, `ENCRYPTION_KEY` | `release` | Deployment gate |

---

## Configuration

Configuration binds from `appsettings.json`, environment variables and `.env`. The legacy flat
variables from the Rust version still work and are mapped onto the new sections — see
[`src/RyzeHub.Cli/EnvironmentConfiguration.cs`](src/RyzeHub.Cli/EnvironmentConfiguration.cs).

| Section | Purpose |
| --- | --- |
| `RyzeAuth` | Authority, audience, API base URL, client credentials, scopes, role mappings |
| `Pipeline` | Categorization, prioritization, deduplication, poll interval |
| `Source` / `Destination` | Upstream URLs, keys, timeouts, retries, rate limits, circuit breaker |
| `Security` | Encryption/signing keys, audit, checksums, vault |
| `Hub` | Platform retention, cache, gateway, telemetry |
| `HubManager` | GitHub organization, token, hub directory |

---

## Migration from the Rust implementation

| Rust | C# |
| --- | --- |
| `src/models.rs` | `RyzeHub.Domain/Tickets` |
| `src/config.rs` | `RyzeHub.Application/Configuration/PipelineOptions.cs` |
| `src/errors.rs` | `RyzeHub.Domain/Errors/PipelineException.cs` |
| `src/transformer.rs` | `RyzeHub.Application/Tickets/TicketTransformer.cs` |
| `src/source_client.rs`, `src/destination_client.rs` | `RyzeHub.Infrastructure/Clients` |
| `src/pipeline.rs` | `RyzeHub.Application/Pipeline/TicketPipeline.cs` |
| `src/security.rs`, `src/crypto/*` | `RyzeHub.Application/Security` |
| `src/error_detection/*` | `RyzeHub.Application/Diagnostics` |
| `src/hub_platform.rs` | `RyzeHub.Application/Platform` |
| `src/hub_catalog.rs`, `src/hub_manager.rs`, `src/dependency_manager.rs`, `src/docker_manager.rs`, `src/github_manager.rs` | `RyzeHub.Application/Hub` |
| `src/metrics.rs` (Prometheus crate) | `RyzeHub.Application/Pipeline/PipelineMetrics.cs` (OpenTelemetry) |
| `src/main.rs` (clap) | `RyzeHub.Cli` + `RyzeHub.Api` |

New in the C# version: first-class RyzeAuth integration, an HTTP API, OpenTelemetry tracing,
and per-organization ticket tagging.
