# Feature Specification: OpenID4VP Verifier Endpoint (HAIP)

**Feature Branch**: `098-openid4vp-verifier`
**Created**: 2026-04-09
**Status**: Draft
**Input**: User description: "HAIP OpenID4VP verifier endpoint in Sorcha.Haip.Service: Authorization Request, presentation_definition, direct_post, vp_token verification with x5c and KB-JWT"

## Context

Phase 1 confirmed that Sorcha's existing "OID4VP" path is Sorcha-shaped, not wire-compatible with HAIP 1.0, and contains a signature-verification security bug now fixed by spec 093. The existing path (`/api/v1/presentations/*` in `Sorcha.Wallet.Service`) is useful for Sorcha-internal participants presenting Sorcha-internal credentials, but it is not an interoperability surface. A HAIP-conformant wallet (GOV.UK Wallet, EUDI Wallet, test harness) cannot today submit a presentation to Sorcha in a form Sorcha would accept.

This spec closes the gap by introducing a HAIP-shaped verifier boundary in the same `Sorcha.Haip.Service` that spec 097 stood up for issuance. The verifier speaks the OID4VP Authorization Request format on one side and plugs into Blueprint actions on the other. It reuses the `x5c` chain validation from spec 096, the `cnf`/KB-JWT verification from spec 094, the dual status list consumer from spec 095, and the fixed baseline verifier from spec 093. Every earlier spec in the 093–098 series is a contributor; this spec is the final composition.

The internal `/api/v1/presentations/*` path is preserved unchanged (modulo the 093 security fix). Sorcha-internal participants continue to use it; HAIP-external wallets use the new HAIP endpoints. Both paths exercise the same core verifier library so a credential that verifies on one path verifies identically on the other.

**Related specs.**
- **Hard dependency on spec 093** (`vc-security-fixes`) — the verifier baseline must be correct.
- **Hard dependency on spec 094** (`sdjwt-haip-hardening`) — `cnf`, KB-JWT verification, nested disclosure reconstruction.
- **Hard dependency on spec 095** (`ietf-token-status-list`) — the verifier consumes both W3C and IETF status list claim forms.
- **Hard dependency on spec 096** (`x509-org-trust`) — incoming credential `x5c` chains must be validated against the trust store.
- **Hard dependency on spec 097** (`openid4vci-issuer`) — `Sorcha.Haip.Service` exists, is deployed, and has rate-limiting, metadata publishing, and API Gateway routing in place. This spec adds verifier endpoints to that service rather than creating a new one.
- **Supersedes** the OID4VP story in `specs/039-verifiable-presentations` (US3, US4, and FR-013 through FR-018). Carries forward 039's remaining requirements or points at earlier specs in this series that already satisfy them.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A booking platform verifies a short-term-let licence from a citizen's GOV.UK Wallet (Priority: P1)

A short-term-let operator lists their property on a booking platform. The booking platform is running a Sorcha Blueprint workflow whose first action requires the operator to present a valid short-term-let licence credential. The booking platform's Sorcha UI shows the operator a QR code. The operator scans the QR with their GOV.UK Wallet, which fetches the Sorcha HAIP verifier's signed Authorization Request, displays a consent screen naming the booking platform and the specific licence fields requested (licence number, council area, expiry date), and on approval signs and posts back a `vp_token` via `direct_post`. Sorcha extracts the embedded SD-JWT VC, validates its `x5c` chain against the booking platform's trust store, verifies the KB-JWT against `cnf`, checks the credential status via the IETF `status.status_list` endpoint, matches the disclosed claims against the Blueprint's presentation definition, and hands the verified claim subset to the Blueprint action as its validated input. The action proceeds. The operator sees "Licence verified" on their wallet and "Property listed" on the booking platform — with zero Sorcha-specific knowledge on either side.

**Why this priority**: This is the end-to-end consumption payoff. Spec 097 gets credentials into HAIP wallets; spec 098 gets them back out for use in workflows. Together they make Sorcha the "workflow layer above GOV.UK Wallet" pitch demonstrable. It is also the story that every other User Story in the spec is a component of.

**Independent Test**: Run a Blueprint whose first action requires a specific credential type. The Blueprint action produces a presentation request identifier, which the Sorcha UI renders as a QR. Scan the QR with a HAIP-conformant test wallet holding a matching credential. Confirm the wallet completes the `direct_post` flow, the verifier accepts the presentation, and the Blueprint action resumes with the verified claim subset as its input. Confirm the verifier would reject a replay of the same `vp_token` with a different nonce.

**Acceptance Scenarios**:

1. **Given** a Blueprint action requiring a specific credential type at entry, **When** the action fires, **Then** the Blueprint Service calls the HAIP verifier to create a Presentation Request and receives an Authorization Request URI in return.
2. **Given** an Authorization Request URI, **When** it is rendered as a QR and scanned by a HAIP-conformant wallet, **Then** the wallet fetches the signed Request Object from the `request_uri`, validates its signature, displays a consent screen listing the requested claims, and captures user approval.
3. **Given** a user approves the presentation, **When** the wallet posts `vp_token` and `presentation_submission` via `direct_post` to the verifier's callback URL, **Then** the verifier extracts the SD-JWT VC from `vp_token`, validates its `x5c` chain, verifies the KB-JWT against `cnf`, checks the credential status, and matches the disclosed claims against the `presentation_definition`.
4. **Given** a successful verification, **When** the verifier returns the result, **Then** the result contains the verified claim subset and a verification outcome of "Verified", and the originating Blueprint action resumes with the verified claims as its validated input.
5. **Given** a successful presentation, **When** the same `vp_token` is replayed with a different nonce, **Then** the verifier rejects it with a KB-JWT nonce mismatch error because the KB-JWT binds to the original nonce.
6. **Given** the same presentation, **When** replayed to a different verifier audience, **Then** verification fails with a KB-JWT audience mismatch error.
7. **Given** a Blueprint action whose required credential type matches multiple wallet credentials, **When** the wallet displays the consent screen, **Then** the user can choose which credential to present and the verifier accepts any match.

---

### User Story 2 - A Blueprint author declares "this action requires a credential" and the HAIP verifier flow happens automatically (Priority: P1)

A Blueprint author writes an action that must not run until the invoking participant has presented a specific credential type from an accepted issuer. They configure the existing credential requirement block on the action (from spec 031 / 094 FR-035): credential type, accepted issuers, required claims. They add one new field: `PresentationSource` = `HaipExternalWallet` (or similar) to say the presentation must come via a HAIP external wallet rather than from a Sorcha-internal participant's stored credential. When the action fires at runtime, the Blueprint Service calls the HAIP verifier to mint an Authorization Request, returns the Authorization Request URI up the execution chain for the UI to render, and suspends the action pending a verification result. When the verification result arrives, the action resumes. The Blueprint author never touches OID4VP wire details.

**Why this priority**: Without this, every Blueprint author would have to become a HAIP expert. The ergonomics contract mirrors the issuance-side contract from spec 097 FR-045.

**Independent Test**: Take an existing Blueprint template whose action requires a credential via the internal path. Add `PresentationSource: HaipExternalWallet`. Run the Blueprint. Confirm the action produces a Presentation Request URI in its execution state rather than matching against a Sorcha-internal credential row. Confirm the action remains in a suspended state until a HAIP verification result arrives, and resumes correctly when it does.

**Acceptance Scenarios**:

1. **Given** a Blueprint action with a credential requirement and `PresentationSource: HaipExternalWallet`, **When** the action fires, **Then** the Blueprint Service calls Sorcha.Haip.Service to create a Presentation Request and the action transitions to a new `AwaitingExternalPresentation` state.
2. **Given** an action in `AwaitingExternalPresentation` state, **When** its execution result is inspected, **Then** the result carries a `PresentationRequestUri` that the Sorcha UI can render as a QR.
3. **Given** an action in `AwaitingExternalPresentation` state, **When** a successful HAIP verification result arrives via the verifier callback, **Then** the action transitions to `Executing` with the verified claims as its input and proceeds normally.
4. **Given** an action in `AwaitingExternalPresentation` state, **When** the Presentation Request TTL elapses without a wallet submission, **Then** the action transitions to a failure state with a clear "presentation request timed out" error.
5. **Given** a Blueprint action without `PresentationSource` (or with `PresentationSource: SorchaInternal`), **When** the action fires, **Then** the existing internal credential matching path runs unchanged — no HAIP Presentation Request is created.
6. **Given** a Blueprint action with a credential requirement specifying nested required claims (using JSON Pointer paths per spec 094), **When** the HAIP presentation is submitted and verified, **Then** the nested claims are correctly reconstructed and matched against the requirement.

---

### User Story 3 - The verifier publishes a HAIP-shaped Authorization Request that any wallet can consume (Priority: P1)

A caller asks the HAIP verifier to create a Presentation Request. The verifier generates a HAIP-conformant Authorization Request: a signed JWT Request Object served at a stable `request_uri`, whose payload contains a `client_id` identifying the verifier, a `nonce`, a `response_mode: direct_post`, a `response_uri` pointing at the verifier's callback, a `presentation_definition` conforming to DIF Presentation Exchange 2.0, and HAIP-required metadata. The `request_uri` and a compact deep-link form are both returned to the caller so either a QR-scan or a same-device deep link can start the flow.

**Why this priority**: This is the wire-level conformance story for OID4VP on the verifier side. Without a HAIP-shaped Authorization Request, no HAIP wallet will recognise Sorcha as a valid verifier. Independent conformance testing hangs off this story.

**Independent Test**: Create a Presentation Request via the internal service-to-service API. Fetch the `request_uri`. Validate the returned Request Object against the HAIP 1.0 Section 6 schema in an independent validator. Confirm the `presentation_definition` passes DIF PE 2.0 validation. Confirm the deep-link form parses with any HAIP-conformant client.

**Acceptance Scenarios**:

1. **Given** a Presentation Request creation call specifying a credential type, accepted issuers, required claims, and a callback URL, **When** the verifier processes it, **Then** it mints a signed Request Object, stores it with an associated request identifier and nonce, and returns a `request_uri` plus a deep-link form.
2. **Given** an issued Request Object, **When** a wallet fetches the `request_uri`, **Then** the response is a signed JWT whose header identifies the signing algorithm, whose payload contains `client_id`, `nonce`, `response_mode: direct_post`, `response_uri`, `presentation_definition`, `state`, and `aud: https://self-issued.me/v2`, and whose signature verifies against the HAIP verifier's signing key bound to the same `x5c` trust chain used for credential issuance.
3. **Given** the Request Object's `presentation_definition`, **When** validated against DIF Presentation Exchange 2.0, **Then** it parses without errors, contains at least one `input_descriptor` naming the required credential type, and declares field constraints via JSON Path for the required claims.
4. **Given** an issued Request Object, **When** a wallet follows the deep-link form instead of the `request_uri`, **Then** the same Request Object is consumed equivalently — both forms yield the same wallet behaviour.
5. **Given** a Presentation Request whose TTL has elapsed, **When** a wallet attempts to fetch the `request_uri`, **Then** the response is 410 Gone with an expiry explanation.
6. **Given** a `presentation_definition` naming nested claim constraints via JSON Path, **When** a wallet parses it and looks up matching credentials, **Then** the wallet finds credentials whose nested fields are declared disclosable per spec 094.

---

### User Story 4 - The `direct_post` callback verifies the submitted `vp_token` end-to-end (Priority: P1)

A HAIP wallet completes the consent dialog and posts `vp_token` and `presentation_submission` to the verifier's callback URL via HTTPS `direct_post`. The verifier parses the submission, extracts the SD-JWT VC, walks its `x5c` chain to a trusted root, verifies the issuer signature, verifies the KB-JWT against `cnf` (checking audience matches the verifier's own client identifier, nonce matches the Request Object's nonce, `iat` is within the clock skew window, `sd_hash` matches the presentation), checks the credential status via whichever claim form it carries (W3C or IETF), matches the disclosed claims against the `presentation_definition` input descriptors, and records a verification result bound to the Presentation Request identifier.

**Why this priority**: This is the core verification path. It touches every other spec in the series — 093 baseline, 094 KB-JWT, 095 status, 096 x5c. If any of those integrations is wrong, it shows up here first. It is also the single most security-critical endpoint in the HAIP spec set.

**Independent Test**: Construct a valid HAIP presentation carrying a Sorcha-issued credential. Post it to the verifier's `direct_post` callback. Confirm the verifier accepts it, records a Verified result, and returns a success response. Construct negative cases for each verification step (bad `x5c` chain, bad KB-JWT signature, wrong audience, wrong nonce, wrong `sd_hash`, revoked credential via status list, missing required claim, wrong claim value) and confirm each fails with a specific error identifying the failing check.

**Acceptance Scenarios**:

1. **Given** an active Presentation Request with a known nonce, **When** a wallet posts `vp_token` and `presentation_submission` via `direct_post`, **Then** the verifier parses the submission, extracts the SD-JWT VC embedded in `vp_token`, walks its `x5c` chain to a trust store root, verifies the issuer signature, and proceeds to KB-JWT verification.
2. **Given** a presentation whose credential's `x5c` chain does not terminate in the verifier's trust store, **When** the verifier processes it, **Then** verification fails with a trust anchor error and the Presentation Request transitions to Denied.
3. **Given** a presentation whose credential's issuer signature does not verify against the leaf cert's Subject Public Key Info, **When** the verifier processes it, **Then** verification fails with an issuer signature error.
4. **Given** a presentation whose KB-JWT `aud` does not match the verifier's `client_id`, **When** the verifier processes it, **Then** verification fails with a KB-JWT audience mismatch error per spec 094 FR-012.
5. **Given** a presentation whose KB-JWT `nonce` does not match the Presentation Request's nonce, **When** the verifier processes it, **Then** verification fails with a KB-JWT nonce mismatch error.
6. **Given** a presentation whose credential carries an IETF `status.status_list` claim pointing at a revoked bit, **When** the verifier checks status, **Then** verification fails with a credential revoked error per spec 095 FR-011.
7. **Given** a presentation whose credential carries a W3C `credentialStatus` claim pointing at a revoked bit, **When** the verifier checks status, **Then** verification fails identically — the verifier handles both claim forms per spec 095 FR-014.
8. **Given** a presentation whose `presentation_submission` references an input descriptor that is not matched by the disclosed claims, **When** the verifier processes it, **Then** verification fails with an input descriptor match error naming the unmatched descriptor.
9. **Given** a presentation whose disclosed claims satisfy all input descriptors, **When** verification succeeds, **Then** the Presentation Request transitions to Verified and the verification result carries the full disclosed claim subset indexed by input descriptor name.
10. **Given** a successful verification, **When** a caller queries the Presentation Request by its identifier, **Then** the verification result is returned including the verified claims, the issuer identity (from the X.509 chain), the timestamp of verification, and the verifier's own signed attestation that verification succeeded.
11. **Given** a `direct_post` submission to a Presentation Request that has already been fulfilled, **When** the verifier processes it, **Then** the submission is rejected with a "request already fulfilled" error and the existing verification result is not overwritten.

---

### User Story 5 - A Blueprint action polls for verification outcome and resumes when the wallet completes the flow (Priority: P2)

A Blueprint action in `AwaitingExternalPresentation` state polls the HAIP verifier for the outcome of its Presentation Request at a reasonable interval, or subscribes to a signal that the verifier emits on status transitions. When the outcome becomes available (Verified, Denied, Expired, Cancelled), the action resumes in the corresponding branch of its workflow. The polling behaviour, the signalling behaviour, and the timeout configuration are all driven by existing Sorcha workflow infrastructure rather than new HAIP-specific plumbing.

**Why this priority**: This is the integration story between the HAIP verifier and the Blueprint execution engine. Without it, verifier outcomes would reach verifier state but never flow back into Blueprint execution, leaving actions stuck forever. It is "how the verifier talks to the engine" and needs to be specified even though most of the implementation lives in the existing SignalR / polling infrastructure.

**Independent Test**: Start a Blueprint action that transitions to `AwaitingExternalPresentation`. Submit a verified presentation via the callback endpoint. Confirm the action transitions to `Executing` with the verified claims within a reasonable delay (polling interval or signal delivery). Separately, start another action and let its Presentation Request expire without submission. Confirm the action transitions to failure with a timeout error.

**Acceptance Scenarios**:

1. **Given** a Blueprint action in `AwaitingExternalPresentation` state, **When** a verification result is recorded against its Presentation Request identifier, **Then** the action's execution engine observes the new outcome (via polling or signal) and resumes the action with the verified claims as its input.
2. **Given** a Blueprint action in `AwaitingExternalPresentation` state, **When** the Presentation Request's TTL elapses without a result, **Then** the action's execution engine observes the Expired state and resumes the action in a failure branch with a timeout error.
3. **Given** a Blueprint action in `AwaitingExternalPresentation` state, **When** the Presentation Request transitions to Denied because the submitted presentation failed verification, **Then** the action resumes in a failure branch carrying the verification failure cause (trust anchor, KB-JWT, status, claim match).
4. **Given** the existing SignalR ActionsHub infrastructure, **When** a verification result is recorded, **Then** a thin signal (matching spec 089's minimal-disclosure policy) is emitted on the hub so UI clients can refresh without polling.

---

### User Story 6 - Same-device deep link and cross-device QR both work (Priority: P2)

Two distinct user flows are supported. **Cross-device**: a verifier terminal (a booking platform's web page, a gate at a venue, a pharmacy counter) displays a QR code containing the Authorization Request URI. The user scans it with their wallet on their phone. The wallet processes the request, shows consent, and posts back via `direct_post` over HTTPS. The verifier terminal sees the outcome asynchronously and updates its UI. **Same-device**: the user is already on their phone interacting with a Sorcha-driven UI in a mobile browser. The UI invokes a deep link into the wallet (typically a custom scheme or universal link). The wallet opens with the Authorization Request already loaded, shows consent, and posts back. The browser UI receives the outcome and continues.

**Why this priority**: HAIP 1.0 mandates support for both flows. The cross-device flow is what bootstraps demos and the same-device flow is what matures into a production mobile experience. Both share the same Authorization Request backend — the difference is purely in how the wallet is invoked.

**Independent Test**: Generate a Presentation Request. Render its URI as a QR code and scan it with a wallet on a separate device — confirm the cross-device flow completes. Generate a second Presentation Request. Render its URI as a clickable same-device deep link and tap it from a mobile browser — confirm the wallet opens and the same-device flow completes.

**Acceptance Scenarios**:

1. **Given** a Presentation Request, **When** its deep-link form is rendered as a QR code and scanned by a wallet on a separate device, **Then** the wallet fetches the Request Object, processes it, and posts back successfully.
2. **Given** the same Presentation Request, **When** its deep-link form is rendered as a tap-to-open link and tapped on the same mobile device, **Then** the device's wallet app launches with the Request Object pre-loaded and the same-device flow completes successfully.
3. **Given** a wallet that does not support the deep-link scheme (for example, no HAIP wallet is installed on the device), **When** the user taps the link, **Then** the behaviour is a graceful fallback per the device's URL handler — this is out of Sorcha's control but the spec does not require anything Sorcha-specific to break.
4. **Given** a HAIP wallet invoked via same-device deep link, **When** the wallet posts back via `direct_post`, **Then** the response reaches the verifier via HTTPS exactly as in the cross-device flow — the response path is identical regardless of how the wallet was invoked.

---

### Edge Cases

- What happens when a wallet posts to the `direct_post` callback twice for the same Presentation Request? The first submission wins. The second receives a "request already fulfilled" error. Neither overwrites nor extends the existing verification result.
- What happens when a Presentation Request is cancelled by the originating Blueprint action before the wallet submits? The request transitions to Cancelled and subsequent wallet submissions are rejected with "request cancelled".
- What happens when the wallet submits a valid `vp_token` whose embedded credential is expired (not revoked, just past `exp`)? Verification fails with a credential-expired error via the existing SD-JWT VC `exp` check.
- What happens when the `presentation_definition` declares multiple input descriptors and the wallet satisfies some but not all? The submission fails — all input descriptors must be satisfied per DIF PE 2.0 semantics.
- What happens when the `presentation_definition` allows submission of a credential from any of several accepted issuers and the wallet picks one that is subsequently revoked at the moment of verification? The verifier checks status at submission time, sees the revocation, and fails the verification. This is a race window but it is acceptable — the wallet cannot have done anything wrong by choosing a then-valid credential.
- What happens when the wallet submits `vp_token` using a DID-based issuer identifier (for example, a credential from another Sorcha deployment) rather than an X.509 chain? The verifier falls back to DID resolution using the spec 093 / spec 039 trust path. This is a valid mode for Sorcha-to-Sorcha interop and the verifier must support both trust paths.
- What happens when the `x5c` chain in the presented credential is valid but rooted in a tenant root the verifier does not trust? Verification fails with a trust anchor error and a clear message naming the unknown root.
- What happens when the wallet's `direct_post` submission arrives at the verifier over an unexpectedly long network delay, past the Presentation Request TTL? The request has already transitioned to Expired by the time the submission arrives and the submission is rejected with "request expired".
- What happens when the Blueprint action's credential requirement names a JSON Pointer path that does not correspond to any `input_descriptor` field constraint the `presentation_definition` generator knows how to emit? The Presentation Request creation call fails with a clear error before any URI is minted. Callers cannot ship Presentation Requests whose definitions the verifier cannot later match against.
- What happens when a wallet bundles multiple credentials into one `vp_token` (batch presentation)? HAIP 1.0 permits this. The verifier handles each sub-credential in turn and the verification result carries the verified subset per input descriptor.
- What happens when the verifier's own signing key (used to sign Request Objects) is rotated between Request Object creation and wallet fetch? The wallet trusts whichever key is live at fetch time. A rotation during a Request Object's TTL is rare and recoverable — the wallet refuses and the caller retries.
- What happens when a malicious actor replays a captured valid presentation to the verifier's callback? Since `nonce` and `aud` are bound into the KB-JWT and the request is marked fulfilled on first success, the replay fails with either a nonce mismatch (if the attacker mints a new Presentation Request) or a "request already fulfilled" error (if the attacker targets the same Presentation Request).

## Requirements *(mandatory)*

### Functional Requirements

**Verifier service location and integration:**
- **FR-001**: The HAIP verifier endpoints MUST be hosted in `Sorcha.Haip.Service`, alongside the OpenID4VCI issuer endpoints introduced by spec 097. This spec does not create a new service.
- **FR-002**: The HAIP verifier MUST reuse spec 097's deployment topology, rate-limiting framework, metadata publishing infrastructure, and API Gateway routing. New endpoints are added to the same Dockerfile, the same port assignment, and the same health check.
- **FR-003**: The HAIP verifier MUST reuse the core verifier library that the existing Sorcha-internal `/api/v1/presentations/*` path uses (as fixed by spec 093), so that a credential that verifies on one path verifies identically on the other. The difference between the two paths is purely in the outer wire protocol, not in the credential verification logic.

**Authorization Request generation:**
- **FR-004**: The HAIP verifier MUST expose an internal service-to-service API for creating a Presentation Request. The creation call MUST accept at minimum: the required credential type, accepted issuers, required claims (expressed as claim names or JSON Pointer paths per spec 094), a callback URL, a verifier identity, and a TTL.
- **FR-005**: The creation call MUST produce a signed Authorization Request Object conforming to OID4VP 1.0 with the HAIP 1.0 profile applied.
- **FR-006**: The Request Object MUST be served at a stable HTTPS URL (`request_uri`) and MUST also be expressible as a compact deep-link form suitable for same-device invocation.
- **FR-007**: The Request Object's payload MUST contain at minimum `client_id` (the verifier's HAIP identifier), `nonce` (unique per request, unguessable), `response_mode: direct_post`, `response_uri` (the verifier's callback URL), `presentation_definition`, `state`, and `aud: https://self-issued.me/v2`.
- **FR-008**: The Request Object MUST be signed by the HAIP verifier's classical signing key, which MUST be bound to the same `x5c` trust chain used for credential issuance (spec 096 FR-017). A wallet can therefore trust the Request Object using the same trust store it uses for credentials.
- **FR-009**: The `presentation_definition` MUST conform to DIF Presentation Exchange 2.0 and MUST encode field constraints via JSON Path over the credential claims.
- **FR-010**: `presentation_definition` input descriptors MUST support both top-level name-keyed claim constraints and nested JSON-Pointer-style claim constraints, matching the disclosure shapes supported by spec 094.
- **FR-011**: Presentation Requests MUST have a configurable TTL (default 5 minutes) and MUST transition to `Expired` when the TTL elapses without a submission.
- **FR-012**: A Presentation Request that has transitioned to `Verified`, `Denied`, `Expired`, or `Cancelled` MUST NOT be re-fulfilled. Subsequent `direct_post` submissions MUST be rejected with a terminal-state error.

**`direct_post` callback endpoint:**
- **FR-013**: The HAIP verifier MUST expose a public HTTPS `POST` endpoint (anonymous, rate-limited) that accepts `direct_post` submissions containing `vp_token`, `presentation_submission`, and `state`.
- **FR-014**: The callback endpoint MUST match the `state` to an active Presentation Request. If no match, the submission is rejected with `invalid_request` and no verification is performed.
- **FR-015**: The callback endpoint MUST extract the SD-JWT VC (or batch of SD-JWT VCs) from `vp_token` and pass each to the core verifier library for full verification.
- **FR-016**: The callback endpoint MUST be rate-limited using a new `HaipVerifier` policy added to the existing `RateLimitPolicies` pattern from `Sorcha.ServiceDefaults`.

**Credential verification pipeline:**
- **FR-017**: For each presented credential, the verifier MUST walk the outer JWS header's `x5c` chain and validate it against the configured trust store per spec 096 FR-026.
- **FR-018**: If the `x5c` chain is absent and the credential's `iss` claim resolves to a DID the verifier trusts, the verifier MUST fall back to the DID-based trust path from spec 039 FR-019 through FR-023. Both trust paths are valid; the verifier picks whichever the credential supplies.
- **FR-019**: The verifier MUST verify the SD-JWT VC issuer signature against the leaf cert's Subject Public Key Info (X.509 path) or the DID document's verification method (DID path), whichever trust path is in use.
- **FR-020**: The verifier MUST verify the Key Binding JWT against the credential's `cnf.jwk` per spec 094 FR-012. The KB-JWT's `aud` MUST match the verifier's `client_id`, its `nonce` MUST match the Presentation Request's nonce, its `iat` MUST be within the acceptable clock skew window, and its `sd_hash` MUST match a SHA-256 of the preceding portion of the serialised presentation.
- **FR-021**: The verifier MUST check credential status via whichever claim form the credential carries (W3C `credentialStatus` or IETF `status.status_list`) per spec 095 FR-010 through FR-015. Legacy credentials without either claim form fall back to the server-side row path per spec 093 FR-010.
- **FR-022**: The verifier MUST match the disclosed claims against every input descriptor in the `presentation_definition` and fail the submission if any input descriptor is unmatched.
- **FR-023**: The verifier MUST honour field constraints inside each input descriptor (required, predicate, filter) per DIF PE 2.0 semantics.
- **FR-024**: If every verification check passes, the verifier MUST record a `Verified` result bound to the Presentation Request identifier. The result MUST carry: the verified claim subset indexed by input descriptor, the issuer identity (from the X.509 chain or the DID document), the verification timestamp, and a verifier-signed attestation that verification succeeded.
- **FR-025**: If any verification check fails, the verifier MUST record a `Denied` result with a specific error identifying which check failed (trust anchor, issuer signature, KB-JWT audience / nonce / clock skew / sd_hash / signature, credential status, input descriptor mismatch, field constraint failure).
- **FR-026**: The verifier MUST NOT leak disclosed claim values into the `Denied` result — the failure reason is opaque to preserve holder privacy against a hostile caller who is probing the verifier.

**Blueprint integration:**
- **FR-027**: The Blueprint Service's existing credential requirement model on actions MUST be extended with a `PresentationSource` field whose values are at minimum `SorchaInternal` (default, unchanged behaviour) and `HaipExternalWallet` (new, routes through the HAIP verifier).
- **FR-028**: When `PresentationSource: HaipExternalWallet`, the Blueprint action MUST call the HAIP verifier's internal Presentation Request creation API at execution time and suspend the action in a new `AwaitingExternalPresentation` state.
- **FR-029**: A Blueprint action in `AwaitingExternalPresentation` state MUST surface the Presentation Request URI in its execution result so the Sorcha UI can render it as a QR or a deep link.
- **FR-030**: A Blueprint action in `AwaitingExternalPresentation` state MUST resume when a verification result is recorded against its Presentation Request. The action engine MUST observe the result transition primarily via a SignalR signal on the existing ActionsHub (per spec 089's minimal-disclosure policy) emitted by the HAIP verifier on result transitions, with periodic polling retained as a fallback for when SignalR delivery is unavailable. The signal carries only the Presentation Request identifier and the new status; the action engine pulls full details via an authenticated call. See Clarifications Q6.1 ruling.
- **FR-031**: A Blueprint action in `AwaitingExternalPresentation` state MUST transition to a failure branch when its Presentation Request transitions to `Expired`, `Denied`, or `Cancelled`, with the failure cause propagated to the action's failure branch input.
- **FR-032**: When the Blueprint action transitions to `Executing` after a successful HAIP verification, the verified claim subset MUST be available to the action as input, structured the same way as internal-path credential claims would be.
- **FR-033**: When `PresentationSource` is absent or equal to `SorchaInternal`, the existing internal credential matching path runs unchanged.
- **FR-034**: A Presentation Request created by a Blueprint action MUST carry the originating action identifier so verification results can be routed back to the correct action instance.

**Parallel-path preservation:**
- **FR-035**: The existing Sorcha-internal presentation endpoint (`/api/v1/presentations/*` in `Sorcha.Wallet.Service`, as fixed by spec 093) MUST remain fully functional for Sorcha-internal participants presenting Sorcha-internal credentials. This spec does not touch that endpoint.
- **FR-036**: A credential that verifies on the internal path MUST verify identically on the HAIP path (given the same input) because both paths share the same core verifier library. Confirmed via a parity regression test.
- **FR-037**: A credential issued via the HAIP path (spec 097) MUST be verifiable via the internal path as well (Sorcha-internal verifier can also read `x5c` chains and KB-JWTs), so a HAIP-issued credential is not second-class in Sorcha-internal workflows.

**Cross-cutting:**
- **FR-038**: All new endpoints in `Sorcha.Haip.Service` for verification MUST be covered by automated tests at unit and integration level, with end-to-end round-trip tests from Blueprint trigger through QR scan through wallet submission to Blueprint resume.
- **FR-039**: The spec MUST not regress any acceptance scenario from specs 039, 093, 094, 095, 096, or 097.
- **FR-040**: The spec MUST NOT change the wire format of any existing endpoint outside of `Sorcha.Haip.Service`. Only the Blueprint Service's credential requirement model gains the new `PresentationSource` field, consistent with spec 097's `TargetAudience` addition.

### Key Entities *(include if feature involves data)*

- **Presentation Request** (new, persisted in `Sorcha.Haip.Service`): Tracks an in-flight HAIP verification. Contains the signed Request Object, the nonce, the `presentation_definition`, the callback URL, the originating Blueprint action identifier, the verifier's own client identifier, the TTL, and the lifecycle state (Pending, Submitted, Verified, Denied, Expired, Cancelled).
- **Authorization Request Object** (new, signed JWT): The HAIP-shaped OID4VP Authorization Request. Payload contains `client_id`, `nonce`, `response_mode`, `response_uri`, `presentation_definition`, `state`, `aud`. Signed by the verifier's classical signing key, bound to the same `x5c` chain used for credential issuance.
- **`presentation_definition`** (new, embedded in Request Object): A DIF Presentation Exchange 2.0 document naming the required credential type, issuer constraints, and field constraints via JSON Path.
- **Verification Result** (new, persisted): The outcome of verification. Contains the verified claim subset (only on Verified), the issuer identity, the verification timestamp, the failure cause (only on Denied), and a verifier-signed attestation.
- **`PresentationSource`** (extended on existing Blueprint credential requirement model): New field. Values `SorchaInternal` (default, unchanged) or `HaipExternalWallet` (new). Drives which presentation path the action uses.
- **`AwaitingExternalPresentation`** (new Blueprint action state): Transient state for actions suspended pending a HAIP verifier result. Resumes to `Executing` on Verified, to the failure branch on Denied / Expired / Cancelled.
- **`PresentationRequestUri`** (new, on action execution result): The HAIP-path action surfaces this URI so UIs can render it as a QR or deep link. Populated only when `PresentationSource: HaipExternalWallet` and the action is in `AwaitingExternalPresentation` state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A generic HAIP-conformant wallet (open-source reference implementation or equivalent) completes end-to-end presentation against a Sorcha Blueprint with no Sorcha-specific code on the wallet side: fetch metadata, fetch Request Object, display consent, post `vp_token` via `direct_post`, receive verifier acknowledgement.
- **SC-002**: The verifier correctly accepts valid presentations and rejects invalid ones, in 100 % of test cases across every verification step (`x5c`, issuer signature, KB-JWT audience / nonce / clock / sd_hash, credential status, input descriptor match, field constraint).
- **SC-003**: A Blueprint action configured with `PresentationSource: HaipExternalWallet` resumes correctly on successful verification within 2 seconds of the verification result being recorded (subject to the observation mechanism — polling or signal).
- **SC-004**: A Blueprint action configured with `PresentationSource: HaipExternalWallet` transitions cleanly to its failure branch on presentation timeout, denial, or cancellation.
- **SC-005**: The HAIP verifier's Authorization Request Object passes an independent HAIP 1.0 Section 6 validator without errors.
- **SC-006**: The verifier's `presentation_definition` passes DIF Presentation Exchange 2.0 validation in an independent validator.
- **SC-007**: Both cross-device QR and same-device deep-link flows succeed in end-to-end tests using a HAIP-conformant wallet.
- **SC-008**: A credential issued via the HAIP path (spec 097) is verifiable via both the HAIP verifier path and the internal verifier path, producing identical verification outcomes, confirmed by a parity regression test.
- **SC-009**: A credential issued via the internal path is verifiable via the internal path (unchanged) and also via the HAIP verifier path (if it happens to be presented that way), producing identical outcomes.
- **SC-010**: Replay of a captured valid presentation fails in 100 % of test cases (either KB-JWT nonce mismatch or "request already fulfilled" error, depending on whether the attacker mints a new Presentation Request or targets the original).
- **SC-011**: No acceptance scenario from specs 039, 093, 094, 095, 096, or 097 regresses after this spec ships.

## Out of Scope

The following are explicitly deferred or handled by earlier specs in the series:

- Credential lifecycle management (Active, Suspended, Revoked, Expired, Consumed). Handled by spec 039 (still in force) and spec 093 (fixes to the existing behaviour).
- Status list publishing. Handled by spec 039 (W3C, in force) and spec 095 (IETF, added).
- DID resolution machinery (`did:sorcha`, `did:web`, `did:key`). Handled by spec 039 (FR-019 through FR-023, still in force). The verifier falls back to DID-based trust where no `x5c` chain is present.
- Credential holder-side UI (wallet card display, presentation inbox, consent dialog). For Sorcha-internal participants, handled by spec 039 (US6, US7, still in force) and the existing Sorcha.UI. For HAIP external participants, the UI is the external HAIP wallet's own UI — Sorcha does not own it.
- Cross-blueprint credential flows. Handled by spec 039 (US8, still in force) — credentials from one blueprint feeding into another via the existing internal path. The HAIP path naturally composes the same way.
- OpenID4VCI issuance. Handled by spec 097.
- mdoc presentation. Deferred beyond the current spec set.
- Response modes other than `direct_post`. HAIP 1.0 MTI is `direct_post`; other modes (`fragment`, `direct_post.jwt`) are deferred.
- Pre-registered verifier clients. Sorcha's HAIP verifier uses the `x5c` chain on the signed Request Object for wallet-side verifier identification, not a pre-registration step.
- Signed metadata for the verifier at a `.well-known/` URL. Spec 097 already publishes issuer metadata; verifier metadata is a smaller additive concern and may land in this spec or a follow-up operational spec at planner discretion.
- `client_id_scheme` variants other than `x509_san_uri` (the scheme HAIP mandates when X.509 is in use). Other schemes are a future concern if the deployment landscape demands them.
- Deferred or batch verification. Single synchronous verification per `direct_post` call.
- Authorization code flow for OID4VP. HAIP 1.0 does not require one for the verifier path; the wallet posts directly.
- DPoP on the verifier path. Bearer-style trust via the signed Request Object and the KB-JWT is sufficient for HAIP 1.0 MTI.

## Assumptions

- Phase 2 D1 Option A is confirmed: `Sorcha.Haip.Service` exists and is the correct location for this spec's endpoints. Spec 097 has already created it.
- The Blueprint Service's existing action execution engine supports suspend/resume semantics sufficient to accommodate a new `AwaitingExternalPresentation` state. The existing pause-and-resume infrastructure for long-running actions is usable for this purpose without a structural rewrite. Based on Phase 1 reading of `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`.
- The existing SignalR ActionsHub (per spec 089) is usable for emitting verification outcome signals to UI clients without introducing new hub infrastructure.
- DIF Presentation Exchange 2.0 is stable and the HAIP 1.0 profile of OID4VP 1.0 is stable enough to target now. Both are mandated by HAIP 1.0; this is not a speculative target.
- `Sorcha.Haip.Service`'s signing identity for Request Objects is the same classical HAIP signing key the issuer side uses — one org identity, two roles (issuer and verifier) on the same wallet and the same `x5c` chain. A deployment that wants a different verifier identity can enrol a second org wallet, but the default is single-identity.
- `client_id_scheme: x509_san_uri` is the correct HAIP scheme when the verifier's Request Object is signed with an X.509 chain whose leaf carries the client identifier in a SAN URI. This is the standard HAIP profile.
- Verified claim subsets can be persisted and retrieved via the existing workflow storage layer used for action state. No new storage backend is required for verification results.
- A default Presentation Request TTL of 5 minutes is acceptable. Configurable per deployment.
- The clock skew window for KB-JWT `iat` continues to default to ±60 seconds, matching spec 094 FR-012.
- The existing `RevocationCheckPolicy` pattern from the status list consumer applies to credential status checks on this path as well (fail-closed default, fail-open-with-warning configurable).

## Clarifications

One architectural question arose during drafting and has been resolved by user ruling. Retained here for traceability.

### Q6.1 — How does a Blueprint action observe the verification outcome?

**Ruling: Option C (SignalR signal with polling fallback).** The HAIP verifier emits a thin signal on the existing ActionsHub per spec 089's minimal-disclosure policy when a Presentation Request transitions to a terminal state (`Verified`, `Denied`, `Expired`, `Cancelled`). The Blueprint action execution engine subscribes to the hub and resumes the action on signal. Periodic polling remains as a fallback for when SignalR delivery is unavailable (network partition, client disconnect, hub restart). Rationale: loose coupling between `Sorcha.Haip.Service` and the Blueprint Service, reuse of existing SignalR infrastructure, matches the established Sorcha pattern for real-time coordination. Reflected in FR-030.

## Dependencies

- **Hard dependency on spec 093** (`vc-security-fixes`) — the core verifier library must actually verify.
- **Hard dependency on spec 094** (`sdjwt-haip-hardening`) — KB-JWT verification, `cnf`, nested disclosure reconstruction.
- **Hard dependency on spec 095** (`ietf-token-status-list`) — dual claim form status check.
- **Hard dependency on spec 096** (`x509-org-trust`) — `x5c` chain validation against trust store.
- **Hard dependency on spec 097** (`openid4vci-issuer`) — `Sorcha.Haip.Service` exists with deployment topology, rate limits, metadata infrastructure, API Gateway routing.

## Amendment note on spec 039

This spec **supersedes** the OID4VP presentation parts of `specs/039-verifiable-presentations`. Specifically:

- **US3** (OID4VP Credential Presentation) — superseded. The HAIP-conformant equivalent lives in this spec's US1, US3, and US4.
- **US4** (QR Code In-Person Presentation) — superseded. This spec's US6 (cross-device QR and same-device deep link) replaces it.
- **FR-013** (create presentation requests with credential type, issuer constraints, required claims, and a unique nonce) — superseded by FR-004, FR-009.
- **FR-014** (selective disclosure) — carried forward; the underlying mechanism is now spec 094's nested disclosure.
- **FR-015** (verify presentations by signature / status list / required claims / nonce freshness) — superseded by FR-017 through FR-023 (full HAIP-path verification pipeline). Spec 093 already fixed the corresponding internal-path behaviour.
- **FR-016** (`response_mode: direct_post`) — superseded by FR-013 (explicit `direct_post` callback endpoint).
- **FR-017** (QR codes for in-person presentation) — superseded by FR-006 and US6.
- **FR-018** (configurable TTL) — superseded by FR-011.

**Carried forward unchanged from spec 039**:

- US1 (Credential Lifecycle Management) — handled by spec 094 and the carry-forward of 031 requirements.
- US2 (Bitstring Status List) — still in force via spec 039, extended by spec 095.
- US5 (Multi-Method DID Resolution) — still in force via spec 039. The HAIP verifier falls back to DID-based trust where `x5c` is absent.
- US6 (Wallet Credential Card UI) — still in force for Sorcha-internal participants. HAIP external participants use their own wallet's UI.
- US7 (Presentation Request Inbox) — still in force for Sorcha-internal participants.
- US8 (Cross-Blueprint Credential Flows) — still in force. The HAIP path composes naturally.
- FR-007 through FR-012 (W3C Bitstring Status List) — still in force, extended by spec 095.
- FR-019 through FR-023 (DID resolution methods, `did:sorcha`, `did:web`, `did:key`) — still in force.
- FR-024 through FR-028 (wallet card UI, visual states, detail view, inbox) — still in force.
- FR-029 through FR-030 (cross-blueprint flows, credential issuance config) — still in force.
- FR-031 through FR-032 (manual credential import) — still in force. Full HAIP-side import flows via spec 097 when a wallet is HAIP-conformant; manual JSON import is preserved for unusual cases.

All other 039 requirements remain in force unless explicitly listed above as superseded.
