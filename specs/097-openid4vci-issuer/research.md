# Research: OpenID4VCI Issuer Endpoint (HAIP)

**Spec**: 097-openid4vci-issuer
**Date**: 2026-04-10
**Status**: Complete

---

## R1. Service Architecture

**Decision**: Introduce a new service, `Sorcha.Haip.Service`, as a thin boundary orchestrator. It is not bolted onto Wallet Service or Blueprint Service.

**Rationale**:
- **Zero-trust boundary.** Every other Sorcha service assumes authenticated Sorcha principals. The HAIP service is the only service where the caller is an untrusted external wallet. Mixing that trust posture into an existing service would force dual auth modes and widen the attack surface of the host service.
- **Independent scaling.** Credential issuance to external wallets is bursty and externally driven (QR scan from a crowd of applicants). Scaling it independently of Wallet Service (which handles internal crypto operations) and Blueprint Service (which handles workflow orchestration) avoids resource contention.
- **Different auth posture.** The HAIP service issues its own short-lived OAuth 2.0 access tokens via the pre-authorized code grant. It does not participate in the Sorcha JWT ecosystem on its external-facing endpoints. Keeping this OAuth surface isolated prevents accidental token confusion.

**Alternatives considered**:
- **Bolt onto Wallet Service.** Rejected: Wallet Service holds key material and runs under a strict `CanManageWallets` policy. Exposing unauthenticated HAIP endpoints on the same listener would require carving holes in the auth middleware and create a lateral movement risk if the HAIP surface is compromised.
- **Bolt onto Blueprint Service.** Rejected: Blueprint Service manages workflow state and SignalR connections. Its auth model assumes Sorcha-authenticated participants. Adding an anonymous OAuth token endpoint would break the "every caller is a known participant" invariant.
- **Standalone library consumed by API Gateway.** Rejected: YARP is a pass-through proxy with no business logic. Adding credential minting logic to the gateway violates its single responsibility and would make it a high-value target.

---

## R2. OAuth State Storage

**Decision**: Use Redis with TTL-based expiry for all short-lived OAuth state: pre-authorized codes (5 min TTL), access tokens (5 min TTL), and `c_nonce` values (5 min TTL).

**Rationale**:
- **Already in the stack.** Redis is provisioned via .NET Aspire (`builder.AddRedis()`) and used by SignalR backplane, rate limiting, and token revocation. No new infrastructure dependency.
- **Native TTL expiry.** Redis `SETEX`/`PSETEX` handles automatic cleanup. No background sweep jobs, no schema migrations, no EF entities.
- **Atomic operations.** Pre-authorized code exchange is a single `GET + DELETE` that must be atomic (one-time use). Redis `GETDEL` provides this natively.
- **Stateless service.** Storing OAuth state in Redis means any HAIP service instance can handle any request. No sticky sessions, no in-memory state.

**Alternatives considered**:
- **PostgreSQL via EF Core.** Rejected: schema migration for ephemeral 5-minute-lived rows is overkill. Requires a background job to sweep expired rows. Adds write load to the tenant database for data that is gone within minutes.
- **In-memory `ConcurrentDictionary`.** Rejected: single-instance only. Does not survive restarts. Cannot support horizontal scaling.
- **MongoDB.** Rejected: MongoDB is used for register/ledger data. OAuth state is key-value with TTL, which is Redis's native model. Using MongoDB would add an unnecessary cross-concern dependency.

---

## R3. Credential Minting Flow

**Decision**: The HAIP service calls Wallet Service to sign the SD-JWT VC. The HAIP service never holds signing keys.

**Rationale**:
- **Key material isolation.** Wallet Service is the single custodian of all signing keys (org master keys, derived keys, docket signing keys). The HAIP service is an untrusted-boundary service. If it held signing keys, a compromise of the HAIP surface would grant credential forgery capability.
- **Existing signing infrastructure.** Wallet Service already exposes `POST /api/v1/wallets/{address}/credentials/issue` for internal issuance and has the full SD-JWT VC pipeline (selective disclosure, `cnf` binding, status claims). The HAIP service reuses this pipeline by calling it with the holder's public key (extracted from the JWT proof) as the `holderJwk` parameter.
- **Stateless HAIP service.** The HAIP service translates wire protocol (OpenID4VCI JSON) into Sorcha service calls and translates the result back. It holds no persistent state beyond the Redis-backed OAuth tokens.

**Alternatives considered**:
- **HAIP service holds a delegated signing key.** Rejected: adds key distribution, rotation, and revocation complexity. Doubles the number of places where key compromise matters. Violates the "Wallet Service is the only key custodian" architectural invariant.
- **HAIP service calls a generic signing API.** Rejected: the signing operation is not generic. It requires SD-JWT selective disclosure, `cnf` claim injection, `x5c` header attachment, and status claim wiring. Wallet Service already does all of this. Extracting a generic signing API would duplicate logic.

---

## R4. JWT Proof of Possession Validation

**Decision**: Validate the wallet's JWT proof of possession in-process within the HAIP service, using the same crypto primitives available via `Sorcha.Cryptography`.

**Rationale**:
- **No external dependency needed.** The JWT proof is a standard JWS (compact serialization) signed by the wallet's holder key. The holder key is in the `jwk` header of the proof JWT itself (HAIP 1.0 Section 7.2.1.1). Validation is: parse JWS, extract `jwk` from header, verify signature, check `aud`/`iat`/`nonce` claims. `Sorcha.Cryptography` already supports ES256 (P-256) signature verification.
- **Low latency.** Calling Wallet Service to verify a proof signed by an *external* key (not a Sorcha-managed key) would be a wasted network hop. Wallet Service has no knowledge of the external wallet's key.
- **Security boundary alignment.** The proof validation decides whether to proceed with credential issuance. This decision belongs in the HAIP service, which owns the OAuth flow. Delegating it would split the authorization decision across two services.

**Alternatives considered**:
- **Delegate to Wallet Service.** Rejected: Wallet Service manages Sorcha-internal keys. The holder key in a JWT proof belongs to an external wallet. Wallet Service has no reason to know about it. Adding an "verify arbitrary external JWS" endpoint to Wallet Service would widen its API surface unnecessarily.
- **Use a third-party JWT library (e.g., `Microsoft.IdentityModel.JsonWebTokens`).** Viable but unnecessary. The proof JWT is a single-use JWS with a known algorithm (ES256 per HAIP MTI). `Sorcha.Cryptography` can verify ES256 signatures directly. Adding a full JWT library for one verification call adds dependency weight without benefit. If future specs require more complex JWT handling, this decision can be revisited.

---

## R5. Credential Offer Delivery

**Decision**: Generate both inline (`credential_offer=...`) and URI-based (`credential_offer_uri=...`) Credential Offers. Return both forms to the caller (Blueprint Service) so the UI can choose the appropriate delivery mechanism.

**Rationale**:
- **HAIP 1.0 supports both.** Section 4.1 defines both the inline offer (credential data embedded in the URI) and the URI-based offer (credential data hosted at a resolvable URL). Different wallets may prefer different forms. QR codes have size limits (~2,953 bytes for alphanumeric mode at error correction level L) that large inline offers can exceed.
- **UI flexibility.** The Sorcha UI can render the inline form as a QR code for small offers and fall back to the URI form (shorter QR, wallet fetches the full offer from the HAIP service) for large offers. Deep links on mobile always use the URI form.
- **Offline-first inline.** The inline form works even if the wallet cannot immediately reach the HAIP service to resolve a URI (e.g., poor connectivity at the point of scanning). The URI form works better when the offer payload is large or when the offer should be revocable before redemption.

**Alternatives considered**:
- **Inline only.** Rejected: large credential offers (multiple credential types, rich display metadata) can exceed QR code capacity. A fallback is needed.
- **URI only.** Rejected: adds a mandatory network round-trip before the wallet can even parse the offer. Inline offers are simpler for small payloads and reduce latency.

---

## R6. Metadata Endpoint

**Decision**: Build the `.well-known/openid-credential-issuer` metadata document dynamically from enrolled HAIP issuers, rather than serving a static JSON file.

**Rationale**:
- **Credential types depend on enrollment.** Each organisation enrolled as a HAIP issuer may support different credential types (licence, permit, certificate). The `credentials_supported` array must reflect the current set. A static file would require manual updates on every enrollment change.
- **Multi-tenant awareness.** A single HAIP service instance may serve multiple tenants (or a single tenant with multiple issuer orgs). The metadata must reflect the correct issuer identity and credential catalogue for the resolved tenant.
- **Display metadata.** HAIP 1.0 Section 5 requires `display` objects (name, locale, logo, background colour) per credential type. These come from the issuer org's configuration in Tenant Service, not from a static file.

**Alternatives considered**:
- **Static JSON file per tenant.** Rejected: requires regeneration on every enrollment change. Race condition between enrollment and metadata availability. No single source of truth.
- **Cache with invalidation.** Complementary, not alternative. The dynamic builder should cache the result in Redis with a short TTL (e.g., 60s) and invalidate on enrollment changes. This is an implementation detail, not a design decision.

---

## R7. No External OpenID4VCI Library

**Decision**: Implement the OpenID4VCI wire protocol directly against the HAIP 1.0 specification. Do not use a third-party NuGet package.

**Rationale**:
- **No mature .NET library exists.** As of April 2026, there is no production-grade, actively maintained .NET NuGet package implementing OpenID4VCI issuer-side logic. The ecosystem is dominated by Java (Keycloak plugins) and TypeScript (SpruceID, Walt.id) implementations.
- **Narrow protocol surface.** HAIP 1.0 constrains the flow to the pre-authorized code grant only. No browser redirects, no authorization code flow, no dynamic client registration. The wire format is simple JSON over HTTP: one metadata endpoint, one token endpoint, one credential endpoint, one nonce endpoint. The total protocol surface is approximately 5 request/response pairs.
- **Full control over security.** The HAIP service is a zero-trust boundary. Depending on a third-party library for the authentication and token exchange logic would require auditing that library's security posture. Implementing directly means every line of code is under Sorcha's control and review.
- **HAIP constraints simplify implementation.** HAIP 1.0 mandates ES256 for holder proof JWTs, `vc+sd-jwt` for credential format, and pre-authorized code for the grant type. There are no algorithm negotiation branches, no format negotiation branches, and no grant type negotiation branches. The implementation is a straight-line flow.

**Alternatives considered**:
- **Walt.id SSI Kit (.NET bindings).** Rejected: Walt.id is primarily Kotlin/JVM. The .NET bindings are community-maintained and not production-grade. Adding a JVM interop dependency for a simple HTTP protocol is disproportionate.
- **Fork and adapt a TypeScript implementation.** Rejected: cross-language porting introduces translation bugs. The protocol is simple enough that a clean-room .NET implementation is less risky than a port.
- **Wait for a .NET library to mature.** Rejected: blocks the spec set timeline. The protocol surface is small enough that the implementation effort is measured in days, not weeks.

---

## R8. x5c and Status Claim Wiring

**Decision**: The HAIP credential endpoint calls `ITrustProvider.GetOrgCertChainAsync` (spec 096) for the `x5c` certificate chain and uses `StatusClaimForm.IetfTokenStatusList` (spec 095) for the credential status claim. These are wired through the existing service interfaces, not reimplemented.

**Rationale**:
- **Specs 095 and 096 are already merged (or planned to merge before 097).** The `ITrustProvider` interface and the IETF Token Status List infrastructure are available as first-class services. Reimplementing them in the HAIP service would duplicate logic and create divergence risk.
- **x5c chain is org-scoped.** The certificate chain depends on the issuing organisation's X.509 trust hierarchy (spec 096). Wallet Service already resolves this via `ITrustProvider`. The HAIP service passes the issuer org ID and receives the chain. No new crypto logic needed.
- **Status list allocation is register-scoped.** The IETF Token Status List (spec 095) allocates a status index per credential and publishes the compressed bitstring at a well-known URL. The HAIP service requests a status index during credential minting (via the existing `IStatusListService`) and embeds the resulting `status` claim in the SD-JWT VC payload. No new status list logic needed.
- **Consistency.** Internal-path and HAIP-path credentials carry identical `x5c` and `status` claims. A verifier cannot distinguish between a credential issued to a Sorcha wallet and one issued to a GOV.UK Wallet by looking at the trust chain or status mechanism. This is by design.

**Alternatives considered**:
- **Reimplement x5c resolution in the HAIP service.** Rejected: duplicates cert chain logic, risks divergence if the trust hierarchy changes, violates single-responsibility.
- **Skip status claims for HAIP-path credentials.** Rejected: HAIP 1.0 requires a revocation/status mechanism. Omitting it would make Sorcha-issued credentials non-conformant and unverifiable by HAIP wallets that check status.
- **Use a different status mechanism (e.g., StatusList2021).** Rejected: spec 095 implements the IETF Token Status List (draft-ietf-oauth-status-list), which is the HAIP 1.0 mandated mechanism. StatusList2021 is the W3C predecessor and is not HAIP-conformant.
