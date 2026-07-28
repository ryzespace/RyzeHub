# CI/CD workflows — manual installation required

The five workflow files in this directory are the rebuilt GitHub Actions pipelines for the .NET
solution. They are staged here because the automation account that produced this branch does not
hold the GitHub App `workflows` permission, so it cannot create or update files under
`.github/workflows/`.

## Installing them

From a checkout of this branch, with a token that can write workflows:

```bash
git checkout arena/019fa9ed-ryzehub

# Remove the obsolete Rust workflows
git rm -r --ignore-unmatch \
  .github/workflows/ci-pipeline.yml \
  .github/workflows/dependency-analysis.yml \
  .github/workflows/deploy-pipeline.yml \
  .github/workflows/docker-build.yml \
  .github/workflows/error-detection.yml \
  .github/workflows/hub-auto-update.yml \
  .github/workflows/hub-manager.yml \
  .github/workflows/security-encryption.yml

# Install the new ones
cp docs/ci/*.yml .github/workflows/
rm .github/workflows/README.md 2>/dev/null || true

git add .github/workflows
git commit -m "ci: rebuild workflows for the .NET solution"
git push
```

Once installed, this directory can be deleted.

## What changed and why

The previous workflows had several problems that the rewrite fixes.

| Problem in the Rust workflows | Fix |
| --- | --- |
| Checkout done with a hand-rolled `git clone` + `mv * ../` shell block, which silently loses dotfiles and breaks on any nested path | `actions/checkout@v4` |
| `"Cargo caching disabled due to organization policy"` — a no-op step, so every job recompiled from scratch | `actions/setup-dotnet@v4` with NuGet caching enabled |
| Steps ending in `\|\| true`, so audits, tests and license checks could never fail the build | Failures propagate; only genuinely advisory steps use `continue-on-error` |
| Artifacts written to a local `artifacts/` directory that nothing ever uploaded | `actions/upload-artifact@v4` |
| No `permissions:` block — jobs ran with the default broad token | Least-privilege `permissions:` on every workflow |
| No `concurrency:` — superseded pushes kept running | Concurrency groups with `cancel-in-progress` |
| No `timeout-minutes` — a hung job could burn six hours | Explicit timeouts on every job |
| Toolchain installed via `curl \| sh` and re-sourced in each step | Official setup action |

## The workflows

### `ci.yml` — push, PR

- `format` — `dotnet format --verify-no-changes`
- `build-test` — Release build, unit + integration tests, TRX and coverage artifacts
- `cli-smoke` — publishes the CLI and exercises it end to end: encrypt/decrypt round trip,
  sign/verify round trip, vault store/list/integrity, a platform snapshot asserting all 17 modules,
  and an error-detection assertion
- `docker` — builds both images with GHA layer caching, starts the API container and probes
  `/health/live` and `/api/platform/modules`

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
