# RyzeAuth integration guide

How RyzeHub plugs into [`ryzespace/RyzeAuth`](https://github.com/ryzespace/RyzeAuth), and what has
to be provisioned on the RyzeAuth side before RyzeHub can run in a non-development environment.

## Responsibility split

| Concern | Owner |
| --- | --- |
| Credentials, password reset, MFA, passkeys, SSO | Keycloak, managed by RyzeAuth |
| OIDC sessions, global logout, device trust | RyzeAuth |
| Organizations, RBAC/ABAC records, API keys | RyzeAuth (PostgreSQL) |
| Immutable security audit trail | RyzeAuth |
| Ticket pipeline, hub platform modules, telemetry | RyzeHub |
| Hub RBAC projection, per-org ticket routing | RyzeHub (derived from RyzeAuth claims) |

RyzeHub is a **resource server**. It never stores credentials, never hashes API keys and never
issues tokens.

## Wire contracts

### gRPC — API key introspection

`src/RyzeHub.Contracts/Protos/api_keys.proto` is a byte-compatible mirror of
`src/RyzeAuth.Contracts/Protos/api_keys.proto`, generated as a **client** instead of a server.
The `ryzeauth-integration` workflow diffs the two files on every run and fails on drift.

```proto
service ApiKeyIntrospection {
  rpc Introspect (IntrospectApiKeyRequest) returns (IntrospectApiKeyReply);
}
```

The call is authenticated with a `client_credentials` bearer token, because RyzeAuth guards the
gRPC service with its `InternalService` policy (`azp` must match the internal client).

### REST — token introspection

`POST {RyzeAuth:ApiBaseUrl}/internal/tokens/introspect` with `{"token": "..."}`. Used by the
`ryzehub auth introspect-token` command and by any flow that needs to validate an opaque token
rather than a JWT.

### REST — audit forwarding

`POST {RyzeAuth:ApiBaseUrl}/internal/audit/events`:

```json
{
  "eventType": "ryzehub.ticket_transferred",
  "outcome": "success",
  "occurredAt": "2026-07-28T12:00:00.0000000+00:00",
  "subjectId": "…",
  "organizationId": "…",
  "correlationId": "T-1001",
  "metadata": { "ticket_id": "T-1001", "helpcenter_ticket_id": "HC-42" }
}
```

Forwarding is best-effort. If RyzeAuth is unreachable the event is logged locally and the pipeline
continues; failures are recorded through the error detection engine under the
`ryzeauth_denied` pattern.

> If this endpoint does not exist yet in RyzeAuth, set `RyzeAuth:ForwardAuditEvents=false`.
> RyzeHub keeps its own structured audit log and degrades gracefully.

## Required realm configuration

Apply to the `ryzespace` realm before deploying:

### 1. Service client

```text
Client ID:            ryzehub-service
Access type:          confidential
Service accounts:     enabled
Standard flow:        disabled
Direct access grants: disabled
```

Grant its service account the roles RyzeAuth requires for internal endpoints (the same set the
RyzeAuth `InternalService` policy expects, i.e. `azp = ryzeauth-internal` or an equivalent client
added to that policy).

### 2. Audience

Create client scope `ryzehub-api` with an **Audience** mapper adding `ryzehub-api`, and assign it
as a default scope to every client that should be able to call RyzeHub.

### 3. Realm roles

```text
ryzehub-user  ryzehub-seller  ryzehub-moderator
ryzehub-support  ryzehub-admin  ryzehub-superadmin
```

### 4. Hub scopes

```text
hub:tickets:transfer   hub:platform:write   hub:platform:admin
```

### 5. API keys

Create API keys through RyzeAuth with at least the `hub:tickets:transfer` scope. RyzeHub passes the
scope in `required_scope` and rejects any key RyzeAuth reports as inactive, revoked, expired or
under-scoped.

## Configuration reference

| Key | Environment variable | Default | Description |
| --- | --- | --- | --- |
| `RyzeAuth:Authority` | `RYZEAUTH_AUTHORITY` | `http://localhost:8080/realms/ryzespace` | Keycloak realm |
| `RyzeAuth:ValidAudience` | `RYZEAUTH_AUDIENCE` | `ryzehub-api` | Expected `aud` |
| `RyzeAuth:ApiBaseUrl` | `RYZEAUTH_API_BASE_URL` | `http://localhost:8081` | RyzeAuth API |
| `RyzeAuth:ClientId` | `RYZEAUTH_CLIENT_ID` | `ryzehub-service` | Service client |
| `RyzeAuth:ClientSecret` | `RYZEAUTH_CLIENT_SECRET` | — | Service client secret |
| `RyzeAuth:RequiredApiKeyScope` | `RYZEAUTH_REQUIRED_SCOPE` | `hub:tickets:transfer` | Scope gate |
| `RyzeAuth:ApiKeyIntrospectionEnabled` | `RYZEAUTH_API_KEY_INTROSPECTION` | `true` | Enable gRPC introspection |
| `RyzeAuth:ForwardAuditEvents` | `RYZEAUTH_FORWARD_AUDIT` | `true` | Mirror audit into RyzeAuth |
| `RyzeAuth:IntrospectionCacheSeconds` | — | `60` | Positive-result cache TTL |
| `RyzeAuth:RequireHttpsMetadata` | — | `true` | Must stay `true` in production |
| `RyzeAuth:RoleMappings` | — | see README | Realm role → hub role |

## Verifying the integration

```bash
# Is RyzeAuth reachable?
ryzehub auth health

# Does RyzeAuth accept this API key for the transfer scope?
ryzehub auth introspect-key --api-key rk_live_... --scope hub:tickets:transfer

# End-to-end health, including the auth block
curl -s http://localhost:8082/api/pipeline/health | jq '.auth'
```

The `ryzeauth-integration` workflow performs the same checks against a live RyzeAuth Compose stack
on every relevant push and nightly.

## Production checklist

- [ ] `RyzeAuth:Authority` and `RyzeAuth:ApiBaseUrl` use HTTPS (enforced by the release workflow).
- [ ] `RyzeAuth:RequireHttpsMetadata` is `true`.
- [ ] `ryzehub-service` client secret is supplied from a secret store, never from `appsettings.json`.
- [ ] `Security:EncryptionKey` and `Security:SigningKey` are distinct 32-byte base64 values.
- [ ] Realm roles, client scopes and the audience mapper are provisioned.
- [ ] RyzeAuth's own hardening checklist (`docs/KEYCLOAK-HARDENING.md`) is complete.
- [ ] `Cors:AllowedOrigins` lists only the real front-end origins.
