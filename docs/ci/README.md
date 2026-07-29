# CI/CD workflows — manual installation required

The five workflow files in this directory are the rebuilt GitHub Actions pipelines for the .NET
solution. They are staged here because the automation account that produced this branch does not
hold the GitHub App `workflows` permission, so it cannot create or update files under
`.github/workflows/`.

## Installing them

From a checkout of this branch, using an account or token that can write workflows:

```bash
./scripts/install-workflows.sh --commit
```

Or manually:

```bash
cp docs/ci/*.yml .github/workflows/
git add .github/workflows
git commit -m "ci: install rebuilt workflows"
git push
```

Once installed, this directory and `scripts/install-workflows.sh` can be deleted.

## What changed and why

The previous workflows had several problems that the rewrite fixes.

| Problem | Fix |
| --- | --- |
| Checkout done with a hand-rolled `git clone` + `mv * ../` shell block, which silently loses dotfiles and breaks on any nested path | `actions/checkout@v4` |
| `"Cargo caching disabled due to organization policy"` — a no-op step, so every job recompiled from scratch | `actions/setup-dotnet@v4` with NuGet caching enabled |
| Steps ending in `\|\| true`, so audits, tests and license checks could never fail the build | Failures propagate; only genuinely advisory steps use `continue-on-error` |
| Artifacts written to a local `artifacts/` directory that nothing ever uploaded | `actions/upload-artifact@v4` |
| No `permissions:` block — jobs ran with the default broad token | Least-privilege `permissions:` on every workflow |
| No `concurrency:` — superseded pushes kept running | Concurrency groups with `cancel-in-progress` |
| No `timeout-minutes` — a hung job could burn six hours | Explicit timeouts on every job |
| Toolchain installed via `curl \| sh` and re-sourced in each step | Official setup action |
| `setup-dotnet` cache keyed on `**/packages.lock.json`, which this repo does not commit — the step fails outright | `actions/cache@v4` keyed on `*.csproj` + `Directory.Packages.props` |
| `HEALTHCHECK` ran `curl` in an image without curl, and `dotnet App.dll --healthcheck` would have started a second server | curl installed in the API image; the CLI image has no healthcheck (it is a task container) |
| `dotnet run --no-build` after a plain `dotnet build` cannot find the published output | `dotnet publish` then run the resulting `.dll` directly |
| Backgrounded API process was never stopped, so teardown could hang | PID captured and killed in an `if: always()` step |
| `jq index(...)` returns `0` for a first-position match, which is falsy in `jq -e` | Compare with `!= null` |

## The workflows

### `ci.yml` — push, PR

- `build-test` — Release build, `dotnet format` check (advisory), unit + integration tests, TRX artifacts
- `cli-smoke` — publishes the CLI and exercises it end to end: help/exit codes, encrypt/decrypt
  round trip, sign/verify including a negative tamper case, vault store/list/integrity plus a
  plaintext-leak check, a platform snapshot asserting all 17 modules, error-detection patterns,
  and a dependency scan asserting RyzeAuth is found
- `docker` — builds both images with GHA layer caching, starts the API container, probes
  `/health/live` and `/api/platform/modules`, and asserts a protected route returns 401

### `security.yml` — push, PR, weekly

- `dependency-audit` — fails on any vulnerable NuGet package, reports deprecated and outdated ones
- `codeql` — C# static analysis with results uploaded to code scanning
- `crypto-verification` — runs the crypto/vault/signature/key-manager test subset, rejects weak
  primitives (MD5, SHA1, DES, RC2), asserts the engine still enforces 256-bit keys, and scans for
  committed secrets

### `ryzeauth-integration.yml` — targeted push, PR, nightly

- `contract-parity` — diffs `api_keys.proto` against `ryzespace/RyzeAuth` and fails on drift
- `live-ecosystem` — brings up the real RyzeAuth Compose stack (Keycloak + Postgres + Redis +
  RyzeAuth API), starts RyzeHub against it, then asserts that RyzeHub reports RyzeAuth as reachable,
  that unauthenticated pipeline runs return 401, and that an invalid API key is rejected by
  introspection

### `hub-manager.yml` — nightly, manual

- Publishes the CLI, scans the organization, builds the dependency graph and computes a build order
- Writes a markdown job summary
- Fails if RyzeHub stops declaring its RyzeAuth dependency

### `release.yml` — tags, manual

- Verifies, publishes single-file binaries for `linux-x64`, `linux-arm64`, `win-x64`, `osx-arm64`
- Pushes multi-arch images to GHCR with semver tags
- Deploy job gated on an environment, verifying required secrets exist and that the RyzeAuth
  authority uses HTTPS

## Repository secrets

| Secret | Workflow | Purpose |
| --- | --- | --- |
| `RYZEAUTH_REPO_TOKEN` | `ryzeauth-integration` | Read `ryzespace/RyzeAuth` if it is private (falls back to `github.token`) |
| `RYZEHUB_CLIENT_SECRET` | `ryzeauth-integration` | `ryzehub-service` client secret used in CI |
| `HUB_GITHUB_TOKEN` | `hub-manager` | Organization repository scanning |
| `HUB_ORG_NAME` | `hub-manager` | Organization to scan (defaults to `ryzespace`) |
| `RYZEAUTH_AUTHORITY` | `release` | Production realm URL, must be HTTPS |
| `RYZEAUTH_API_BASE_URL` | `release` | Production RyzeAuth API URL |
| `RYZEAUTH_CLIENT_SECRET` | `release` | Production service client secret |
| `ENCRYPTION_KEY` | `release` | Base64 32-byte payload encryption key |
