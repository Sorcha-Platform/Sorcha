# Implementation Plan: Service-to-Service mTLS Workload Identity

**Branch**: `191-mtls-workload-identity` | **Date**: 2026-08-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/191-mtls-workload-identity/spec.md` (issue #1420)

## Summary

Retire the shared OAuth2 service secret by making a per-installation X.509 workload certificate
the service's client credential at the token mint. A new leaf library
(`Sorcha.WorkloadIdentity`) provides SPIFFE-style identity, CA/leaf issuance, trust-bundle
validation, and cert loading; a new `sorcha workload-ca` CLI command group owns the certificate
lifecycle (init/status/renew/rotate-ca); Tenant gains a dedicated mTLS listener whose validated
client certificate substitutes for `client_secret` in the `client_credentials` grant;
`ServiceAuthClient` gains a cert mode; both credential paths coexist until
`ServiceAuth:DisableSharedSecrets` is flipped per deployment after live verification. Everything
downstream of the mint (service JWT shape, tier audiences, scopes, `RequireService`) is
unchanged. See `research.md` for the decision record (D1–D10).

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: BCL only for crypto (`System.Security.Cryptography.X509Certificates.CertificateRequest`, `X509Chain` CustomRootTrust, `SocketsHttpHandler.SslOptions`, Kestrel client-cert options); System.CommandLine 2.0.2 + Spectre.Console (existing CLI stack); no new NuGet packages.

**Storage**: Filesystem only — per-deploy certificate directory (`config/workload-certs/`, gitignored). **No database or EF-migration changes**: `ServicePrincipal` is unchanged.

**Testing**: xUnit v3 + FluentAssertions + Moq (repo standard). Real-socket mTLS integration tests via `WebApplicationFactory.UseKestrel` (fallback documented in research D10). Mutation-checked guards per repo verification discipline.

**Target Platform**: Linux containers (docker-compose primary), Windows/macOS dev hosts (Aspire), CLI additionally as NuGet tool + new container image.

**Project Type**: Multi-service platform + CLI — additive changes to existing projects plus one new leaf library.

**Performance Goals**: Negligible — token mint happens once per ~8h per service (5-min refresh buffer); one extra TLS handshake per mint.

**Constraints**: Secret path byte-for-byte unchanged when no cert configured (FR-005/SC-004); fail-fast on configured-but-unreadable cert (FR-009, never fall back); dev/Aspire zero-setup unchanged (FR-011); no secrets/keys committed or logged (FR-012); coexistence default, retirement only per-deployment post-live-verify (D7).

**Scale/Scope**: 8 service principals, 1 installation-scoped CA, ~6 projects touched + 1 new library + 1 new test project.

## Constitution Check

*GATE: evaluated pre-Phase 0 and re-checked post-design — PASS, no violations to justify.*

- **I. Microservices-First**: PASS — new library is a downward-only leaf (BCL deps); no service gains an upward dependency; Tenant listener is additive.
- **II. Security First**: PASS — this feature *is* zero-trust hardening; no secrets committed (cert dir gitignored, joins `.env`/`docker/certs` precedent); private keys never logged; input validation on endpoints unchanged; fail-closed postures mirrored from #1409/#1412.
- **III. API Documentation**: PASS — the one endpoint whose contract widens (`client_secret` optional under mTLS) gets its OpenAPI description updated; CLI gets `--help` surfaces; docs-sync obligations listed below.
- **IV. Testing**: PASS — TDD tasks; unit coverage on the new library; real-handshake integration tests (SC-006); mutation checks specified.
- **V. Code Quality**: PASS — nullable enabled, async I/O, DI, license headers.
- **VII. DDD language**: n/a-clean — reuses existing terms (service principal, service token, installation).
- **VIII. Observability**: PASS — new health check + meter (`Sorcha.WorkloadIdentity`), structured logging for every refusal class (SC-002).

## Project Structure

### Documentation (this feature)

```text
specs/191-mtls-workload-identity/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D10 + exploration facts
├── data-model.md        # Phase 1 — identities, material layout, config keys
├── quickstart.md        # Phase 1 — operator flow (init → deploy → verify → retire)
├── contracts/
│   ├── service-auth-mtls.md   # Token-mint contract deltas + refusal matrix
│   ├── workload-ca-cli.md     # CLI command group contract (args, outputs, exit codes)
│   └── config-keys.md         # Canonical config keys + env vars + compose wiring
└── tasks.md             # Phase 2 (/speckit.tasks — not created by /speckit.plan)
```

### Source Code (repository root)

```text
src/Common/Sorcha.WorkloadIdentity/          # NEW leaf library (BCL-only)
├── SpiffeId.cs                              # build/parse/validate spiffe://{installation}/service/{client_id}
├── WorkloadCertificateAuthority.cs          # CA + leaf + server-cert issuance (CertificateRequest, P-256)
├── WorkloadTrustBundle.cs                   # PEM bundle load; X509Chain CustomRootTrust validation (NoCheck revocation)
├── WorkloadCertificateLoader.cs             # PFX path-or-base64 loading (VerifierCertificate pattern), fail-fast
├── WorkloadCertificateInventory.cs          # expiry inspection shared by CLI status/renew + health check
└── WorkloadIdentityConfig.cs                # canonical config-key constants + option binding

src/Apps/Sorcha.Cli/
├── Commands/WorkloadCa/                     # NEW: init / status / renew / rotate-ca (+ rotate-ca --complete)
└── Dockerfile                               # NEW: CLI image (MUST carry ARG GITHUB_RUN_NUMBER/RUN_ATTEMPT block, §14)

src/Services/Sorcha.Tenant.Service/
├── Program.cs                               # additive Kestrel mTLS listener (:8443) when configured
├── Extensions/                              # mTLS listener + validation wiring extension
├── Services/ServiceAuthService.cs           # cert-credential path + DisableSharedSecrets enforcement
└── Endpoints/ServiceAuthEndpoints.cs        # client_secret optional under validated cert; delegated-path parity

src/Common/Sorcha.ServiceClients.Http/
└── Auth/ServiceAuthClient.cs                # cert mode (SocketsHttpHandler + client cert + bundle-pinned server validation)

src/Common/Sorcha.ServiceDefaults/
└── (health)                                 # workload-certificate health check + days-to-expiry gauge

scripts/sorcha-setup.sh                      # invoke CLI (PATH → docker run fallback) to provision certs; .env additions
docker-compose.yml                           # per-service PFX + bundle mounts (own key ONLY), cert env, tenant :8443
.github/workflows/docker-publish.yml         # CLI image publish entry
.gitignore                                   # config/workload-certs/

tests/Sorcha.WorkloadIdentity.Tests/         # NEW: unit tests for the library
tests/Sorcha.Cli.Tests/                      # workload-ca command tests (temp-dir lifecycle round-trips)
tests/Sorcha.Tenant.Service.Tests/           # real-Kestrel mTLS integration tests + refusal matrix + DisableSharedSecrets
tests/Sorcha.ServiceClients.Tests/ (or .Http tests project)  # ServiceAuthClient cert-mode + fail-fast + legacy-unchanged
```

**Structure Decision**: One new leaf library because four consumers (CLI, Tenant,
ServiceClients.Http, ServiceDefaults) must agree byte-for-byte on identity format and validation
semantics — the repo's established "exactly one home" pattern (CLAUDE.md §15/§16). Everything
else is additive edits inside existing projects at the two seams the exploration identified.

## Design Detail (implementation-facing)

### Identity

- SPIFFE trust domain = installation name from the same source as JWT issuer/audiences
  (`JwtSettings:InstallationName`, default `sorcha`) — never a second knob.
- Workload id: `spiffe://{installation}/service/{client_id}` carried as the leaf's URI SAN;
  leaf also carries DNS SAN of the service's compose hostname. Matching is exact Ordinal on the
  full URI built from (installation, requested client_id).

### Server (Tenant)

- Additive Kestrel listener (default port 8443, key `ServiceAuth:Mtls:Port`) configured only
  when `ServiceAuth:Mtls:ServerCertificate` + `ServiceAuth:Mtls:TrustBundle` are set;
  `ClientCertificateMode.RequireCertificate`; TLS validation callback = chain build against the
  bundle (CustomRootTrust, `X509RevocationMode.NoCheck`), rejecting untrusted/expired/not-yet-valid.
- Mint flow when `client_secret` absent: connection must carry a validated client cert; extract
  SPIFFE SAN → must equal expected id for requested `client_id`; principal must exist + Active;
  scope intersection and token generation identical to the secret path. Distinguishable log line
  per refusal class (wrong-identity logs both values).
- `ServiceAuth:DisableSharedSecrets=true`: any secret-presenting request on token/delegated is
  refused with an explicit "shared secrets disabled" error; prominent startup log. Rotate-secret
  becomes inert by construction (it authenticates with the secret) — documented.
- Delegated tokens: `/token/delegated` gains the same cert-credential option; behaviour parity
  with the secret path otherwise.

### Client (`ServiceAuthClient`)

- Cert mode active iff `ServiceAuth:ClientCertificate` set: load PFX (path|base64 + password),
  fail-fast if unreadable; token endpoint becomes `ServiceAuth:MtlsTokenAddress`
  (default `https://tenant-service:8443`); `SocketsHttpHandler.SslOptions` presents the client
  cert and validates the server chain **only** against `ServiceAuth:TrustBundle`.
- No cert configured: existing code path untouched (assert by leaving existing tests green and
  by a no-behaviour-delta test).
- One startup log line when both cert and secret are configured (cert wins for token
  acquisition; secret ignored).

### CLI (`sorcha workload-ca …`)

- `init`: CA (EC P-256, ~5y) + 8 leaves (~2y) + tenant server cert; PFX per service, PEM bundle;
  idempotent (valid material → no-op, reported). Installation name and service list (client_id →
  DNS host) parameterised with the 8 seeded principals as default.
- `status`: table (subject, spiffe id, notAfter, days left); exit 0 healthy / 2 threshold
  (default 30d) / 1 error.
- `renew`: re-issue leaves inside threshold (`--all` override) under current CA; prints the
  "recreate containers to pick up" instruction.
- `rotate-ca`: new CA; bundle = [new, old]; re-issue all leaves + server cert under new CA.
  `rotate-ca --complete`: drop old root from bundle. Two-step with container recreates between.
- Password for PFX material via option/env (`WORKLOAD_CERT_PASSWORD`).

### Delivery (compose + installer)

- `sorcha-setup.sh`: generate `WORKLOAD_CERT_PASSWORD` into `.env`; provision via `sorcha` on
  PATH if present, else `docker run --rm -v <certdir>:/certs <cli-image> workload-ca init …`.
- Compose per service: mount **only** that service's PFX + the public bundle (read-only); env
  `ServiceAuth__ClientCertificate=/workload/client.pfx`, password + bundle keys; Tenant
  additionally mounts the server PFX and exposes :8443 internally (no host publish).
- Secrets stay wired (`${*_SERVICE_SECRET:?}` untouched) — coexistence; retire step is a
  documented operator action, not part of this PR's default posture.
- New CLI image published by docker-publish with the §14 version-args block (enforced by
  `check-dockerfile-version-args.ps1`).

### Observability

- Health check `workload-certificate` (Healthy / Degraded <30d / Unhealthy expired /
  Healthy-not-applicable when unconfigured) registered via ServiceDefaults.
- Gauge `sorcha_workload_cert_days_to_expiry{subject}` on meter `Sorcha.WorkloadIdentity`.

## Verification strategy (maps to SC-006/SC-007)

1. **Unit**: SpiffeId round-trip/rejection; issuance (SAN contents, lifetimes, P-256); bundle
   validation truth table; loader fail-fast; inventory thresholds; CLI lifecycle in temp dirs
   (init idempotence, renew threshold, rotate overlap then complete).
2. **Integration (the join)**: real Kestrel socket + real handshake + real mint handler
   (research D10). Refusal matrix: wrong CA / expired leaf / SAN mismatch / suspended principal /
   unknown principal. DisableSharedSecrets matrix. Legacy path unchanged.
3. **Mutation checks**: weaken each validation (accept any root; skip expiry; compare SAN
   case-insensitively to a wrong id; ignore status) → named test goes RED → restore.
4. **Live n1 (part of the work, not optional)**: deploy branch images; provision certs; one
   service minting via cert with its secret env removed; AIAS golden-path rehearse green; flip
   `DisableSharedSecrets` on n1; secret mint refused; golden path still green. (SC-007 —
   nineteen logged seam bugs say the live run is where this class of defect dies.)

## Docs-sync obligations (per CLAUDE.md)

- Tenant Service README (new listener, config keys, DisableSharedSecrets)
- `src/Apps/Sorcha.Cli/README.md` (workload-ca group)
- `docs/reference/API-DOCUMENTATION.md` (mint contract delta)
- `docs/getting-started/PORT-CONFIGURATION.md` (tenant 8443 internal)
- `docs/guides/AUTHENTICATION-SETUP.md` (cert-mode s2s auth + retirement runbook)
- CLAUDE.md pattern note if reviewers deem it a standing pattern (candidate: "workload identity
  has exactly one home")
- `.specify/MASTER-TASKS.md` status

## Complexity Tracking

No constitution violations; table intentionally empty.

## Risks

- `WebApplicationFactory.UseKestrel` + Tenant Program compatibility → fallback host documented
  (D10); either way production validation code is exercised, no test auth handler.
- Verifier compose hostname (`sorcha-verifier`) to be confirmed at implementation.
- CLI image is new distribution surface — version-args gate + smoke `--help` in CI cover it.
- Coexistence means no behaviour change by default; the risky step (retirement) is explicitly
  deferred to a live-verified operator action.
