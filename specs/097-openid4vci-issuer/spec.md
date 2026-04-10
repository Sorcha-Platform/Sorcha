# Feature Specification: OpenID4VCI Issuer Endpoint (HAIP)

**Feature Branch**: `097-openid4vci-issuer`
**Created**: 2026-04-09
**Status**: Draft
**Input**: User description: "HAIP OpenID4VCI issuer endpoint in new Sorcha.Haip.Service: metadata, token endpoint, credential endpoint, credential offer URIs for external wallets"

## Context

Phase 1 confirmed that Sorcha has no OpenID4VCI support on master: no issuer metadata endpoint, no OAuth 2.0 token endpoint, no Credential Offer URI generator, no Credential Endpoint with JWT proof of possession. External HAIP wallets (GOV.UK Wallet, EUDI Wallet, any HAIP-conformant wallet or test harness) cannot obtain a Sorcha-issued credential today. The existing issuance path (`CredentialEndpoints.cs` `POST /api/v1/wallets/{address}/credentials/issue`) is Sorcha-shaped, authenticated with `CanManageWallets`, and writes credentials directly into another Sorcha wallet row — there is no wire protocol an external wallet can speak.

This spec closes that gap by standing up **a new boundary service**, `Sorcha.Haip.Service` (Phase 2 D1 Option A), and hosting the OpenID4VCI issuance protocol in it. The service is a thin orchestrator: it speaks HAIP on the outside and calls into existing Sorcha services on the inside. It holds no signing keys. It is the one place in the Sorcha architecture where the trust posture is "treat every caller as untrusted external until proven otherwise" — distinct from every other Sorcha service, which assume authenticated Sorcha principals.

The HAIP 1.0 minimum-to-implement flow for wallet-scoped issuance is the **pre-authorized code grant**: an existing Sorcha-side workflow (typically a Blueprint action) decides that a credential should be issued to an external holder, generates a Credential Offer carrying a one-time pre-authorized code, surfaces that offer as a QR or deep link, and the external wallet then exchanges the code for an access token and fetches the credential. Browser-redirect authorization code flow is not required by HAIP 1.0 MTI and is deferred here.

**Related specs.**
- **Hard dependency on spec 093** (`vc-security-fixes`) — the verifier baseline must be correct.
- **Hard dependency on spec 094** (`sdjwt-haip-hardening`) — `cnf` binding, KB-JWT, nested disclosure, classical co-key all required.
- **Hard dependency on spec 095** (`ietf-token-status-list`) — HAIP-path credentials emit IETF `status.status_list` claims.
- **Hard dependency on spec 096** (`x509-org-trust`) — HAIP-path credentials carry `x5c` chains in the outer JWS header.
- **Required by spec 098** (`openid4vp-verifier`) — the verifier boundary will sit in the same `Sorcha.Haip.Service` and share its HAIP-side infrastructure.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A citizen receives a Sorcha-issued licence credential into their GOV.UK Wallet via QR code (Priority: P1)

An operator applies to their council for a short-term-let licence through a Blueprint-driven workflow. The council participant approves the application, which triggers a Blueprint action that issues a licence credential to the operator. Rather than writing the credential into a Sorcha-internal participant's wallet, this credential is destined for the operator's GOV.UK Wallet. The Blueprint action generates a Credential Offer, the council's Sorcha UI shows it to the operator as a QR code, the operator scans the QR with GOV.UK Wallet, the wallet authenticates the operator (via GOV.UK One Login), exchanges the pre-authorized code, fetches the credential, and lands it in the operator's wallet bound to the operator's device key.

From the operator's point of view: they scan a QR, tap Approve, and see a licence card appear in their wallet. From the council's point of view: they approve an application in Sorcha. From the Sorcha architect's point of view: everything between those two points is HAIP 1.0, and none of it requires Sorcha-specific knowledge on either side of the wire.

**Why this priority**: This is the entire payoff for the spec set. Without this, the "Sorcha is the workflow layer above GOV.UK Wallet" positioning never demonstrates. All other specs in the 093–098 series exist to make this one scenario work end-to-end.

**Independent Test**: Run a Blueprint that triggers HAIP-path issuance. Scan the emitted QR code with a HAIP-conformant test wallet (open-source reference implementation is sufficient). Confirm the wallet completes the pre-authorized code flow, receives a valid SD-JWT VC, stores it, and displays it. Verify the stored credential contains `cnf` bound to the wallet's holder key, carries an `x5c` chain to the tenant root, and carries an IETF `status.status_list` claim.

**Acceptance Scenarios**:

1. **Given** a Blueprint action configured to issue a credential via the HAIP path, **When** the action fires and names a target audience, **Then** the Blueprint Service calls the HAIP service to create a Credential Offer and receives an `openid-credential-offer` URI in return.
2. **Given** a Credential Offer URI, **When** it is rendered as a QR and scanned by a HAIP-conformant wallet, **Then** the wallet fetches the issuer's `.well-known/openid-credential-issuer` metadata and sees the supported credential formats and endpoints.
3. **Given** a wallet with the pre-authorized code from the Credential Offer, **When** it posts the code to the issuer's token endpoint, **Then** it receives an access token, a `c_nonce`, and the credential format descriptor identifying which SD-JWT VC to request.
4. **Given** a wallet with a valid access token and `c_nonce`, **When** it calls the credential endpoint with a JWT proof of possession signed by the wallet's holder key and whose payload binds the `c_nonce`, **Then** the issuer returns a signed SD-JWT VC whose payload contains a `cnf` claim naming the wallet's holder key, whose JWS header contains an `x5c` chain to the tenant root, and whose payload contains an IETF `status.status_list` claim.
5. **Given** a wallet that receives a credential through this flow, **When** it stores and later displays the credential, **Then** the credential verifies against its own `x5c` chain and its own `cnf` claim without any Sorcha-specific knowledge on the wallet side.

---

### User Story 2 - A Sorcha deployment exposes an OpenID4VCI issuer metadata endpoint (Priority: P1)

An operator of a Sorcha deployment enables HAIP issuance on a tenant that is provisioned for X.509 trust (spec 096) and has at least one organisation enrolled as a HAIP issuer. The deployment exposes the standard HAIP discovery URLs — `.well-known/openid-credential-issuer` and `.well-known/oauth-authorization-server` — at a public URL under the deployment's domain. Any HAIP-conformant wallet or test harness can fetch those URLs, parse the metadata, and learn which credential types the issuer can mint, which signing algorithms it supports, where its token endpoint is, where its credential endpoint is, and where its nonce endpoint is.

**Why this priority**: Issuer metadata is the discovery entry point. Everything else in the flow hangs off it. A HAIP wallet with no knowledge of Sorcha must be able to use the metadata to bootstrap the whole issuance flow without prior configuration. This is the "is this really OpenID4VCI?" test.

**Independent Test**: Fetch `.well-known/openid-credential-issuer` from a running Sorcha.Haip.Service with an enrolled issuer. Validate the returned JSON against the HAIP 1.0 metadata schema in an independent validator. Confirm every declared endpoint (`token_endpoint`, `credential_endpoint`, `nonce_endpoint`) resolves and responds sensibly to a basic well-formed request. Confirm the `credentials_supported` array contains at least one entry describing an SD-JWT VC credential type.

**Acceptance Scenarios**:

1. **Given** a Sorcha.Haip.Service running on a HAIP-enabled tenant with at least one HAIP issuer organisation enrolled, **When** a caller fetches the `.well-known/openid-credential-issuer` URL, **Then** the response is a JSON document containing at minimum `credential_issuer`, `credential_endpoint`, `token_endpoint`, `nonce_endpoint`, `credentials_supported`, and `display` per HAIP 1.0 Section 5.
2. **Given** the issuer metadata document, **When** a caller parses it, **Then** every declared credential format in `credentials_supported` names `vc+sd-jwt` (matching spec 094 FR-038), and each entry declares its supported signing algorithms (ES256 mandatory, additional algorithms optional).
3. **Given** the issuer metadata document, **When** a caller parses it, **Then** the declared `token_endpoint`, `credential_endpoint`, and `nonce_endpoint` are all HTTPS URLs under the deployment's public domain and all resolve.
4. **Given** a HAIP-enabled deployment that also publishes a parallel `.well-known/oauth-authorization-server` document, **When** a caller fetches it, **Then** the document contains the token endpoint URL, the grant types supported (including `urn:ietf:params:oauth:grant-type:pre-authorized_code`), and the token endpoint authentication methods supported.
5. **Given** a Sorcha deployment where no HAIP issuer organisation is enrolled, **When** a caller fetches `.well-known/openid-credential-issuer`, **Then** the response either returns a metadata document with an empty `credentials_supported` array, or returns 404, consistently and not both. The chosen behaviour is documented in the deployment's operational configuration.

---

### User Story 3 - A Blueprint action triggers a Credential Offer without the author writing HAIP-specific code (Priority: P1)

A Blueprint author writes an action that issues a licence credential. They configure the existing `CredentialIssuanceConfig` block as they would today: credential type, claim mappings, recipient reference, expiry. They add one new field: `TargetAudience` = `HaipExternalWallet` (or similar) to say the credential should flow to an external HAIP wallet rather than to another Sorcha participant. When the action fires at runtime, the Blueprint Service calls the HAIP service to create a Credential Offer carrying the mapped claims, and the resulting offer URI is returned up the Blueprint execution chain for the UI to render. The Blueprint author never touches OAuth 2.0, pre-authorized codes, JWT proofs, or X.509 chains.

**Why this priority**: Without this, every Blueprint author who wants to issue to an external HAIP wallet would have to become an OpenID4VCI expert. The whole point of the spec set is that the HAIP plumbing is invisible to the Blueprint authoring layer. This is the ergonomics contract.

**Independent Test**: Take an existing Blueprint template that issues a credential via the internal path. Add `TargetAudience: HaipExternalWallet` to the credential issuance config. Run the Blueprint. Confirm the action's execution result contains a `CredentialOfferUri` (in addition to or instead of the internal `CredentialIssuanceResult`), and confirm that URI resolves to a valid HAIP Credential Offer.

**Acceptance Scenarios**:

1. **Given** a Blueprint credential issuance configuration with `TargetAudience: HaipExternalWallet`, **When** the action fires, **Then** the Blueprint Service calls Sorcha.Haip.Service to create a Credential Offer and receives a URI back.
2. **Given** the same configuration, **When** the action completes, **Then** its execution result carries a `CredentialOfferUri` field that can be surfaced to the Sorcha UI for rendering as a QR code.
3. **Given** a Blueprint credential issuance configuration without the `TargetAudience` field (or with `TargetAudience: SorchaInternal`), **When** the action fires, **Then** the existing internal issuance path runs unchanged and no HAIP Credential Offer is created.
4. **Given** a Blueprint configuration that names disclosable fields by JSON Pointer (spec 094 nested disclosure), **When** the action issues a HAIP-path credential, **Then** the emitted credential carries the same nested disclosure structure as an internal-path credential would.
5. **Given** a Blueprint configuration naming a `RecipientParticipantId` that is **not** a Sorcha participant (because the recipient is an external HAIP wallet holder), **When** the action fires, **Then** the Blueprint Service correctly routes through the HAIP path without failing on the missing recipient wallet lookup.

---

### User Story 4 - The HAIP token endpoint exchanges a pre-authorized code for an access token and nonce (Priority: P1)

An external HAIP wallet has received a Credential Offer URI and extracted the pre-authorized code from it. The wallet calls the issuer's token endpoint with the code and the grant type `urn:ietf:params:oauth:grant-type:pre-authorized_code`. The issuer validates the code (one-time use, not expired, correct issuer binding), allocates an access token with a short TTL, generates a `c_nonce` for the JWT proof of possession step, and returns the access token, `c_nonce`, and a credential format descriptor.

**Why this priority**: This is the OAuth 2.0 glue that connects the Credential Offer to the credential endpoint. Without a working token endpoint, no wallet can progress past the Credential Offer. It is also one of only two places where Sorcha.Haip.Service has to implement OAuth 2.0 semantics, which is why it gets its own User Story.

**Independent Test**: Create a Credential Offer directly via the internal Blueprint-facing API. Extract the pre-authorized code. POST it to the token endpoint with the correct grant type. Confirm the response is a valid OAuth 2.0 token response containing `access_token`, `token_type`, `expires_in`, `c_nonce`, and `c_nonce_expires_in`. Try to reuse the code and confirm the second attempt fails with `invalid_grant`.

**Acceptance Scenarios**:

1. **Given** an issued Credential Offer with a valid pre-authorized code, **When** the wallet posts the code to the token endpoint with the correct grant type, **Then** the token endpoint returns a JSON response containing `access_token`, `token_type: Bearer`, `expires_in`, `c_nonce`, and `c_nonce_expires_in`.
2. **Given** the same pre-authorized code is posted a second time, **When** the token endpoint processes it, **Then** the response is `invalid_grant` with the code rejected as already consumed.
3. **Given** a pre-authorized code whose TTL has elapsed, **When** the wallet posts it, **Then** the response is `invalid_grant` with the code rejected as expired.
4. **Given** a post to the token endpoint with a missing or wrong grant type, **When** the endpoint processes it, **Then** the response is `unsupported_grant_type`.
5. **Given** a Credential Offer that declared a `tx_code` requirement (user-presented transaction code for additional binding), **When** the wallet posts the pre-authorized code without the `tx_code`, **Then** the response is `invalid_request` and the transaction is not consumed.
6. **Given** an access token issued by the token endpoint, **When** its TTL elapses, **Then** subsequent calls to the credential endpoint using it fail with `invalid_token`.

---

### User Story 5 - The HAIP credential endpoint mints an SD-JWT VC bound to the wallet's holder key (Priority: P1)

An external HAIP wallet has a valid access token and a fresh `c_nonce`. It constructs a JWT proof of possession: a small JWT signed by the wallet's holder key, whose header declares the key in `jwk` form and whose payload binds the `c_nonce`. The wallet calls the credential endpoint with the access token and the proof. The issuer validates the proof (signature over the correct signing input, `c_nonce` matches a freshly issued one, `iat` within the clock skew window), extracts the holder public key from the proof's header, and issues an SD-JWT VC whose `cnf` claim is the holder key, whose JWS `x5c` header is the issuer chain, and whose payload carries the mapped claims from the originating Blueprint action plus the IETF `status.status_list` pointer.

**Why this priority**: This is the credential-mint step — the step that actually delivers the payoff. It is also the step where every prior spec in the 093–098 series converges: holder binding (094), classical co-key selection (094), IETF status claim (095), `x5c` header (096). If this endpoint does not work end to end, nothing downstream works.

**Independent Test**: Using the access token and `c_nonce` from User Story 4, construct a valid JWT proof of possession signed by a test holder key, call the credential endpoint, and validate the response. The response must contain a serialised SD-JWT VC whose decoded payload contains `cnf` with the test holder key, whose JWS header contains `x5c` with the tenant root at the end, and whose payload contains `status.status_list` pointing at the IETF endpoint. Verify the SD-JWT VC signature against the issuer's public key extracted from the `x5c` chain.

**Acceptance Scenarios**:

1. **Given** a valid access token and `c_nonce`, **When** the wallet posts a credential request with a valid JWT proof of possession, **Then** the credential endpoint returns a JSON response containing a `credential` field carrying a serialised SD-JWT VC.
2. **Given** the serialised SD-JWT VC from the credential response, **When** the payload is decoded, **Then** it contains `iss` identifying the issuer organisation, `cnf.jwk` matching the holder public key presented in the proof, `vct` naming the credential type, mapped claims per the originating Blueprint action, and an IETF `status.status_list` claim with `uri` and `idx`.
3. **Given** the same SD-JWT VC, **When** the JWS header is inspected, **Then** it contains an `x5c` array whose leaf cert's Subject Public Key Info matches the issuer's classical HAIP signing key and whose final element is the tenant root CA.
4. **Given** a credential request with a JWT proof whose signature does not verify against the `jwk` declared in the proof's header, **When** the credential endpoint processes it, **Then** the response is `invalid_proof`.
5. **Given** a credential request with a JWT proof whose `c_nonce` does not match any recently issued `c_nonce`, **When** the credential endpoint processes it, **Then** the response is `invalid_nonce` and the request is rejected.
6. **Given** a credential request with a JWT proof whose `iat` is more than the acceptable clock skew window away from server time, **When** the credential endpoint processes it, **Then** the response is `invalid_proof` with clock skew named as the cause.
7. **Given** a credential request with a valid access token but a proof carrying a different holder key than the original proof that minted the token, **When** the credential endpoint processes it, **Then** the proof is accepted and the new holder key goes into `cnf`. (The access token does not bind a specific holder key; the JWT proof does.)
8. **Given** a credential request where the underlying Blueprint action has already been fulfilled, **When** the wallet calls the credential endpoint a second time with a different holder key (for example, the user moved wallets), **Then** the behaviour is governed by the credential issuance configuration: either a second credential is minted for the new holder key (if the action permits reissuance), or the request is refused with `invalid_request`. The default is refuse.

---

### User Story 6 - Sorcha.Haip.Service is a standalone eighth service with its own deployment lifecycle (Priority: P2)

Operators of a Sorcha deployment run `Sorcha.Haip.Service` as a separate Aspire-managed service alongside the existing seven. It has its own Dockerfile, its own port, its own health check, its own rate-limit policies, and its own authentication posture (anonymous endpoints for metadata and token, rate-limited for the credential endpoint). It does not hold any signing keys of its own — it calls into Wallet Service for all signing operations and into Blueprint Service for Credential Offer lifecycle.

**Why this priority**: Phase 2 D1 Option A chose a new service rather than bolting HAIP into Wallet Service, because the boundary posture (anonymous, OAuth 2.0, X.509, external) is distinct from every other Sorcha service's posture. This story confirms the service is a real first-class citizen in the architecture, not a sidecar.

**Independent Test**: Stand up the Sorcha dev environment via `docker-compose up -d` and confirm `sorcha-haip-service` appears as a managed container. Confirm it has a public health endpoint, an assigned port in the Sorcha port configuration, a dedicated route cluster in the API Gateway, and an Aspire service discovery entry.

**Acceptance Scenarios**:

1. **Given** the Sorcha dev environment configuration, **When** `docker-compose up -d` runs, **Then** a `sorcha-haip-service` container is created alongside the existing seven services and reports healthy on its `/health` endpoint.
2. **Given** a running HAIP service, **When** the API Gateway routes HAIP-discovery traffic (`/.well-known/openid-credential-issuer`, `/.well-known/oauth-authorization-server`, the token endpoint, the credential endpoint, the nonce endpoint), **Then** the requests resolve to the HAIP service cluster and return correctly.
3. **Given** the HAIP service, **When** it starts up without any signing keys locally, **Then** it successfully obtains its issuer identity by calling the Wallet Service for the org's classical HAIP signing key.
4. **Given** the HAIP service, **When** it receives a credential request and needs to sign an SD-JWT VC, **Then** it calls the Wallet Service's signing API with the relevant JWS input and receives a signed token in return — no private key material touches the HAIP service.
5. **Given** the HAIP service is exposed to the public internet via the API Gateway, **When** metadata and token endpoints are hit without authentication, **Then** they respond successfully (anonymous by design) and are protected by rate limits rather than auth.
6. **Given** the HAIP service is stopped (container down), **When** existing internal Sorcha workflows run, **Then** internal credential issuance continues to work normally because internal issuance does not depend on the HAIP service.

---

### Edge Cases

- What happens when a Credential Offer is created but the external wallet never scans the QR? The pre-authorized code expires after its TTL (default 5 minutes) and the offer becomes unusable. The offer record is retained for audit and then garbage collected on a schedule.
- What happens when a wallet exchanges the pre-authorized code successfully but never calls the credential endpoint? The access token expires after its TTL (default 5 minutes) and the credential is never issued. The underlying Blueprint action remains in its post-action state; whether it is considered "complete" depends on the Blueprint author's choice (see Clarifications).
- What happens when two wallets race to exchange the same pre-authorized code? The first succeeds, the second fails with `invalid_grant`. Pre-authorized codes are strictly one-time.
- What happens when the wallet presents a proof whose JWK is in a format the issuer does not recognise (for example, a public key encoded differently)? The issuer fails the request with `invalid_proof`. The supported JWK key types for holder keys are at minimum Ed25519, NIST P-256, and RSA (matching the Sorcha classical verification set).
- What happens when the issuer's classical co-key is rotated between the creation of the Credential Offer and the call to the credential endpoint? The credential is signed with whichever key is current at signing time; the Credential Offer does not pre-commit a signing key. If the rotation invalidated the Org Cert, the credential endpoint fails with `invalid_request`.
- What happens when the Blueprint action that triggered the Credential Offer is cancelled before the wallet completes the flow? The pre-authorized code is marked cancelled and the next call to the token endpoint fails with `invalid_grant`. The wallet sees a clean failure; the Blueprint action rollback is handled by the Blueprint Service as usual.
- What happens when the HAIP service receives a request for a credential type that is not in its `credentials_supported` metadata? The credential endpoint fails with `unsupported_credential_format` before any signing is attempted.
- What happens when the wallet's pre-authorized code contains a `tx_code` requirement (user-presented transaction code) but the Sorcha deployment does not support `tx_code` on its pre-authorized code flow? The Credential Offer simply does not include a `tx_code` requirement. Adding `tx_code` support is a future extension; HAIP 1.0 MTI does not require it for wallet-bound flows, although the HAIP profile allows it.
- What happens when the HAIP service sits behind the API Gateway but the gateway rewrites URLs? All URLs in issuer metadata must be the public-facing URLs, not the internal service URLs. This is a deployment configuration concern; the service reads its public base URL from configuration rather than constructing it from the request.
- What happens when the Blueprint action declares a `RecipientParticipantId` but the `TargetAudience` is `HaipExternalWallet`? The `RecipientParticipantId` is treated as an advisory tag for display and audit only; the actual recipient is whoever scans the QR. If the Blueprint author wants to restrict who can scan, they can add a `tx_code` or bind to an authenticated GOV.UK One Login session — that is out of scope for this spec.

## Requirements *(mandatory)*

### Functional Requirements

**New service and topology:**
- **FR-001**: The system MUST introduce a new service `Sorcha.Haip.Service` as an eighth first-class service alongside the existing seven (API Gateway, Blueprint, Peer, Register, Tenant, Validator, Wallet).
- **FR-002**: `Sorcha.Haip.Service` MUST have its own Aspire orchestration entry, its own Dockerfile, its own port assignment in the Sorcha port configuration, its own health check, and its own route cluster in the API Gateway.
- **FR-003**: `Sorcha.Haip.Service` MUST NOT hold any long-lived cryptographic signing keys of its own. All signing operations MUST be delegated to the Wallet Service via the existing service client pattern.
- **FR-004**: `Sorcha.Haip.Service` MUST read its public base URL from configuration rather than constructing it from incoming requests, so URLs in issuer metadata match the deployment's actual public-facing domain regardless of how the API Gateway rewrites paths.
- **FR-005**: Stopping `Sorcha.Haip.Service` MUST NOT impair any internal Sorcha workflow. Internal credential issuance (via the existing `CredentialEndpoints` path on the Wallet Service) continues to work.

**Issuer metadata:**
- **FR-006**: The HAIP service MUST expose `.well-known/openid-credential-issuer` as an anonymous, publicly cacheable HTTP endpoint that returns a JSON metadata document conforming to HAIP 1.0 Section 5.
- **FR-007**: The metadata document MUST contain at minimum `credential_issuer`, `credential_endpoint`, `token_endpoint`, `nonce_endpoint`, `credentials_supported`, and `display`.
- **FR-008**: Every entry in `credentials_supported` MUST declare `format: vc+sd-jwt`, matching spec 094 FR-038.
- **FR-009**: Every entry in `credentials_supported` MUST declare its supported `cryptographic_binding_methods_supported` (at minimum `jwk`) and `credential_signing_alg_values_supported` (at minimum ES256, matching HAIP 1.0 MTI).
- **FR-010**: Every entry in `credentials_supported` MUST declare a `vct` value that uniquely identifies the credential type, consistent with the `vct` claim embedded in issued credentials.
- **FR-011**: The HAIP service MUST expose `.well-known/oauth-authorization-server` as an anonymous, publicly cacheable HTTP endpoint containing at minimum `token_endpoint`, `grant_types_supported` (including `urn:ietf:params:oauth:grant-type:pre-authorized_code`), and `token_endpoint_auth_methods_supported`.
- **FR-012**: Both metadata endpoints MUST return `Cache-Control` headers with a configurable TTL (default 1 hour).

**Credential Offer:**
- **FR-013**: The HAIP service MUST expose an internal API (service-to-service, callable from the Blueprint Service) for creating a Credential Offer for a specific credential type with mapped claims pre-computed.
- **FR-014**: Creating a Credential Offer MUST allocate a one-time pre-authorized code with a configurable TTL (default 5 minutes).
- **FR-015**: The returned Credential Offer MUST be expressible both as a JSON object and as an `openid-credential-offer` URI deep link.
- **FR-016**: The Credential Offer URI MUST be renderable as a QR code by the Sorcha UI without requiring the HAIP service to generate the QR image itself.
- **FR-017**: The Credential Offer record MUST carry the originating Blueprint action identifier, the credential type, the pre-computed claims, the status list index allocation (per spec 095), and the selected classical issuer co-key identifier.
- **FR-018**: Pre-authorized codes MUST be strictly one-time. A second exchange attempt with the same code MUST fail with `invalid_grant`.
- **FR-019**: Pre-authorized codes MUST expire after their TTL and subsequent exchange attempts MUST fail with `invalid_grant`.
- **FR-020**: Expired or consumed Credential Offer records MAY be retained for audit purposes for a configurable retention window, then garbage collected.

**Token endpoint:**
- **FR-021**: The HAIP service MUST expose an anonymous `POST` token endpoint implementing the OAuth 2.0 Token Endpoint protocol.
- **FR-022**: The token endpoint MUST accept the grant type `urn:ietf:params:oauth:grant-type:pre-authorized_code` with the pre-authorized code as the `pre-authorized_code` parameter.
- **FR-023**: On successful exchange, the token endpoint MUST return a JSON response containing `access_token`, `token_type: Bearer`, `expires_in`, `c_nonce`, and `c_nonce_expires_in`.
- **FR-024**: Access tokens issued by the token endpoint MUST have a short TTL (default 5 minutes).
- **FR-025**: `c_nonce` values issued by the token endpoint MUST be unique, unguessable, and single-use from the holder's perspective — a given `c_nonce` is valid only for one credential request.
- **FR-026**: The token endpoint MUST support refreshing the `c_nonce` via the separate nonce endpoint (FR-032) without issuing a new access token.
- **FR-027**: Failed code exchanges MUST return standard OAuth 2.0 error responses (`invalid_grant`, `invalid_request`, `unsupported_grant_type`) with no information leakage about which specific failure cause applies beyond what the HAIP profile requires.
- **FR-028**: The token endpoint MUST be rate limited. The rate limit policy uses the existing `RateLimitPolicies` pattern from `Sorcha.ServiceDefaults`; a new policy `HaipToken` is added with sensible defaults tighter than the general API policy.

**Nonce endpoint:**
- **FR-029**: The HAIP service MUST expose an anonymous `POST` nonce endpoint that accepts a valid access token and returns a fresh `c_nonce`.
- **FR-030**: Calling the nonce endpoint with an invalid or expired access token MUST fail with `invalid_token`.
- **FR-031**: The nonce endpoint MUST be rate limited using the same `HaipToken` policy as the token endpoint.
- **FR-032**: A fresh `c_nonce` from the nonce endpoint MUST invalidate any previously issued `c_nonce` for the same access token.

**Credential endpoint:**
- **FR-033**: The HAIP service MUST expose a `POST` credential endpoint that requires a valid access token in the `Authorization: Bearer` header.
- **FR-034**: The credential endpoint MUST accept a credential request containing the requested `format` (`vc+sd-jwt`), a `vct` identifying the credential type, and a `proof` object carrying a JWT proof of possession signed by the holder's key.
- **FR-035**: The JWT proof MUST be verified as follows: (a) the proof's header declares a key via `jwk`; (b) the proof's signature verifies against that key; (c) the proof's payload contains a `c_nonce` that matches a freshly issued one associated with the access token; (d) the proof's payload contains `aud` matching the HAIP service's `credential_issuer` URL; (e) the proof's payload contains `iat` within the acceptable clock skew window (±60 seconds).
- **FR-036**: If proof verification fails for any reason, the credential endpoint MUST return `invalid_proof` with a specific error description naming the failure cause (signature invalid, nonce mismatch, audience mismatch, clock skew, unsupported key type).
- **FR-037**: On successful proof verification, the credential endpoint MUST extract the holder public key from the proof and call the Wallet Service to sign an SD-JWT VC whose payload contains `cnf.jwk` set to the holder key.
- **FR-038**: The signed SD-JWT VC MUST carry the `x5c` header chain from spec 096 identifying the issuer organisation.
- **FR-039**: The signed SD-JWT VC MUST carry an IETF `status.status_list` claim from spec 095 pointing at the allocated status list index.
- **FR-040**: The signed SD-JWT VC MUST carry `vct` matching the credential type declared in the Credential Offer.
- **FR-041**: The signed SD-JWT VC MUST carry the mapped claims pre-computed at Credential Offer creation time (FR-017) — the credential endpoint does not remap at issuance time.
- **FR-042**: The credential endpoint MUST return a JSON response containing at minimum `credential` (the serialised SD-JWT VC) and optionally `c_nonce`, `c_nonce_expires_in` for batch reuse.
- **FR-043**: Once a credential has been successfully issued against a given pre-authorized code, subsequent calls to the credential endpoint with the same access token MUST fail with `invalid_request` to prevent duplicate issuance, unless the originating Blueprint action explicitly permits reissuance. Default is refuse.
- **FR-044**: The credential endpoint MUST be rate limited using a new `HaipCredential` policy tighter than `HaipToken`.

**Blueprint integration:**
- **FR-045**: The Blueprint Service's existing `CredentialIssuanceConfig` model MUST be extended with a `TargetAudience` field whose values are at minimum `SorchaInternal` (default, unchanged behaviour) and `HaipExternalWallet` (new, routes through the HAIP service).
- **FR-046**: When `TargetAudience: HaipExternalWallet`, the Blueprint action's execution MUST call the HAIP service's internal Credential Offer creation API and carry the returned `CredentialOfferUri` back through its action execution result.
- **FR-047**: When `TargetAudience: HaipExternalWallet`, the Blueprint action MUST NOT write a Sorcha-internal credential row; the credential is minted by the HAIP service on successful wallet callback, not pre-written.
- **FR-048**: When `TargetAudience: HaipExternalWallet`, the Blueprint action's `RecipientParticipantId` is treated as advisory (display/audit) only, not as a binding constraint — the real recipient is whoever scans the QR.
- **FR-049**: When `TargetAudience` is absent or equal to `SorchaInternal`, the existing internal issuance path runs unchanged.
- **FR-050**: Nested selective disclosure configuration (spec 094 FR-015) MUST be honoured for HAIP-path credentials identically to internal-path credentials.

**Cross-cutting:**
- **FR-051**: All new endpoints in `Sorcha.Haip.Service` MUST be covered by automated tests at unit and integration level, with end-to-end round-trip tests from Blueprint trigger through QR scan through credential issuance to credential verification.
- **FR-052**: The spec MUST not regress any acceptance scenario from specs 039, 093, 094, 095, or 096.
- **FR-053**: The HAIP service MUST be deployable via the existing `docker-compose.yml` and `Sorcha.AppHost` Aspire project, with no external dependencies beyond what the other seven services use.

### Key Entities *(include if feature involves data)*

- **Sorcha.Haip.Service** (new service): Eighth first-class Sorcha service. Hosts all OpenID4VCI issuer endpoints and (in spec 098) the OpenID4VP verifier endpoints. Holds no keys. Calls into Wallet Service for signing, Blueprint Service for offer lifecycle, Tenant Service for issuer enrolment state. Anonymous metadata and token endpoints; rate-limited everywhere.
- **Credential Offer record** (new, persisted or cached): Tracks an in-flight HAIP issuance. Contains the pre-authorized code, the originating Blueprint action identifier, the credential type, the pre-computed claims, the allocated status list index, the selected issuer co-key, the creation timestamp, the TTL, and lifecycle state (Pending, Exchanged, Issued, Expired, Cancelled).
- **Pre-authorized code** (new, short-lived token): A unique, unguessable one-time code carried in a Credential Offer URI. Used by the external wallet at the token endpoint to obtain an access token. TTL default 5 minutes.
- **Access token** (new, short-lived OAuth 2.0 Bearer token): Issued by the token endpoint in exchange for a pre-authorized code. Authorises one or more calls to the credential endpoint. TTL default 5 minutes. Not tied to a specific holder key; the holder key is bound at credential endpoint time via the JWT proof.
- **`c_nonce`** (new, short-lived anti-replay nonce): Issued alongside the access token or from the nonce endpoint. Bound into the JWT proof by the holder. Invalidated on use.
- **`CredentialIssuanceConfig.TargetAudience`** (extended on existing model): New field on the Blueprint Service's credential issuance config. Values `SorchaInternal` (default) or `HaipExternalWallet`. Controls which issuance path runs at action execution time.
- **`CredentialOfferUri`** (new, on action execution result): The HAIP-path action result surfaces this URI so UIs can render it as a QR code. Populated only when `TargetAudience: HaipExternalWallet`.
- **Issuer metadata document** (new, generated): HAIP 1.0-conformant JSON at `.well-known/openid-credential-issuer`. Derived from tenant configuration plus enrolled HAIP issuer organisations plus their declared credential types.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A generic HAIP-conformant wallet (open-source reference or equivalent) completes end-to-end issuance against a Sorcha deployment without any Sorcha-specific code: discover metadata, exchange pre-authorized code, submit JWT proof, receive signed SD-JWT VC, verify it.
- **SC-002**: The issued credential contains `cnf.jwk` matching the wallet's holder key, an `x5c` chain to the tenant root, an IETF `status.status_list` claim, and mapped claims from the originating Blueprint action, in 100 % of test cases.
- **SC-003**: Pre-authorized codes are strictly one-time in 100 % of test cases, confirmed by a replay test that attempts second exchange and confirms `invalid_grant`.
- **SC-004**: The token endpoint and credential endpoint together complete issuance in under 3 seconds at P95 for credentials with up to 10 mapped claims and 5 disclosable fields.
- **SC-005**: Metadata endpoints return valid HAIP 1.0 Section 5 documents that pass an independent HAIP metadata validator.
- **SC-006**: Blueprint authors can configure a credential to be issued via the HAIP path by adding a single `TargetAudience: HaipExternalWallet` field to their existing credential issuance config. No other Blueprint change is required.
- **SC-007**: `Sorcha.Haip.Service` deploys as an eighth service via `docker-compose up -d` without manual intervention.
- **SC-008**: Internal Sorcha credential issuance continues to work when `Sorcha.Haip.Service` is stopped.
- **SC-009**: Rate limits on the token endpoint and credential endpoint prevent brute-force attempts against pre-authorized codes and c_nonces under a sustained-attack load test.
- **SC-010**: No acceptance scenario from specs 039, 093, 094, 095, or 096 regresses after this spec ships.

## Out of Scope

The following are explicitly deferred to later specs or follow-up work:

- OpenID4VP verification endpoint — that is spec 098. This spec covers the issuer boundary only.
- Authorization code flow with browser redirect. HAIP 1.0 MTI for wallet scenarios is the pre-authorized code flow. The authorization code flow is a later extension.
- `tx_code` (user-presented transaction code) on pre-authorized code exchange. HAIP 1.0 allows but does not mandate it for wallet scenarios. Can be added in a follow-up operational spec.
- DPoP (Demonstration of Proof-of-Possession) for access tokens. The HAIP MTI path uses Bearer tokens; DPoP is a follow-up.
- mdoc format credential issuance. Deferred to a separate spec set.
- Credential refresh / re-issuance endpoints for expired credentials flowing to external wallets. Internal-path refresh (spec 039 FR-005) is unchanged; HAIP-path refresh is a follow-up.
- Deferred issuance (HAIP Deferred Credential Endpoint). The spec only implements synchronous issuance in the credential endpoint response.
- Batch credential issuance (multiple credentials in one response). One credential per credential endpoint call in this spec.
- Any wallet-side UX. Sorcha does not own the citizen's wallet; that is GOV.UK Wallet / EUDI Wallet / equivalent.
- Proof types other than JWT (e.g. CWT proofs). JWT only in this spec.
- Holder key types other than Ed25519, NIST P-256, and RSA (matching Sorcha classical set). Additional types are a follow-up.

## Assumptions

- Phase 2 D1 Option A (new eighth service) is the correct topology and has been confirmed by user ruling.
- The existing Sorcha infrastructure supports adding a new service to `docker-compose.yml`, `Sorcha.AppHost`, the API Gateway routing table, and the port configuration without structural changes. Based on Phase 1 file reading of `docker-compose`, `Sorcha.ApiGateway/appsettings.json`, and `src/Apps/Sorcha.AppHost/`.
- The Wallet Service can expose a service-to-service signing API that accepts a JWS signing input and returns a signature using a specific wallet's classical HAIP issuer co-key, without leaking the private key. Spec 094 work covers the co-key; this spec assumes the signing API exists at the service client layer.
- The Blueprint Service can be extended to call `Sorcha.Haip.Service` via the consolidated service client pattern in `Sorcha.ServiceClients`.
- HAIP 1.0's MTI for wallet-scoped issuance is the pre-authorized code grant. The authorization code flow with browser redirect is not required.
- HAIP 1.0 Section 5 metadata schema is stable and will not change between HAIP 1.0 and 1.1 beyond additive fields.
- `c_nonce` lifetime and access token lifetime defaults of 5 minutes each are acceptable for HAIP wallet interop. Both are configurable per deployment.
- The clock skew window for JWT proof `iat` can safely default to ±60 seconds, matching spec 094 FR-012 for KB-JWT clock skew.
- The Blueprint Service's existing `CredentialIssuanceConfig` model can be extended with a new `TargetAudience` field without breaking existing Blueprints. Based on Phase 1 reading of `Sorcha.Blueprint.Fluent/CredentialIssuanceBuilder.cs`.
- Credential Offer persistence can use the existing Blueprint Service storage layer or a new dedicated store in `Sorcha.Haip.Service`. The choice is a planning concern; this spec does not mandate one.
- External HAIP wallets used for testing will tolerate self-signed tenant root CAs added to their trust store, at least for the walkthrough scenarios.

## Dependencies

- **Hard dependency on spec 093** — verifier baseline correct.
- **Hard dependency on spec 094** — SD-JWT VC with `cnf`, nested disclosure, classical co-key.
- **Hard dependency on spec 095** — IETF `status.status_list` claim form for HAIP-path credentials.
- **Hard dependency on spec 096** — `x5c` chain in the outer JWS header, and the Tenant CA / Org Cert enrolment state.
- **Required by spec 098** — the HAIP verifier boundary will live in the same `Sorcha.Haip.Service` and share its infrastructure (metadata publishing, rate limits, API Gateway routing, Dockerfile).
- **Independent of specs 039, 050** except as amended.

## Amendment note on earlier specs

This spec does not supersede any earlier spec. It extends:

- `specs/039-verifiable-presentations` implicitly, by adding an external wallet path for credential delivery that did not previously exist. 039's internal wallet-card UI and internal presentation flow are unchanged.
- `specs/094-sdjwt-haip-hardening` (from the same spec set) by consuming its `cnf`, nested disclosure, and classical co-key capabilities as the credential mint substrate.
- `specs/095-ietf-token-status-list` by emitting HAIP-path credentials with the IETF claim form and thereby exercising the IETF endpoint introduced there.
- `specs/096-x509-org-trust` by emitting HAIP-path credentials with `x5c` chains drawn from its trust provider.

No earlier requirement is retired by this spec.
