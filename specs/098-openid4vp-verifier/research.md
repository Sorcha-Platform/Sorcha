# Research: OpenID4VP Verifier Endpoint (HAIP)

**Spec**: 098-openid4vp-verifier
**Date**: 2026-04-11
**Status**: Complete

---

## R1. Service Architecture — Extends Sorcha.Haip.Service

**Decision**: Add verifier endpoints to the existing `Sorcha.Haip.Service` created by spec 097. Do not create a new service boundary.

**Rationale**:
- **Same trust posture.** The HAIP verifier and the HAIP issuer share the same zero-trust boundary: both interact with untrusted external wallets. Both sit behind the same rate-limiting, health-check, and API Gateway routing infrastructure. Splitting them into two services would duplicate all of this without security benefit.
- **Same signing identity.** The verifier signs Authorization Request Objects with the same classical signing key and `x5c` chain that the issuer uses for credential signing (spec 096 FR-017). One org identity, two roles. Sharing a service means one key resolution path, not two.
- **Spec 097 already created the service.** `Sorcha.Haip.Service` exists with its Dockerfile, Aspire resource, Docker Compose entry, and YARP route. Adding verifier endpoints is additive — no infrastructure changes needed.
- **Independent scaling is preserved.** If the verifier receives disproportionate load, the entire HAIP service scales horizontally (it is stateless except for Redis). There is no resource contention between issuer and verifier endpoints because both are thin orchestrators calling into Wallet/Blueprint/Tenant service clients.

**Alternatives considered**:
- **New `Sorcha.Haip.Verifier.Service`.** Rejected: doubles infrastructure (Dockerfile, Aspire resource, Docker Compose entry, YARP route, health check, rate-limit config) for a service that shares the same trust posture, signing identity, and Redis instance as the issuer. The constitution's microservices-first principle favours new services for new trust boundaries, not for new endpoints on the same boundary.
- **Add to Wallet Service.** Rejected: same reasoning as spec 097 R1. Wallet Service holds key material under strict policy. Exposing anonymous public endpoints (the `direct_post` callback) on the Wallet Service listener would widen its attack surface.

---

## R2. Response Mode — direct_post (HAIP 1.0 MTI)

**Decision**: Implement `response_mode: direct_post` as the only response mode. Do not implement `fragment`, `query`, or `direct_post.jwt`.

**Rationale**:
- **HAIP 1.0 mandate.** HAIP 1.0 Section 6 declares `direct_post` as the mandatory-to-implement response mode for OID4VP. A HAIP-conformant verifier MUST support it. Other response modes are optional and none are required for HAIP conformance.
- **Server-side verification.** `direct_post` sends the `vp_token` directly to the verifier's server-side callback URL. This is the only mode where Sorcha can perform server-side verification without relying on the browser's fragment or query string. Since Sorcha's verification pipeline involves `x5c` chain walks, status list HTTP fetches, and claim matching against Blueprint definitions, server-side execution is non-negotiable.
- **Cross-device compatibility.** `direct_post` works for both cross-device (QR scan) and same-device (deep link) flows because the wallet posts to a server URL, not back to the browser. The `fragment` and `query` modes require the wallet to redirect back to the caller's browser, which fails in the cross-device case (the browser and the wallet are on different devices).

**Alternatives considered**:
- **`direct_post.jwt`** (JARM — JWT-secured Authorization Response Mode). Viable for future hardening: the wallet would wrap the `vp_token` in a signed JWT before posting, providing an additional integrity layer. Deferred because HAIP 1.0 does not mandate it and it adds complexity (the verifier must validate two JWS layers per submission). Can be added non-breakingly.
- **`fragment`**. Rejected: incompatible with cross-device flows and server-side verification.
- **`query`**. Rejected: insecure (tokens in URL query strings are logged by proxies and browsers) and incompatible with cross-device flows.

---

## R3. Signed Request Object via request_uri

**Decision**: Serve the Authorization Request as a signed JWT at a stable `request_uri` URL. Do not use the inline `request` parameter.

**Rationale**:
- **HAIP 1.0 recommendation.** HAIP 1.0 Section 6.2 recommends `request_uri` for Authorization Requests. The signed Request Object at the URI provides integrity (wallet verifies the JWS signature) and authenticity (wallet checks the `x5c` chain) before proceeding with consent.
- **QR code size.** A full signed Authorization Request Object (containing the `presentation_definition`, `client_id`, `nonce`, etc.) can easily exceed QR code capacity (~2,953 bytes alphanumeric). The `request_uri` form produces a short URL that fits comfortably in a QR code. The wallet fetches the full Request Object from the URI.
- **Revocability.** A `request_uri` can be invalidated server-side if the Presentation Request is cancelled before the wallet fetches it. An inline `request` parameter, once rendered as a QR code, cannot be revoked.
- **Same-device deep links.** The `request_uri` form also works as a deep link: `openid4vp://authorize?client_id=...&request_uri=https://...`. The deep link is short and embeddable.

**Alternatives considered**:
- **Inline `request` parameter.** Rejected: payload too large for QR codes when the `presentation_definition` contains multiple input descriptors with field constraints. Also non-revocable once rendered.
- **Both inline and URI.** Unnecessary complexity. The URI form covers all use cases (QR, deep link, programmatic). The inline form adds no capability that the URI form lacks, given that the wallet must have network access to post via `direct_post` anyway.

---

## R4. Reuse Existing Core Verifier Library

**Decision**: Reuse `SdJwtService.VerifyPresentationAsync` (as fixed by spec 093) for core SD-JWT VC verification. The HAIP verifier is a thin adapter that translates the OID4VP wire protocol into calls to the existing verifier library.

**Rationale**:
- **Parity guarantee.** FR-003 requires that a credential verifying on the internal path verifies identically on the HAIP path. Sharing the same core verifier library makes this a structural guarantee, not a testing aspiration. There is one verification implementation, two wire protocol adapters.
- **No duplication.** The core verifier already handles: SD-JWT signature verification, selective disclosure reconstruction, `cnf`/KB-JWT validation (spec 094), status check dispatch (spec 095), `x5c` chain walk (spec 096). Reimplementing any of this in the HAIP service would create divergence risk and double the test surface.
- **Spec 093 already fixed it.** The signature-verification security bug found in Phase 1 (spec 093) was fixed in the core verifier. The HAIP path inherits the fix automatically by reusing the library.

**Alternatives considered**:
- **Reimplement verification in the HAIP service.** Rejected: violates DRY, doubles the verification test surface, creates divergence risk, and would not inherit the spec 093 security fix without explicit porting.
- **Call Wallet Service to verify.** Rejected for the same reason as spec 097 R4: the presented credential is from an external wallet. Wallet Service manages Sorcha-internal keys, not external wallet keys. The KB-JWT signature is verified against the holder's `cnf.jwk` (from the credential itself), not against any Sorcha-managed key. This verification belongs in the boundary service that received the submission.

---

## R5. x5c Chain Validation via ITrustStore (spec 096)

**Decision**: Validate incoming credential `x5c` chains by calling `ITrustStore.ValidateChainAsync` from spec 096. Fall back to DID-based trust resolution when no `x5c` chain is present.

**Rationale**:
- **Spec 096 already built it.** The `ITrustStore` interface provides `ValidateChainAsync(X509Certificate2[] chain)` which walks the chain, checks revocation via CRL, verifies the root against the configured trust anchors, and returns a structured result. No new X.509 logic needed.
- **Dual trust path.** HAIP 1.0 mandates X.509-based trust (`x5c` header). However, Sorcha-internal credentials (from earlier specs) may carry DID-based issuer identifiers without `x5c`. The verifier must handle both: `x5c` first (HAIP path), DID fallback (Sorcha-internal path). This dual path is required by FR-018.
- **Trust store is deployment-scoped.** Each Sorcha deployment configures its own trust anchors (root CAs). The verifier does not maintain a separate trust store — it shares the deployment's trust configuration with the issuer side. This ensures that a credential issued by a trusted org is verifiable by any verifier in the same deployment.

**Alternatives considered**:
- **Reimplement X.509 validation.** Rejected: duplicates spec 096's `ITrustStore`, risks divergence on revocation checking and chain-building logic.
- **X.509 only, no DID fallback.** Rejected: breaks backward compatibility with credentials issued before spec 096 (which carry DID-based `iss` claims). FR-018 explicitly requires the fallback.
- **External trust anchor service (e.g., EU Trust List).** Deferred. HAIP 1.0 supports external trust lists but Sorcha's current deployment model uses self-managed trust anchors. External trust list integration is a future operational spec.

---

## R6. Redis-Backed Presentation Request Store

**Decision**: Use Redis with TTL-based expiry for all presentation request state: request objects, nonces, state tokens, and verification results during the request lifecycle. Mirrors spec 097's pattern for OAuth state.

**Rationale**:
- **Consistency with spec 097.** The issuer side already uses Redis for pre-authorized codes, access tokens, and `c_nonce` values. Using the same storage pattern for presentation requests means one operational model, one monitoring approach, one failure mode.
- **Native TTL expiry.** Presentation requests have a configurable TTL (default 5 minutes). Redis `SETEX` handles automatic expiry. No background sweep jobs needed.
- **Atomic state transitions.** Presentation request state transitions (`Pending -> Submitted -> Verified/Denied`, `Pending -> Expired`, `Pending -> Cancelled`) must be atomic to prevent race conditions (e.g., a wallet submission arriving at the same moment the TTL expires). Redis `WATCH`/`MULTI`/`EXEC` or Lua scripts provide this.
- **Stateless service.** Any HAIP service instance can handle any request. No sticky sessions.

**Alternatives considered**:
- **PostgreSQL via EF Core.** Rejected: same reasoning as spec 097 R2. Schema migration for 5-minute-lived rows is overkill. Adds write load to the tenant database.
- **In-memory `ConcurrentDictionary`.** Rejected: single-instance only. Does not survive restarts. Cannot support horizontal scaling.
- **Store verification results in Blueprint action state only.** Partially adopted: the final verification result is persisted as Blueprint action state (via the existing workflow storage layer). But the in-flight presentation request lifecycle (nonce tracking, state transitions, request object serving) lives in Redis because it is transient and must be accessible from any HAIP service instance.

---

## R7. PresentationSource Enum on CredentialRequirement

**Decision**: Add a `PresentationSource` field to the existing `CredentialRequirement` model on Blueprint actions. Values: `SorchaInternal` (default, unchanged behaviour) and `HaipExternalWallet` (new, routes through the HAIP verifier). This mirrors spec 097's `TargetAudience` field on `CredentialIssuanceConfig`.

**Rationale**:
- **Symmetric design.** Spec 097 added `TargetAudience` to control the issuance path (internal vs. HAIP). This spec adds `PresentationSource` to control the presentation/verification path. Same pattern, same location in the model, same ergonomics for Blueprint authors. A Blueprint author who understands one field immediately understands the other.
- **Additive and backward-compatible.** The field defaults to `SorchaInternal`. Existing blueprints are unaffected. No migration needed for Blueprint JSON/YAML files that do not use the field.
- **Clean routing.** The `PresentationSource` field drives a single branching point in the Blueprint action execution engine: internal path (existing credential matching) or HAIP path (create Presentation Request, suspend action, resume on result). No other part of the execution engine changes.

**Alternatives considered**:
- **Reuse `TargetAudience` for both issuance and presentation.** Rejected: `TargetAudience` is on `CredentialIssuanceConfig` (output side). `PresentationSource` is on `CredentialRequirement` (input side). They are different models on different actions. Conflating them would create semantic confusion — an action can issue to a HAIP wallet (output) while requiring a presentation from a Sorcha-internal wallet (input), or vice versa.
- **Infer from issuer type.** Rejected: the source of the presentation (internal vs. external) is independent of the issuer. A credential issued by a Sorcha org to a GOV.UK Wallet (spec 097) could be presented back via either path. The Blueprint author must explicitly choose which path the action expects.
- **Global service-level toggle.** Rejected: too coarse. Different actions in the same Blueprint may require different presentation sources. The field must be per-action, per-credential-requirement.
