# Research: Service-to-Service mTLS Workload Identity (F191, #1420)

All decisions below were settled in the maintainer-approved brainstorm (2026-08-15) plus two
codebase exploration passes. No NEEDS CLARIFICATION items remain.

## D1 — Layer: app-level, not mesh-level

**Decision**: Implement workload identity at the application level (own CA, own cert wiring),
not via a service mesh.

**Rationale**: Istio/Linkerd require Kubernetes; Sorcha ships docker-compose. SPIFFE/SPIRE can
run on compose but adds two infra containers, weak workload attestation on plain Docker
(docker-label attestation is spoofable by anything reaching the socket), an immature .NET
workload-API story, and operational burden on every self-host operator — against the Public
Gates "one-liner install" goal. Federated nodes are separate installations (trust boundary is
the register), so there is no cross-node win to amortise mesh infrastructure against.

**Alternatives considered**: Istio/Linkerd (rejected: k8s-only); SPIFFE/SPIRE on compose
(rejected: oversized, weak attestation, operator burden). **Kept compatible**: workload identity
is expressed as a SPIFFE-style URI SAN (`spiffe://{installation}/service/{client_id}`) so a
future k8s managed install can swap the issuer for SPIRE without touching authorization.

## D2 — Shape: cert-bound token mint, not full-hop mTLS

**Decision**: The workload certificate replaces the shared secret **as the client credential at
the token mint only**. Everything downstream (service JWT, `RequireService`, tier audiences,
scopes) is unchanged.

**Rationale**: The entire s2s credential surface converges on two seams — client:
`ServiceAuthClient` (`src/Common/Sorcha.ServiceClients.Http/Auth/ServiceAuthClient.cs`), server:
`ServiceAuthEndpoints`/`ServiceAuthService` in Tenant. Cert-bound mint retires the shared secret
(the issue's goal) while concentrating cert-identity→scope mapping in the one place that already
owns principal→scope mapping. Full-hop mTLS would distribute authz mapping across 12 services,
touch every Kestrel config / HttpClient / gRPC channel / healthcheck, and is the "big-bang" the
issue rules out. This is the standard SPIFFE-style cert→token-exchange pattern.

**Alternatives considered**: full-hop mTLS everywhere (deferred to phase 2, separate issue —
also the vehicle for #1439's "backends refuse non-gateway traffic"); mTLS-only with no JWT
(rejected: destroys the scope model and delegation).

## D3 — Certificate lifecycle home: `sorcha workload-ca` CLI command group

**Decision**: A new CLI command group owns the full lifecycle: `init`, `status`, `renew`,
`rotate-ca` (maintainer explicitly asked the CLI to own expiry/update, settling the
setup-script-vs-CLI fork).

**Rationale**: One .NET implementation (BCL `CertificateRequest`, the same machinery F135's
`X509CertificateBuilder` proves out) serves issuance and lifecycle; no bash/openssl duplication;
works on Windows hosts. `sorcha-setup.sh` invokes the CLI rather than embedding cert logic
(FR-008).

**Facts**: `Sorcha.Cli` uses System.CommandLine 2.0.2 + Spectre.Console; it is published as a
NuGet tool only — **no Dockerfile / image exists** (`cli-publish.yml` packs to NuGet). Compose
hosts may lack the .NET SDK, so the installer needs a runnable form → this feature adds a small
CLI Dockerfile + docker-publish wiring (MUST carry the `ARG GITHUB_RUN_NUMBER`/`RUN_ATTEMPT`
block per CLAUDE.md §14) and `sorcha-setup.sh` prefers a locally-installed `sorcha` on PATH,
falling back to `docker run` of the CLI image.

## D4 — Issuance model: central issuance from the seeded-principal list (no CSR enrolment)

**Decision**: The CLI issues all certificates centrally from the known list of 8 service
principals (client_id + compose DNS hostname). No service↔Tenant CSR/enrolment protocol in v1.

**Rationale**: Matches the #1412 installer-generates-credentials model exactly and avoids the
chicken-and-egg of services needing credentials to enrol. F135's `InternalCaTrustProvider` is a
*tenant/org* CA (per-tenant roots, org DID SANs, DB-resident keys) — reusing it would couple
workload identity to Tenant DB state and to a different trust rail; the Workload CA is
deliberately a separate, filesystem-resident, per-installation CA.

**Alternatives considered**: Tenant-as-CA with bootstrap-secret enrolment (rejected for v1: more
machinery, live migration hazard; revisit if dynamic principals appear); reusing F135 CA
(rejected: different trust rail, DB-resident, per-tenant not per-installation).

## D5 — Chain validation: Kestrel-level chain check + handler-level identity match

**Decision**: On Tenant's dedicated mTLS listener, Kestrel requires a client certificate and the
TLS-layer validation callback builds an `X509Chain` with `CustomRootTrust` against the Workload
CA **bundle** (revocation `NoCheck` — private CA, documented). The mint handler then reads the
validated connection certificate, extracts the SPIFFE URI SAN, and requires exact (Ordinal)
match with the requested `client_id`'s expected identity. No ASP.NET authentication scheme is
added for this.

**Rationale**: The internal service-auth endpoints are credential-in-request today
(`AllowAnonymous`, secret verified in the service layer); keeping identity verification in the
same seam avoids auth-scheme interplay with the JWT bearer default and keeps one testable code
path. Expiry/not-yet-valid rejection falls out of chain building. The CA *bundle* (multi-root)
is what makes `rotate-ca` overlap work.

**Alternatives considered**: `AddCertificateAuthentication` middleware with `CustomTrustStore`
(functionally equivalent; rejected to avoid running a second auth scheme and policy surface for
two endpoints); accepting certs on the existing plaintext port via `AllowCertificate`
(rejected: mTLS must be required, not opportunistic, and the plaintext listener must stay
untouched for coexistence).

## D6 — Client-side loading: the `VerifierCertificate` precedent

**Decision**: `ServiceAuthClient` gains a cert mode configured by `ServiceAuth:ClientCertificate`
(PFX file path OR base64 blob — exactly the Haip `VerifierCertificate` loading pattern),
`ServiceAuth:ClientCertificatePassword`, `ServiceAuth:TrustBundle` (PEM bundle path), and
`ServiceAuth:MtlsTokenAddress` (default `https://tenant-service:8443`). When configured, token
requests use a `SocketsHttpHandler` presenting the client cert and validating the server
certificate against the same bundle (no system/public roots). When not configured, the secret
path is byte-for-byte untouched. **Fail-fast**: cert configured but unreadable → throw at
startup; never silently fall back to the secret (FR-009 — a fallback good enough to hide a dead
primary is the seam-bug incubator this repo keeps logging).

## D7 — Migration and retirement

**Decision**: Coexistence by default. Tenant config `ServiceAuth:DisableSharedSecrets`
(default `false`) refuses secret-presenting `client_credentials` (and secret-authenticated
delegation) with an explicit error when `true`, logged prominently at startup. This PR ships
compose wiring for certs on all 8 services while keeping the `${*_SERVICE_SECRET:?}` guards; the
per-deployment retire step (flip flag, drop secret wirings) happens only after live n1
verification, per the approved design and #1412's additive-migration requirement.

## D8 — Observability

**Decision**: A ServiceDefaults health check (`workload-certificate`) reports Healthy /
Degraded (inside warning window, default 30 days) / Unhealthy (expired), and not-applicable
(Healthy) when no cert is configured; a gauge on a `Sorcha.WorkloadIdentity` meter exposes
days-to-expiry per configured certificate. Complements CLI `status` (out-of-band) with in-band
warning.

## D9 — Shared implementation home: new leaf library `Sorcha.WorkloadIdentity`

**Decision**: New small library `src/Common/Sorcha.WorkloadIdentity` (BCL-only dependencies)
holding: SPIFFE id construction/parsing, CA + leaf issuance primitives, trust-bundle
load/validate, PFX/base64 certificate loading, expiry reporting, and the canonical config-key
constants. Consumers: Sorcha.Cli (lifecycle), Tenant (server validation), ServiceClients.Http
(client), ServiceDefaults (health).

**Rationale**: Four consumers across layers that must agree byte-for-byte on identity format and
validation semantics — exactly the "one home" pattern this repo enforces for derivation contexts
(§15) and validation codes (§16). A hand-mirrored SAN format across client/server/CLI would be
seam-bug #21 waiting to happen.

## D10 — Integration-test vehicle for the real mTLS join

**Decision**: The integration test must perform a **real TLS handshake on a real socket** into
the real mint pipeline: `WebApplicationFactory.UseKestrel` (available since .NET 8) with the
Testing environment (in-memory stores), a test-issued Workload CA + certs generated through the
same `Sorcha.WorkloadIdentity` issuance code, and an `HttpClient` presenting the client cert.
TestServer is explicitly ruled out (in-memory transport performs no TLS — it cannot exercise the
join). Mutation checks: wrong CA, expired leaf, SAN↔client_id mismatch, non-Active principal —
each must fail RED when the corresponding validation is weakened.

**Fallback** (if `UseKestrel` proves incompatible with Tenant's Program): a test host composed
from Tenant's own service-registration + endpoint-mapping extension methods on a hand-built
`WebApplication` with the same Kestrel mTLS configuration extension — still the production
validation code and mint handler, never a test auth handler.

## Known facts the implementation must respect (from exploration)

- `ServiceAuthClient` reads `ServiceAuth:ClientId/ClientSecret/Scopes`, POSTs to
  `/api/internal/service-auth/token`, caches per-process (singleton, 5-min refresh buffer);
  returns `null` on failure (callers proceed unauthenticated with a warning).
- Tenant internal group `/api/internal/service-auth` is `AllowAnonymous`, protected by gateway
  topology only; `/token/delegated` and `/rotate-secret` also verify client secrets — cert mode
  and `DisableSharedSecrets` must be applied consistently there (delegated: cert may replace the
  secret; rotate-secret is inherently secret-bound and becomes inert once secrets are disabled).
- Secret verification is Argon2id (48-byte format) via `VerifyClientSecret`; seeding via
  `ServicePrincipalSecretResolver` (#1412 fail-closed) — none of this changes.
- The 8 principals (client_id → compose DNS): service-blueprint→blueprint-service,
  service-wallet→wallet-service, register-service→register-service, service-peer→peer-service,
  validator-service→validator-service, tenant-service→tenant-service,
  service-haip→haip-service, service-verifier→sorcha-verifier. (Verify the verifier hostname at
  implementation time.)
- All services run `ASPNETCORE_URLS: http://+:8080`; Tenant's mTLS listener is **additive**
  (code-configured :8443), the plaintext listener untouched.
- Installation name: same source as JWT issuer/audiences (`JwtSettings:InstallationName`,
  default `sorcha`, via `SorchaAudiences`/`SorchaIssuer` conventions) — the SPIFFE trust domain
  MUST be derived from it, never a second config knob.
- P-256/ES256 is the established curve for the platform's X.509 rail (F135 builder is
  P-256-only); the Workload CA uses EC P-256 throughout.
- Per-service key isolation in compose: mount each service ONLY its own PFX + the public CA
  bundle — never the whole cert directory (mounting all keys everywhere would collapse
  per-service identity).
- `docker/certs` + `config/.env` are the precedent for gitignored per-deploy material; the
  workload-cert directory joins them.
