# Architecture

How the solution is layered and why each piece is separate. The guiding rule is that a file owns
one responsibility, so a change has one obvious home and can be tested in isolation.

## Layers

```text
RyzeHub.Api  ──►  RyzeHub.Application  ──►  RyzeHub.Domain
RyzeHub.Cli  ──►          ▲                       ▲
                          └── RyzeHub.Infrastructure ──┘
                                     │
                              RyzeHub.Contracts (protobuf)
```

`Domain` has no dependencies. `Application` defines the interfaces and the business rules.
`Infrastructure` implements those interfaces against HTTP, gRPC and the file system.
`Api` and `Cli` are thin hosts.

## Hub platform

The platform layer is a facade over eight independent stores. `HubPlatform` holds no state of its
own: it delegates each module to its store and composes cross-module workflows.

| Store | Modules |
| --- | --- |
| `RealtimeEventStore` | Real Time Event System, Event Bus |
| `NotificationStore` | Notification Center |
| `AuditStore` | Audit Log Engine |
| `AccessControlStore` | Permission & Role Hub |
| `IdentityStateStore` | Presence, Device Management, Session Manager |
| `GatewayCacheStore` | API Gateway, Distributed Cache |
| `EngagementStore` | Activity Feed, Internal Messaging, File Transfer |
| `OperationsStore` | Feature Flags, Health Monitoring, Telemetry, Security Center |

`PlatformEventPublisher` owns the fan-out rule — record the event, notify each recipient, update
telemetry — so no store needs to know about the others. `PlatformDemoSeeder` holds the demo data
that used to be inlined into the platform.

`BoundedLog<T>` is the shared primitive behind every rolling history: append-only, size-capped and
thread-safe, with newest-first reads.

## Diagnostics

Anomaly detection is strategy-based. `AnomalyDetector` owns registration, sampling and alert
retention; each statistical test is its own `IAnomalyDetectionStrategy`:

| Strategy | Test |
| --- | --- |
| `ZScoreStrategy` | Distance from the mean in standard deviations |
| `IqrStrategy` | Outside Q1/Q3 widened by an IQR multiplier |
| `MovingAverageStrategy` | Relative deviation from a trailing mean |
| `ExponentialSmoothingStrategy` | Deviation from a recency-weighted baseline |

Strategies are stateless, so each is tested against a hand-built `TimeSeries` without a detector.
`ErrorDetectionEngine` keeps only pattern matching and correlation; its signatures live in
`DefaultErrorPatterns` and its statistical scan in `MetricAnomalyScanner`.

## Pipeline

`TicketPipeline` orchestrates the run and nothing else. The work it used to do inline now lives in:

| Type | Responsibility |
| --- | --- |
| `TicketTransferService` | Delivery with per-ticket exponential backoff |
| `TransferOutcomeHandler` | Post-transfer side effects: source update, audit, hub events, RyzeAuth forwarding |
| `PipelineHealthService` | Aggregating source, destination and RyzeAuth health |
| `TicketTransformer` | Validation, categorization, prioritization, tagging, filtering |

This means the retry policy can be tested without a hub, and health aggregation without a pipeline.

## Infrastructure clients

`HelpCenterClient` handles requests and responses only. Failure tracking is delegated to
`CircuitBreaker` (open/half-open/closed transitions) and payload shaping to
`TicketPayloadFactory`, so the breaker can be tested without HTTP.

`SecureVault` keeps access control and entry bookkeeping; `VaultCipher` owns the AES-GCM envelope
and `VaultFileStore` the JSON persistence.

`RyzeAuthClient` covers REST introspection, audit forwarding and health probes, while
`ApiKeyIntrospector` owns the gRPC path plus its digest-keyed positive-result cache.

## CLI

Each verb is an `ICommandHandler` with its own `Usage` and `Description`. `CommandRouter` only
dispatches and renders help, so help text cannot drift from the implemented commands. Handlers
throw `CommandUsageException` for bad input; the router turns that into exit code 2 plus the
relevant usage line. `ConsoleOutput` centralises JSON rendering.

## Composition root

`AddRyzeHub` is a sequence of focused registration extensions:

| Extension | Registers |
| --- | --- |
| `AddRyzeHubOptions` | Options binding for all seven sections |
| `AddRyzeHubCore` | Clock, metrics, diagnostics, transformer |
| `AddHubPlatform` | The eight stores, the publisher and the facade |
| `AddRyzeAuthIntegration` | Token provider, REST client, gRPC channel, role synchronizer |
| `AddRyzeHubSecurity` | Audit logger, and the crypto stack when a key is configured |
| `AddRyzeHubClients` | Source, destination and GitHub HTTP clients |
| `AddRyzeHubPipeline` | Transfer, health, outcome handler and the pipeline |

Services with optional constructor parameters (`IRyzeAuthClient`, `ITicketEncryptionManager`) are
registered with explicit factories using `GetService`, because the built-in container does not
honour C# default parameter values.

## API host

`Program.cs` is a startup sequence only. The detail sits in:

- `AuthenticationSetup` — JWT bearer, the API-key scheme and the authorization policies
- `ObservabilitySetup` — OpenTelemetry, CORS and rate limiting
- `ProblemDetailsHandler` — domain exception to RFC 7807 mapping
- `CorrelationIdMiddleware`, `SecurityHeadersMiddleware`
- `Endpoints/*` — one file per route group

## Optional dependencies

Two integrations degrade instead of failing:

- **No encryption key** — the crypto stack is not registered and the pipeline runs without payload
  encryption.
- **RyzeAuth unreachable** — audit forwarding is fire-and-forget and health reports `auth.status:
  "down"` rather than throwing.
