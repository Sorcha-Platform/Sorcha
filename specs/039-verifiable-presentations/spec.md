# Feature Specification: Verifiable Credential Lifecycle & Presentations

**Feature Branch**: `039-verifiable-presentations`
**Created**: 2026-02-21
**Status**: Draft
**Input**: User description: "Verifiable credential lifecycle, OID4VP presentations, Bitstring Status List, wallet card UI, and multi-method DID resolution"
**Design Document**: `docs/archive/plans/2026-02-21-verifiable-credentials-design.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Credential Lifecycle Management (Priority: P1)

An issuer (e.g., a government authority) issues a credential through a blueprint workflow action. The credential is stored in the recipient's wallet with full lifecycle support: it starts as Active, can be Suspended temporarily by the issuer, Revoked permanently, expires naturally, or is Consumed after use (for single-use credentials like tickets). The issuer can check and manage the status of credentials they have issued.

**Why this priority**: Without lifecycle states, credentials have no trust model. Verifiers cannot distinguish between valid and invalid credentials. This is the foundation all other features depend on.

**Independent Test**: Can be fully tested by issuing a credential via a blueprint action and then cycling it through Active → Suspended → Reinstated → Revoked states, verifying the credential's status is correctly reflected at each stage.

**Acceptance Scenarios**:

1. **Given** a blueprint action with credential issuance configuration completes, **When** the engine processes the action, **Then** an SD-JWT VC credential is created, stored in the recipient's wallet with status "Active", and an issuance transaction is written to the register.
2. **Given** an Active credential, **When** the issuer suspends it, **Then** the credential status becomes "Suspended" in the wallet and the suspension bitstring is updated.
3. **Given** a Suspended credential, **When** the issuer reinstates it, **Then** the credential status returns to "Active" and the suspension bit is cleared.
4. **Given** an Active credential, **When** the issuer revokes it, **Then** the credential status becomes "Revoked" permanently and the revocation bit is set.
5. **Given** a credential with an expiry date, **When** the expiry date passes, **Then** the credential status transitions to "Expired" and cannot be presented.
6. **Given** a SingleUse credential, **When** it is successfully presented once, **Then** the credential status becomes "Consumed" and further presentations are rejected.
7. **Given** an expired credential that supports refresh, **When** the holder requests renewal, **Then** the original credential is consumed and a new credential is issued with a fresh expiry.

---

### User Story 2 - Bitstring Status List (Priority: P1)

Credential status (revocation and suspension) is tracked using the W3C Bitstring Status List standard. Each issuer maintains a compressed bitstring per register where each credential occupies a fixed position. Verifiers fetch the status list to check credential validity without contacting the issuer directly, preserving holder privacy. The canonical status list is stored as a Control transaction on the register, with a cached HTTP endpoint for fast verifier access.

**Why this priority**: The status list is the mechanism that makes lifecycle management verifiable by third parties. Without it, verifiers must trust the holder's claim about status — which defeats the purpose.

**Independent Test**: Can be tested by allocating a credential in a status list, flipping its revocation bit, and verifying that a status list fetch reflects the change within the cache TTL.

**Acceptance Scenarios**:

1. **Given** a new credential is issued, **When** the issuance completes, **Then** the credential is assigned a unique index position in the issuer's status list and a `credentialStatus` claim is embedded in the VC pointing to the list URL and index.
2. **Given** a status list exists on a register, **When** a verifier requests it via the HTTP endpoint, **Then** a W3C-compliant Bitstring Status List credential is returned, signed by the issuer.
3. **Given** a credential is revoked, **When** the revocation is processed, **Then** the revocation bit at the credential's index is set to 1, and a Control transaction with the updated bitstring is written to the register.
4. **Given** a status list has been recently fetched, **When** a second request arrives within the cache TTL, **Then** the cached version is returned without re-fetching from the register.
5. **Given** a status list with 131,072 entries, **When** a verifier downloads it, **Then** the compressed size is under 20KB and the verifier cannot determine which specific credential was checked.

---

### User Story 3 - OID4VP Credential Presentation (Priority: P2)

A holder presents a credential to a verifier using the OID4VP (OpenID for Verifiable Presentations) protocol. The verifier creates a presentation request specifying what credential type and claims they need. The holder's wallet shows the request, lets the holder choose which claims to disclose, and submits a signed presentation. The verifier validates the signature, checks the status list, and confirms the claims. This works both remotely (HTTPS-based) and in-person via QR code scanning.

**Why this priority**: Presentations are the primary way credentials deliver value — without them, credentials are just stored data. This is higher priority than the wallet UI because it enables programmatic verification within blueprint workflows.

**Independent Test**: Can be tested by creating a presentation request, having a wallet match it to a credential, generating an SD-JWT presentation with selective disclosure, and verifying it end-to-end including status list check and nonce validation.

**Acceptance Scenarios**:

1. **Given** a verifier needs to check a credential, **When** they create a presentation request with type, issuer constraints, required claims, and a nonce, **Then** the request is stored and a unique request ID is returned.
2. **Given** a presentation request exists, **When** the holder's wallet fetches the request, **Then** the wallet shows which credential matches, which claims are requested, and which will be disclosed.
3. **Given** the holder approves a disclosure, **When** the wallet creates the presentation, **Then** an SD-JWT presentation is generated containing only the approved claims, bound to the verifier's nonce.
4. **Given** a presentation is submitted, **When** the verifier validates it, **Then** the signature is verified via DID resolution, the status list is checked, required claims are validated, and the nonce confirms freshness.
5. **Given** a replay attempt using a previous presentation, **When** submitted with a different nonce, **Then** the verification fails because the nonce does not match.
6. **Given** a presentation for a revoked credential, **When** the verifier checks the status list, **Then** the verification fails with a clear "credential revoked" reason.

---

### User Story 4 - QR Code In-Person Presentation (Priority: P2)

A verifier terminal (e.g., at a building site gate, event entrance, or pharmacy counter) displays a QR code. The holder scans the QR code with their phone, which opens the wallet, shows the presentation request, and allows approval. The presentation is submitted over HTTPS to the verifier's callback URL. The terminal receives the verification result and acts on it (e.g., opens a gate, approves a transaction).

**Why this priority**: This bridges digital credentials to physical-world interactions. It shares the same OID4VP protocol as remote presentation, so the incremental effort is in QR encoding and terminal-side flow, not in a separate verification path.

**Independent Test**: Can be tested by generating a QR code containing a presentation request URL, scanning it (or simulating a scan), completing the OID4VP flow, and confirming the terminal receives the verification result.

**Acceptance Scenarios**:

1. **Given** a verifier creates a presentation request, **When** the terminal generates a QR code, **Then** the QR encodes a URL containing the request endpoint and a unique nonce (not the credential itself).
2. **Given** a holder scans the QR code, **When** the wallet opens, **Then** the full presentation request is fetched from the URL and displayed to the holder.
3. **Given** the holder approves the presentation, **When** the wallet submits the vp_token, **Then** it is POSTed via HTTPS direct_post to the verifier's callback URL.
4. **Given** the verifier receives the vp_token, **When** verification completes, **Then** the terminal displays a success or failure result within 3 seconds of the holder's approval.
5. **Given** a QR code from a previous session, **When** scanned after the request has expired, **Then** the wallet displays a clear "request expired" message.

---

### User Story 5 - Multi-Method DID Resolution (Priority: P2)

Credentials reference issuer and subject identities using Decentralized Identifiers (DIDs). The system resolves DIDs from multiple methods: `did:sorcha` (native Sorcha registers and wallets), `did:web` (HTTP-based, for organizations with web presence), and `did:key` (self-contained, for ephemeral interactions). This enables verifying credentials issued by external parties and allows external verifiers to check Sorcha-issued credentials.

**Why this priority**: DID resolution is required for signature verification during presentation. Without it, the system can only verify credentials from Sorcha wallets, not from external issuers. It is essential for cross-system interoperability.

**Independent Test**: Can be tested by resolving each DID method independently — a `did:sorcha:w:` address against the wallet service, a `did:web:` against a mock HTTPS endpoint, and a `did:key:` by decoding the multicodec key — and confirming each returns a valid DID Document with verification methods.

**Acceptance Scenarios**:

1. **Given** a `did:sorcha:w:{address}` identifier, **When** resolved, **Then** the resolver queries the wallet service for the public key and returns a DID Document with the wallet's verification method.
2. **Given** a `did:sorcha:r:{registerId}:t:{txId}` identifier, **When** resolved, **Then** the resolver fetches the transaction from the register and extracts the public key from the payload.
3. **Given** a `did:web:example.com` identifier, **When** resolved, **Then** the resolver fetches `https://example.com/.well-known/did.json` and returns the DID Document.
4. **Given** a `did:key:z6Mk...` identifier, **When** resolved, **Then** the resolver decodes the multicodec-encoded public key from the DID string and returns a DID Document without any network call.
5. **Given** an unsupported DID method (e.g., `did:ethr`), **When** resolution is attempted, **Then** the resolver returns a clear "unsupported method" error, and the system does not crash or hang.
6. **Given** a `did:web` endpoint that is unreachable, **When** resolution is attempted, **Then** the resolver returns a timeout error within 5 seconds.

---

### User Story 6 - Wallet Credential Card UI (Priority: P3)

Holders view and manage their credentials in the wallet UI through a visual card-based display. Each credential appears as a styled card showing the credential type, issuer, key claims, status, and expiry. Cards are visually distinct based on the credential type (color, icon, layout) as configured by the issuer. Holders can tap a card to see full details, present it, export it, or manage its status.

**Why this priority**: The UI is the user-facing layer. While critical for the end-user experience, it depends on the backend lifecycle, status list, and presentation infrastructure being in place first.

**Independent Test**: Can be tested by loading a wallet with multiple credentials of different types and statuses, rendering the card view, and verifying correct visual treatment, filtering, and action availability for each state.

**Acceptance Scenarios**:

1. **Given** a wallet contains multiple credentials, **When** the holder opens the Credentials tab, **Then** each credential is displayed as a visual card with type icon, issuer name, key claims, status indicator, and expiry date.
2. **Given** a credential has issuer-defined display configuration (color, icon, layout), **When** the card renders, **Then** it uses the issuer's visual template.
3. **Given** a credential with no display configuration, **When** the card renders, **Then** a sensible default is generated from the credential type name.
4. **Given** an Active credential, **When** the holder taps the card, **Then** the detail view shows all claims with disclosure indicators, metadata, usage history, and action buttons (Present, Export, Delete).
5. **Given** a Suspended credential, **When** displayed, **Then** the card shows an amber status indicator and the "Present" action is disabled.
6. **Given** a Revoked credential, **When** displayed, **Then** the card is greyed out with a red status indicator and only View and Delete actions are available.
7. **Given** a credential expiring within 30 days, **When** displayed, **Then** the card shows an amber warning with a countdown and a "Renew" button if refresh is supported.
8. **Given** a Consumed (single-use) credential, **When** displayed, **Then** the card shows a "Used" badge with strikethrough styling and only View and Delete actions are available.

---

### User Story 7 - Presentation Request Inbox (Priority: P3)

When a verifier sends a presentation request targeting the holder's wallet, the holder receives a notification. The wallet UI displays the request showing who is asking, what claims they want, which credential matches, and exactly what will be disclosed versus hidden. The holder can approve or deny the request.

**Why this priority**: This is the user-facing side of OID4VP presentation. It depends on the presentation protocol infrastructure (Story 3) being in place.

**Independent Test**: Can be tested by creating a presentation request against a wallet address, verifying the notification appears, and confirming the request detail view correctly shows matching credentials, requested claims, and disclosure scope.

**Acceptance Scenarios**:

1. **Given** a verifier creates a presentation request for a holder's wallet, **When** the request is received, **Then** the holder sees a notification in the wallet UI.
2. **Given** a presentation request notification, **When** the holder opens it, **Then** the dialog shows the verifier's identity, requested credential type, specific claims requested, and which claims will be disclosed.
3. **Given** a presentation request that matches multiple credentials, **When** displayed, **Then** the holder can choose which credential to present.
4. **Given** the holder approves a presentation, **When** submitted, **Then** the SD-JWT presentation is created with only the approved disclosures and sent to the verifier.
5. **Given** the holder denies a presentation request, **When** denied, **Then** the verifier receives a denial response and no credential data is shared.

---

### User Story 8 - Cross-Blueprint Credential Flows (Priority: P2)

A credential issued by one blueprint workflow can be required by a different blueprint workflow. For example, Blueprint A ("Chemical Handling License") issues a credential on completion. Blueprint B ("Chemical Purchase Order") requires that credential to initiate. The verifier (Blueprint B's engine) checks the credential's status on Blueprint A's register via the Bitstring Status List. If the issuer later suspends or revokes the credential, future attempts to use it in Blueprint B fail.

**Why this priority**: This is the core value proposition differentiating Sorcha from standalone VC platforms. It enables declarative credential supply chains across workflows and registers.

**Independent Test**: Can be tested by configuring two blueprint templates — one with credential issuance on its final action, another with a credential requirement on its first action — and running both, verifying the credential flows from one to the other and that revocation in the first blocks the second.

**Acceptance Scenarios**:

1. **Given** Blueprint A completes its final action with credential issuance, **When** the credential is issued, **Then** it is stored in the recipient's wallet and a status list entry is allocated on Blueprint A's register.
2. **Given** Blueprint B has a credential requirement on its first action, **When** a participant submits the action with a presentation of the credential from Blueprint A, **Then** the engine verifies it by resolving the issuer's DID and checking the status list on Blueprint A's register.
3. **Given** the credential from Blueprint A has been revoked, **When** a participant attempts to use it in Blueprint B, **Then** the engine rejects the submission with a "credential revoked" error.
4. **Given** a credential is used across blueprints on different registers, **When** the verifier checks the status list, **Then** the status list is fetched from the issuing register (not the verifying register).

---

### Edge Cases

- What happens when a status list reaches capacity (all 131,072 positions allocated)? The issuer must create a new status list and new credentials reference the new list.
- What happens when a verifier cannot reach the status list endpoint? Behaviour depends on the `RevocationCheckPolicy` — FailClosed blocks the action, FailOpen allows it with an audit warning.
- What happens when a holder presents a credential to a verifier that doesn't support the credential's DID method? The verifier returns an "unsupported DID method" error.
- What happens when a `did:web` domain's TLS certificate is invalid? Resolution fails with a clear security error — no fallback to HTTP.
- What happens when a QR code is scanned but the wallet has no matching credential? The wallet displays "No matching credential found" with the verifier's request details.
- What happens when two issuers assign the same credential type name but issue different credentials? Credential matching uses both type and issuer identity — the AcceptedIssuers constraint disambiguates.
- What happens when a LimitedUse credential has 1 presentation remaining and two verifiers request it simultaneously? The first successful verification triggers consumption; the second receives a "credential consumed" error.
- What happens when a holder's wallet is offline when a presentation request arrives? The request is stored server-side with a TTL; the holder sees it when they next open the wallet.
- What happens when a holder deletes a credential from their wallet? Deletion is a local wallet operation only — the status list entry is unchanged, the issuer can still revoke, and the credential remains valid if exported elsewhere. Issuer notification on deletion is deferred to a future phase.

## Requirements *(mandatory)*

### Functional Requirements

**Lifecycle Management:**
- **FR-001**: System MUST support five credential states: Active, Suspended, Revoked, Expired, and Consumed.
- **FR-002**: System MUST allow the original issuing wallet or register governance roles (Owner/Admin) to suspend an Active credential (reversible) and reinstate a Suspended credential.
- **FR-003**: System MUST allow the original issuing wallet or register governance roles (Owner/Admin) to revoke an Active or Suspended credential (permanent, irreversible).
- **FR-004**: System MUST automatically transition credentials to Expired when their `expiresAt` date passes.
- **FR-005**: System MUST support credential refresh/reissuance for expired credentials when the credential type allows it, consuming the old credential and issuing a new one.
- **FR-006**: System MUST support three usage policies: Reusable (unlimited presentations), SingleUse (one presentation then consumed), and LimitedUse (N presentations then consumed).

**Bitstring Status List:**
- **FR-007**: System MUST implement W3C Bitstring Status List v1.0 with separate bitstrings for revocation and suspension purposes.
- **FR-008**: System MUST allocate a unique index position in the status list for each issued credential.
- **FR-009**: System MUST embed a `credentialStatus` claim in every issued VC pointing to the status list URL and the credential's index position.
- **FR-010**: System MUST store the canonical status list as a Control transaction on the issuing register.
- **FR-011**: System MUST provide a public (unauthenticated) cached HTTP endpoint for verifiers to fetch status lists, with configurable cache TTL (default 5 minutes). Privacy is ensured by the minimum list size (131,072 entries), not access control.
- **FR-012**: Status list size MUST be at least 131,072 entries (W3C recommended minimum for privacy).

**OID4VP Presentations:**
- **FR-013**: System MUST support creating presentation requests with credential type, issuer constraints, required claims, and a unique nonce.
- **FR-014**: System MUST support selective disclosure — holders choose which claims to reveal in each presentation.
- **FR-015**: System MUST verify presentations by checking: signature validity (via DID resolution), status list (not revoked/suspended/consumed), required claim constraints, and nonce freshness.
- **FR-016**: System MUST support `response_mode=direct_post` where the wallet POSTs the `vp_token` to the verifier's callback URL.
- **FR-017**: System MUST generate QR codes for in-person presentation that encode a request URL and nonce (not the credential itself).
- **FR-018**: System MUST support presentation requests with a configurable TTL (default 5 minutes) after which they expire.

**DID Resolution:**
- **FR-019**: System MUST resolve `did:sorcha` identifiers by querying wallet service (for wallet DIDs) or register service (for register DIDs).
- **FR-020**: System MUST resolve `did:web` identifiers by fetching the DID Document from the standard well-known path on the domain.
- **FR-021**: System MUST resolve `did:key` identifiers by decoding the multicodec-encoded public key from the DID string.
- **FR-022**: System MUST provide a pluggable DID resolver interface that supports registering additional methods.
- **FR-023**: DID resolution for `did:web` MUST enforce HTTPS (no HTTP fallback) and timeout within 5 seconds.

**Credential Import:**
- **FR-031**: System MUST allow holders to import externally-issued SD-JWT VC credentials into their wallet via the existing store endpoint, provided the credential is a valid SD-JWT VC with a resolvable issuer DID.
- **FR-032**: Imported credentials MUST be treated identically to Sorcha-issued credentials for display, presentation, and status checking purposes. Status list checks use the `credentialStatus` claim embedded in the imported VC.

**Wallet UI:**
- **FR-024**: System MUST display credentials as visual cards with type-specific styling (color, icon, layout) based on issuer-defined display configuration.
- **FR-025**: System MUST show credential status using visual indicators: green (Active), amber (Suspended/Expiring Soon), red (Revoked), grey (Expired), strikethrough (Consumed).
- **FR-026**: System MUST provide a credential detail view showing all claims, disclosure indicators, metadata, usage history, and available actions.
- **FR-027**: System MUST provide a presentation request inbox where holders can review, approve, or deny incoming requests.
- **FR-028**: System MUST disable the "Present" action for credentials that are not in Active state.

**Cross-Blueprint:**
- **FR-029**: System MUST support credential verification across different blueprint instances and registers, resolving the issuer's status list from the issuing register.
- **FR-030**: System MUST support credential issuance configuration on blueprint actions, including usage policy, display configuration, and status list allocation.

### Key Entities

- **CredentialEntity**: A verifiable credential stored in a wallet. Has type, issuer DID, subject DID, claims, status (Active/Suspended/Revoked/Expired/Consumed), usage policy, status list index, presentation count, expiry date, raw SD-JWT token, and display configuration.
- **BitstringStatusList**: A compressed bitstring tracking credential status. Associated with an issuer and register. Contains revocation and suspension bitstrings, list metadata, and version. Stored as a Control transaction on the register.
- **PresentationRequest**: A verifier's request for a credential presentation. Contains credential type, issuer constraints, required claims, nonce, callback URL, and TTL. Has states: Pending, Submitted, Verified, Denied, Expired.
- **DidDocument**: A W3C DID Core document resolved from a DID. Contains verification methods (public keys), authentication methods, assertion methods, and service endpoints.
- **CredentialDisplayConfig**: Issuer-defined visual template for how a credential appears in wallets. Contains background color, text color, icon, card layout, and highlighted claims.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Credentials transition through all five lifecycle states correctly, with each state change reflected in the Bitstring Status List within 5 seconds.
- **SC-002**: Verifiers can check credential status via the cached status list endpoint, with 95% of requests served from cache under normal operation.
- **SC-003**: End-to-end OID4VP presentation (request creation through to verification result) completes within 5 seconds for remote flows and within 8 seconds for QR-initiated flows.
- **SC-004**: QR code scanning and wallet notification appear within 2 seconds of scanning.
- **SC-005**: DID resolution succeeds for all three supported methods (`did:sorcha`, `did:web`, `did:key`) with `did:key` resolving without any network call.
- **SC-006**: Selective disclosure correctly reveals only the claims approved by the holder — no additional claims leak in the presentation.
- **SC-007**: SingleUse credentials are consumed after exactly one successful presentation; subsequent attempts are rejected.
- **SC-008**: Cross-blueprint credential flows work across different registers, with the verifying engine correctly fetching the status list from the issuing register.
- **SC-009**: The wallet credential card UI renders correctly for all five credential states with appropriate visual treatment and available actions.
- **SC-010**: Presentation request inbox shows all pending requests and correctly matches them to stored credentials.

## Clarifications

### Session 2026-02-21

- Q: Who is authorized to perform credential lifecycle operations (suspend/revoke/reinstate)? → A: The original issuing wallet plus register governance roles (Owner/Admin) can suspend, revoke, and reinstate credentials.
- Q: Can holders import credentials issued outside of Sorcha into their wallet? → A: Yes, manual import of pre-formed SD-JWT VCs via the store endpoint. Full OID4VCI import deferred to a future phase.
- Q: What is the default TTL for presentation requests? → A: 5 minutes, configurable per request.
- Q: Is the status list HTTP endpoint public or authenticated? → A: Public (unauthenticated). Privacy ensured by minimum list size (131,072 entries), not access control. Aligns with W3C standard.
- Q: What happens when a holder deletes a credential from their wallet? → A: Local wallet operation only — status list unchanged, issuer can still revoke. Issuer notification on deletion deferred to future phase.

## Assumptions

- The existing SD-JWT cryptographic implementation (RFC 9901) in the cryptography module is correct and does not need modification.
- The existing credential verifier and issuer in the Blueprint Engine provide a sound foundation to extend (not replace).
- The existing Wallet Service credential storage endpoints are functional and will be extended, not rewritten.
- The W3C Bitstring Status List v1.0 specification (published May 2025) is stable and will not change materially.
- `did:web` resolution assumes the target domain serves valid TLS certificates and a correctly formatted DID Document.
- `did:key` only needs to support ED25519 and P-256 key types (matching existing crypto support).
- QR code presentation assumes the holder's device has a camera and internet connectivity (no offline-only presentation).
- The Blazor PWA can generate and read QR codes using a JavaScript interop library.
- Status list cache TTL of 5 minutes is acceptable for most use cases; time-critical scenarios may need shorter TTL.
- The Construction Permit walkthrough template provides a reference pattern for credential issuance configuration.
