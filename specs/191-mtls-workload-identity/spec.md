# Feature Specification: Service-to-Service mTLS Workload Identity

**Feature Branch**: `191-mtls-workload-identity`

**Created**: 2026-08-15

**Status**: Draft

**Issue**: #1420 — Service-to-service auth: move to mTLS / workload identity (retire shared service secrets)

**Input**: User description: "Retire the shared OAuth2 service secret by making a per-installation X.509 workload certificate the service's client credential at the token mint. Approach approved by maintainer: app-level cert-bound token mint (NOT mesh-level, NOT full-hop mTLS — those are phase 2 / out of scope). CLI-owned certificate lifecycle (init / status / renew / rotate-ca). Both credential paths coexist during migration; shared secrets are retired per-deployment only after live verification."

## Context

Today every Sorcha service proves its identity to the platform with a shared symmetric secret (`ServiceAuth:ClientSecret`), presented to the internal token-mint endpoint in exchange for a short-lived service token. Issue #1412 made those secrets per-deployment and fail-closed, but a shared secret still has to be stored in cleartext on both sides and transits the internal network on every token refresh. This feature replaces the *credential* — the service presents a per-installation X.509 workload certificate over mutual TLS instead of a secret — while leaving everything downstream of the mint (service tokens, tier audiences, scopes, authorization policies) unchanged. Certificate identity is expressed as a SPIFFE-compatible identifier so a future orchestrated deployment can swap the certificate issuer for a mesh (e.g. SPIRE) without touching authorization.

Out of scope (deliberately, per the approved design): full-hop mTLS between services; gRPC transport authentication (Peer's inbound enforcement gap is tracked separately); Feature 175 node-to-node peer TLS (a different, register-anchored trust rail); #1380 org-key custody.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A service authenticates with a certificate instead of a secret (Priority: P1)

A Sorcha backend service configured with a workload certificate (and no shared secret at all) requests a service token from the platform's identity service over mutual TLS. The identity service verifies the presented certificate chains to this installation's Workload CA, matches the certificate's workload identifier to the requested client identity, and mints exactly the service token it would have minted for a valid secret. Every downstream call the service makes with that token behaves identically to today.

**Why this priority**: This is the feature — the shared secret ceases to be required for service authentication. Without it nothing else in this spec has value.

**Independent Test**: Stand up the identity service with a Workload CA trust bundle and one enrolled service principal; using only that principal's workload certificate (no secret configured anywhere), acquire a service token and use it against a `RequireService`-protected endpoint.

**Acceptance Scenarios**:

1. **Given** a service principal exists and is Active, **When** the matching workload certificate is presented over mutual TLS with a `client_credentials` request naming that client id and no client secret, **Then** a service token is issued with the same claims shape, audience, scopes, and lifetime as the secret path produces.
2. **Given** a certificate signed by a different (unknown) CA, **When** it is presented at the mutual-TLS token endpoint, **Then** the connection/request is refused and no token is issued.
3. **Given** an expired workload certificate signed by the correct CA, **When** it is presented, **Then** the request is refused and no token is issued.
4. **Given** a valid workload certificate for service A, **When** the token request names service B's client id, **Then** the request is refused (identity/client-id mismatch) and the refusal is logged with both identities.
5. **Given** a valid certificate whose matching service principal is Suspended or Revoked, **When** a token is requested, **Then** the request is refused.
6. **Given** a valid certificate and a scope request outside the principal's allowed scopes, **When** the token is requested, **Then** scope handling behaves exactly as the secret path does today (intersection; empty intersection refused).
7. **Given** a service with no certificate configured, **When** it authenticates with its shared secret, **Then** behaviour is byte-for-byte unchanged from today (legacy path untouched).

---

### User Story 2 - An operator provisions and manages workload certificates from the CLI (Priority: P2)

An operator installing or maintaining a Sorcha node uses a single CLI command group to create the installation's Workload CA and per-service certificates, see their expiry state, renew leaves, and rotate the CA itself with an overlap window — without ever hand-running a certificate toolchain. The standard installer invokes the same commands, so a fresh install gets certificates without any extra operator action.

**Why this priority**: Certificate lifecycle is the real work of workload identity. Without owned issue/renew/rotate, certificates become a worse-managed secret.

**Independent Test**: On a clean directory, run `init`, verify CA + per-service certificate material exists and re-running is a no-op; run `status` and verify the report and exit code; force a short-lived leaf and verify `renew` re-issues it; run `rotate-ca` and verify old-CA leaves still validate against the bundle during overlap and the new CA signs replacements.

**Acceptance Scenarios**:

1. **Given** an empty certificate directory, **When** `init` runs, **Then** a per-installation Workload CA and one certificate per service principal are created, each leaf carrying a SPIFFE-style workload URI identifier (`spiffe://{installation}/service/{client_id}`) and the service's internal DNS name, plus a server certificate for the identity service's mutual-TLS listener.
2. **Given** valid existing material, **When** `init` runs again, **Then** nothing is re-issued (idempotent) and the command says so.
3. **Given** a mix of healthy and near-expiry certificates, **When** `status` runs, **Then** it reports each certificate's subject, identity, and days to expiry, and exits non-zero if any is inside the renewal threshold.
4. **Given** certificates inside the renewal threshold, **When** `renew` runs, **Then** only those are re-issued under the existing CA (with `--all` forcing all leaves), and the output tells the operator that services pick up new certificates on container recreate.
5. **Given** an existing CA, **When** `rotate-ca` runs, **Then** a new CA is created, the trust bundle contains both old and new roots (overlap), all leaves are re-issued under the new root, and a completion step drops the old root from the bundle.

---

### User Story 3 - An operator retires shared secrets for a deployment (Priority: P3)

Once a deployment is verified minting via certificates, the operator flips a single configuration switch on the identity service, after which secret-based service authentication is refused platform-wide while certificate-based authentication continues, and the per-service secret wiring can be removed from the deployment configuration.

**Why this priority**: This is the "retire" step the issue asks for, but it is deliberately last — it must only happen after live verification, and the platform must be fully functional without it (coexistence is the shipped default).

**Independent Test**: With both paths working, set the disable switch; verify a secret-based `client_credentials` request is refused with a clear error while the certificate path still mints.

**Acceptance Scenarios**:

1. **Given** the disable switch is off (default), **When** services authenticate by secret or by certificate, **Then** both succeed (coexistence).
2. **Given** the disable switch is on, **When** a `client_credentials` request presents a client secret, **Then** it is refused with an explicit "shared secrets are disabled" error, and certificate-based requests are unaffected.
3. **Given** the disable switch is on, **When** the identity service starts, **Then** it logs prominently that secret-based service auth is disabled, so a mis-flipped deployment is diagnosable from startup logs.

---

### User Story 4 - The platform warns before certificates expire (Priority: P3)

Each service exposes its workload-certificate expiry through the standard health and metrics surfaces, so an operator sees "certificate expiring soon" in dashboards and health checks long before authentication starts failing.

**Why this priority**: An expired workload certificate fails exactly like the seam bugs this platform keeps finding — far from the cause. Early warning converts an outage into a maintenance task.

**Independent Test**: Configure a service with a certificate expiring inside the warning window; verify the health check reports Degraded with the expiry date and the metric exposes days-to-expiry.

**Acceptance Scenarios**:

1. **Given** a configured certificate with more than the warning window remaining, **When** health is checked, **Then** the certificate check is Healthy.
2. **Given** a certificate inside the warning window, **When** health is checked, **Then** the check is Degraded and names the certificate and expiry date.
3. **Given** a certificate past expiry, **When** health is checked, **Then** the check is Unhealthy.
4. **Given** no certificate configured (legacy secret mode), **When** health is checked, **Then** the certificate check reports not-applicable/Healthy rather than failing dev deployments.

---

### Edge Cases

- Certificate configured but the file is missing/unreadable at startup → the service must fail loudly at startup (fail-fast), not silently fall back to the secret path; a fallback would mask a broken cert deployment forever.
- Both certificate and secret configured → certificate path is used; the secret is ignored for token acquisition (and this is logged once at startup).
- Disable switch on while some service still has only a secret → that service's token requests fail with a clear error; the platform must not be flipped until all services are on certificates (operator runbook step).
- CA rotation overlap: during overlap, leaves from both roots must validate; after the old root is dropped, old-root leaves must be refused.
- The identity service's own outbound service-auth client (Tenant calls other services too) must work in certificate mode against its own listener.
- Installation name changes would change every workload identifier — the identifier is derived from the same installation name as the JWT issuer/audiences, and a mismatch refuses cleanly.
- Clock skew: standard certificate validity tolerances apply; token lifetimes are unchanged.
- A certificate for a client id that has no seeded service principal → refused (principal must exist and be Active); provisioning order (seed before first mint) is unchanged from the secret model.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST accept an X.509 workload certificate, presented over mutual TLS to a dedicated identity-service listener, as a complete client credential for the service `client_credentials` grant — no shared secret required.
- **FR-002**: Certificate validation MUST chain to the installation's Workload CA trust bundle (and only that bundle — no system/public roots), MUST reject expired or not-yet-valid certificates, and MUST NOT require online revocation checking (private CA, short-ish lifetimes; documented trade-off).
- **FR-003**: The certificate's workload identifier (SPIFFE-style URI `spiffe://{installation}/service/{client_id}`) MUST exactly match the client id named in the token request, where `{installation}` is the same installation name that namespaces JWT issuer and audiences; any mismatch (either half) is refused and logged with both values.
- **FR-004**: A token minted via certificate MUST be indistinguishable in shape from one minted via secret: same claims, same scopes model (intersection with the principal's allowed scopes), same audience tier, same lifetime. Downstream authorization requires zero changes.
- **FR-005**: The legacy secret path MUST remain byte-for-byte unchanged when no certificate is configured, and MUST remain available by default (coexistence) until explicitly disabled per deployment.
- **FR-006**: A single identity-service configuration switch MUST disable secret-based service authentication platform-wide; when on, secret-based requests are refused with an explicit error and the condition is visible in startup logs.
- **FR-007**: A CLI command group MUST own the certificate lifecycle: `init` (create CA + per-service leaves + identity-service server certificate; idempotent), `status` (expiry report; non-zero exit inside renewal threshold), `renew` (threshold-based leaf re-issue under the current CA; `--all` override), `rotate-ca` (new CA with old+new trust-bundle overlap, leaf re-issue, explicit old-root drop).
- **FR-008**: The standard installer MUST provision workload certificates by invoking the CLI (no duplicated certificate logic in shell), and the standard container deployment MUST deliver each service its own certificate material and the shared trust bundle read-only.
- **FR-009**: Services with a certificate configured MUST fail fast at startup if the material is missing or unreadable — never silently fall back to secrets.
- **FR-010**: Every service MUST surface workload-certificate expiry via the standard health-check and metrics surfaces, with Degraded inside a warning window and Unhealthy at expiry; absence of a certificate (legacy mode) is not a failure.
- **FR-011**: Development and test environments MUST keep working with zero certificate setup (secret literals as today); certificate mode is opt-in per deployment via configuration.
- **FR-012**: No private key material may ever be logged, and secrets/keys MUST NOT be committed — the certificate directory joins the ignored/generated per-deploy material exactly as `.env` does.

### Key Entities

- **Workload CA**: A per-installation private certificate authority (EC P-256), created and rotated only by the CLI; its private key never leaves the host's certificate directory. Trust is expressed as a **bundle** (one or more roots) to support rotation overlap.
- **Workload certificate**: A per-service-principal leaf certificate carrying the SPIFFE-style workload URI and the service's internal DNS name; the private key is the service's credential and never transits the network.
- **Identity-service server certificate**: A CA-issued server certificate for the mutual-TLS listener, so clients authenticate the mint endpoint against the same bundle (no public PKI dependency inside the deployment).
- **Service principal**: Existing entity (client id, scopes, status) — unchanged; the certificate maps onto it, it does not replace it.
- **Service token**: Existing short-lived JWT — unchanged in every respect.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A service configured with only a workload certificate (no secret present in its environment) acquires a valid service token and successfully calls a service-protected endpoint.
- **SC-002**: Each invalid-credential case — wrong CA, expired leaf, identity/client-id mismatch, non-Active principal — is refused (no token issued) and produces a distinguishable log line.
- **SC-003**: With secrets disabled on the identity service, 100% of secret-based service auth attempts are refused while certificate-based authentication continues uninterrupted.
- **SC-004**: With no certificate configured, existing deployments and the development experience are unchanged — zero configuration or behaviour delta on the secret path.
- **SC-005**: The full certificate lifecycle (init → status → renew → rotate-ca with overlap) is executable by an operator using only the CLI, and a fresh install via the standard installer comes up minting via certificates with no manual certificate steps.
- **SC-006**: The mutual-TLS join is proven by an integration test performing a real TLS handshake with a real CA-chained client certificate against the real trust-bundle validation into the real mint handler (no test authentication handler), and the test demonstrably fails when the CA is wrong, the leaf is expired, or the identity mismatches (mutation-checked).
- **SC-007**: On the live shared node, after deployment: at least one service mints via certificate with its secret removed, the platform's golden-path walkthrough passes, and after flipping the disable switch the secret path is refused while the golden path still passes.

## Assumptions

- The 8 seeded service principals and their client ids/scopes are the enrolment universe for v1; certificate issuance is driven off that known list, not a dynamic enrolment protocol (no CSR flow between services and the identity service — the CLI issues centrally, matching the installer-generates-credentials model of #1412).
- Certificate lifetime defaults: leaves ~2 years, CA ~5 years, renewal threshold ~30 days — pre-release posture where rotation is "re-run the CLI and recreate containers"; automated in-place rotation is future work.
- Revocation is handled operationally (rotate/re-issue and restart) rather than via CRL/OCSP in v1; the private CA and short chain make this acceptable and it is documented.
- The mutual-TLS listener is additive: existing plaintext internal listeners and all non-mint traffic are unchanged (full-hop mTLS is phase 2, a separate issue).
- Container-compromise threat model is unchanged: a mounted private key is readable by whoever owns the container, just as the env secret is today; the wins are wire-exposure elimination, server-side secret elimination, native expiry, and an HSM-capable asymmetric future.
- The existing per-deploy secret model (#1412) stays fully in place until the per-deployment retire step; nothing in this feature is a big-bang cutover.
