# Feature Specification: Timebound Presentation Lifecycle

**Feature Branch**: `111-presentation-lifecycle`
**Created**: 2026-04-23
**Status**: Draft
**Input**: Evolution of SEC-014 (HAIP two-phase execution) reframed as a general timebound-evidence primitive after design discussion. HAIP external-wallet credential presentation is the first consumer; the lifecycle must generalise.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Attempt is always recorded, even before outcome is known (Priority: P1)

A citizen is asked to present a credential (e.g. proof of identity) to complete a workflow action. The authority's register must record *that the citizen tried* at a particular moment in time, independently of whether the presentation ultimately succeeds, fails, or is abandoned. This attempt record is the legally-weighted proof of engagement and is essential in timebound contexts — for example, when a citizen must submit evidence before a statutory deadline and later needs to demonstrate (to a court, an auditor, or another authority) that they engaged with the system before the deadline, regardless of whether their credential was valid at that time.

**Why this priority**: Without this, the register cannot serve as evidence of engagement in timebound flows — the entire non-HAIP generalisation of this feature rests on it. It is also the minimum change that fixes the bug that triggered SEC-014: false completion records for actions that never actually completed.

**Independent Test**: Submit a presentation request through an authorised flow. Immediately query the register — an attempt record MUST be present, with the submitter wallet, the action being responded to, a digest of what was being asked (requirements), and a timestamp. The record MUST NOT carry any credential content. This can be validated without any credential being presented — the wallet never scans the QR; the attempt record stands on its own.

**Acceptance Scenarios**:

1. **Given** a citizen submits an action requiring a credential presentation, **When** the presentation request is created, **Then** a `presentation-initiated` transaction is written to the register within the same HTTP request cycle.
2. **Given** a `presentation-initiated` transaction exists, **When** any authorised party queries the register, **Then** they see the submitter wallet, action identifier, requirements digest, presentation request identifier, and timestamp — and no credential content.
3. **Given** a citizen never scans the QR code, **When** the presentation validity window expires, **Then** the attempt record remains on the register unchanged as the legally-weighted proof of engagement.

---

### User Story 2 - Outcome is recorded with reason, success or decline (Priority: P1)

When the citizen's wallet returns a presentation (success or decline), the register records the outcome. Success carries the presented credential claims (subject to the normal disclosure rules of the register) and is what downstream workflow routing consumes. Decline carries a reason code (e.g. "expired-credential", "wrong-issuer", "revoked") so the authority, the citizen, and downstream systems all know why it failed.

**Why this priority**: A system that records attempts without outcomes is incomplete — the citizen cannot prove they eventually succeeded, the authority cannot progress the workflow, and auditors cannot explain failures. This is tightly coupled to Story 1 (both must ship together to be meaningful) but represents distinct functionality worth independent testing.

**Independent Test**: With a prior attempt record already on the register, post a verifier callback (success or decline). A `presentation-outcome` transaction MUST be written, linked by the same presentation request identifier, carrying the outcome kind and — for success — the credential claims, or — for decline — the reason code. Downstream workflow routing receives the outcome and responds appropriately (action completes on success, action terminates or reroutes on decline).

**Acceptance Scenarios**:

1. **Given** a pending presentation request, **When** the citizen's wallet presents a valid credential and the verifier confirms it, **Then** a `presentation-outcome` transaction with kind `success` is written, carrying the verified claims under the same disclosure rules as a normal action payload, and the action is marked complete.
2. **Given** a pending presentation request, **When** the citizen's wallet presents an expired credential, **Then** a `presentation-outcome` transaction with kind `decline` and reason `expired-credential` is written, and the action terminates or reroutes per the blueprint's routing rules.
3. **Given** a `presentation-outcome` transaction has already been written, **When** the verifier calls back a second time with the same presentation request identifier, **Then** the second callback is a no-op (idempotent first-write-wins) and the register is not mutated.

---

### User Story 3 - Retry is a first-class flow (Priority: P2)

A citizen whose first presentation is declined (expired credential, wrong issuer, etc.) can retry by submitting a new presentation attempt against the same action. Both attempts are visible on the register as a timeline of events, not as mutations of a single "last attempt" state.

**Why this priority**: Without this, declined citizens are stuck. With it, the full timeline of the citizen's engagement is preserved — valuable for audit, for fraud investigation, and for the citizen's own evidence that they eventually resolved whatever was wrong with their first presentation.

**Independent Test**: After a decline outcome is recorded, submit a new presentation attempt against the same action. A second `presentation-initiated` transaction is written with a new presentation request identifier; the first `presentation-outcome` remains unchanged; subsequent outcomes tie to the new identifier. Querying the register's event timeline shows attempt 1 -> decline -> attempt 2 -> outcome.

**Acceptance Scenarios**:

1. **Given** a declined presentation, **When** the citizen submits a new presentation for the same action, **Then** a new `presentation-initiated` transaction is written with a distinct presentation request identifier, and the previous declined outcome is preserved on the register.
2. **Given** two attempts exist (one declined, one pending), **When** any party queries the register, **Then** they see the full timeline in chronological order.

---

### User Story 4 - Optional abandonment record for timebound flows (Priority: P3)

For blueprints operating in statutory or time-pressured contexts (e.g. deadline-driven submissions), the authority can opt to write a `presentation-abandoned` transaction when the presentation validity window expires with no callback received. Low-stakes flows can opt out; abandonment is not recorded by default.

**Why this priority**: Mostly a record-keeping nicety — the attempt record from Story 1 already provides the evidence of engagement. Abandonment adds a clean "this one is definitively closed" signal that simplifies dashboards, garbage-collection, and downstream retry logic for specific flows. It is valuable but not essential to the feature's core purpose.

**Independent Test**: Configure a blueprint with `recordAbandonment: true`. Submit a presentation attempt and never send a callback. After the validity window elapses, verify that a `presentation-abandoned` transaction is written, linked by the presentation request identifier, carrying only the abandonment timestamp. Configure a second blueprint with `recordAbandonment: false` and repeat — no abandonment record is written.

**Acceptance Scenarios**:

1. **Given** a blueprint with `recordAbandonment: true` and a pending presentation, **When** the validity window elapses without a callback, **Then** a `presentation-abandoned` transaction is written within a reasonable delay (no more than 60 seconds after window expiry).
2. **Given** a blueprint with `recordAbandonment: false`, **When** the window elapses without a callback, **Then** no abandonment transaction is written; the attempt record stands alone.
3. **Given** a `presentation-abandoned` transaction has been written, **When** a late callback arrives (e.g. the citizen finally scanned), **Then** the outcome transaction is still written; the register shows both abandonment and outcome; clients resolve by timestamp order.

---

### User Story 5 - Reuse the primitive for non-HAIP timebound flows (Priority: P3)

Other workflow features that need timebound-evidence semantics — deadline-driven file uploads, multi-party signature requests, step-up authentication with failed MFA — can adopt the same three-event lifecycle (initiated / outcome / abandoned) without re-implementing it. HAIP is the first consumer, not the only one.

**Why this priority**: This is an architectural goal, not a near-term user-facing requirement. It shapes the design so the lifecycle primitive lives outside HAIP-specific code, but near-term delivery value is 100% from Stories 1-4.

**Independent Test**: Implement a second, non-HAIP consumer in a later feature (e.g. a file-upload-by-deadline flow). The same lifecycle transactions are written — initiated, outcome, abandoned — against the same register as the originating action. No HAIP-specific code is referenced by the new consumer. This test is deferred to the second-consumer feature; for the current feature, the independent test is code review confirming no HAIP-specific assumptions leak into the lifecycle primitive.

**Acceptance Scenarios**:

1. **Given** the lifecycle primitive is shipped, **When** a developer wants to add a new timebound-evidence consumer, **Then** they can register as a consumer without modifying the primitive's contract.
2. **Given** the HAIP verifier is offline, **When** a non-HAIP consumer is registered, **Then** that consumer's lifecycle flows continue to function independently.

---

### Edge Cases

- **Late callback after abandonment**: The `presentation-abandoned` record is written, then a callback arrives. The outcome record is still written; the register shows both. Clients resolve by timestamp order. The register's "truth" is the full event stream, not a single resolved state.
- **Verifier callback arrives for unknown request identifier**: The request identifier doesn't match any pending attempt (attempt was never recorded, or TTL already cleaned up transient state). The callback is rejected at the verification endpoint with a 404-equivalent; no register writes occur.
- **Two citizens submit concurrent attempts for the same open-participant action**: Both generate distinct presentation request identifiers and distinct `presentation-initiated` transactions. The late-binding rule (first successful outcome wins) determines which citizen binds to the action; the other's outcome is recorded as declined with a reason indicating the action is no longer available.
- **Attempt rate-limit exceeded**: A submitter wallet exceeds the attempts threshold within the configured window. The endpoint rejects the submission with a rate-limit error; no attempt transaction is written. This is distinct from a decline — it's an input-validation rejection, not an evidence event.
- **Process restart mid-flow**: Transient pending-attempt state is lost. The citizen's wallet scan arrives; the verifier callback cannot find the pending request and rejects with 404. The citizen must re-submit, generating a new attempt record. The original attempt record remains on the register.
- **Outcome arrives after validity window expires but before abandonment is written**: Race condition. The outcome record is written; abandonment is skipped (because an outcome now exists for that request identifier).
- **Credential claims contain PII that cannot go on the register in plaintext**: Success outcomes use the register's normal encryption pipeline; the claim payload is encrypted per the disclosure rules of the originating action. This is a standard disclosure-rule application, not a new concern.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST write a `presentation-initiated` transaction to the register carrying the originating action, immediately when a citizen submits a presentation-required action, containing the submitter wallet, action identifier, instance identifier, a digest of the credential requirements, the presentation request identifier, and a timestamp.
- **FR-002**: System MUST NOT write any credential data into the `presentation-initiated` transaction — at the point of writing, no credential has been presented.
- **FR-003**: System MUST hold the pending presentation state (mapping presentation request identifier to the pending action context) in transient storage with a time-to-live equal to the presentation validity window, colocated with other transient HAIP-flow state.
- **FR-004**: System MUST default the presentation validity window to 600 seconds (10 minutes) and MUST allow a per-blueprint override via a blueprint configuration field.
- **FR-005**: System MUST, on receiving a verifier callback, write exactly one `presentation-outcome` transaction per presentation request identifier, carrying either `success` with the verified claims under the register's normal encryption pipeline, or `decline` with a reason code.
- **FR-006**: System MUST treat repeated verifier callbacks for the same presentation request identifier as idempotent — the first-written outcome wins; subsequent callbacks produce no register writes.
- **FR-007**: System MUST, for blueprints configured with `recordAbandonment: true`, write a `presentation-abandoned` transaction within 60 seconds of the validity window elapsing if no outcome has been written.
- **FR-008**: System MUST NOT write a `presentation-abandoned` transaction if an outcome transaction already exists for the same presentation request identifier (race-safe).
- **FR-009**: System MUST allow a late outcome to be written after an abandonment record has been written. Both transactions MUST remain on the register; no mutation of prior transactions occurs.
- **FR-010**: System MUST support retry — a new `presentation-initiated` transaction can be submitted for the same action after a decline or abandonment, producing a new presentation request identifier and a distinct attempt record.
- **FR-011**: System MUST rate-limit attempt submissions per submitter wallet on a **per-wallet-per-register** basis, preventing a single wallet from flooding one register while allowing concurrent unrelated presentations across registers. Thresholds and window are configured at deployment time.
- **FR-012**: System MUST validate the presentation request identifier in the verifier callback against the pending-attempt state (CSRF protection), and MUST reject callbacks where the identifier does not match an active pending request.
- **FR-013**: System MUST allow per-blueprint configuration of outcome detail level: `minimal` (reason code only on decline) or `verbose` (reason code plus verifier diagnostics). The **platform default MUST depend on the register's visibility**: public-visibility registers default to `minimal` (privacy-preserving, since decline diagnostics land on a public record); private-visibility registers default to `verbose` (debugging-friendly, since access is already restricted to authorised subscribers). Blueprints MAY override the default explicitly.
- **FR-014**: System MUST expose the three lifecycle transaction types as first-class, queryable register transactions, distinguishable from ordinary action transactions by transaction type.
- **FR-015**: System MUST treat the action as complete (and route downstream) only when a `presentation-outcome` with kind `success` has been written — never on `presentation-initiated` alone.
- **FR-016**: System MUST generalise the lifecycle primitive so it is not coupled to HAIP-specific types. Consumers (HAIP verifier today, others in future) MUST integrate via a defined consumer contract.
- **FR-017**: No in-flight migration is required. This feature ships on a clean-start basis — there are no live HAIP presentations under the old one-shot semantics at release time. The old code path is replaced outright; no grandfathering, no re-flow, no scheduled drain.

### Key Entities *(include if feature involves data)*

- **Presentation Attempt**: The record that a citizen began a presentation flow at a given moment. Carries the submitter wallet, action reference, requirements digest, presentation request identifier, and timestamp. Never carries credential content. Persists on the register forever.
- **Presentation Outcome**: The record of the citizen's wallet's response to the presentation request. Kind is `success` or `decline`. Success carries verified credential claims (encrypted per the register's disclosure rules). Decline carries a reason code and optional verifier diagnostics. Persists on the register forever.
- **Presentation Abandonment**: The record that a presentation attempt expired without a callback. Carries only the presentation request identifier and abandonment timestamp. Optional per blueprint configuration. Persists on the register forever.
- **Pending Presentation State**: Transient mapping from presentation request identifier to the originating action context needed to complete the flow on callback. Time-to-live equals the validity window. Lost on process restart; citizens must re-submit, which is acceptable.
- **Blueprint Presentation Configuration**: Per-blueprint fields controlling lifecycle behaviour — `recordAbandonment`, `outcomeDetailLevel`, and `presentationValidityWindowSeconds`.
- **Rate Limit Policy**: Configuration for attempt rate-limiting — scope is fixed at per-wallet-per-register; threshold and window are deployment-configurable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In 100% of cases where a citizen engages a presentation flow but fails or abandons, the register carries an attempt record proving engagement at the submitted timestamp, queryable by the citizen's wallet as evidence.
- **SC-002**: In 0% of cases does the register carry a `completed` or `success` entry for a presentation that never actually completed. (Zero false completion records.)
- **SC-003**: An authority can calculate "presentation attempts per successful outcome" as a workflow KPI from register data alone, without viewing any credential content.
- **SC-004**: A citizen who retries after a decline sees a new attempt record and ultimately a success outcome, with a full visible timeline on the register of every attempt they made and when.
- **SC-005**: Register volume from attempt records stays bounded — rate-limit controls prevent a single submitter wallet adding more than the configured threshold within the configured window.
- **SC-006**: A presentation that times out does not leave the action stuck indefinitely; for blueprints opting in, the register shows an abandonment record within 60 seconds of the validity window elapsing.
- **SC-007**: A second, non-HAIP consumer of the lifecycle primitive can be added in a future feature without modifying the primitive itself. (Verified at the second-consumer feature's review time, not this feature's.)
- **SC-008**: Repeated verifier callbacks for the same presentation request do not produce duplicate outcome records on the register; first-write-wins idempotency holds across 100% of tested callback scenarios.

## Assumptions

- The register's existing encryption pipeline is sufficient to protect credential claims on success outcomes (same pipeline already used for action payloads); no new encryption primitive is required.
- Transient pending-presentation state uses the same storage mechanism already used by related HAIP flows (pre-auth codes, nonces, access tokens); this is not a new infrastructure dependency.
- A 10-minute default validity window balances UX (citizens with unfamiliar phone wallets need time) against replay-attack surface.
- Attempt records without credential content pose no disclosure risk beyond revealing that a particular wallet engaged with a particular action at a particular time; this is the same disclosure already present for ordinary action submission events.
- The rate-limit enforcement point is the attempt submission endpoint, not the transient-state layer — input validation rejects excess attempts before any state or register write.
- The blueprint configuration for `recordAbandonment`, `outcomeDetailLevel`, and `presentationValidityWindowSeconds` is static at publish time and does not change per instance; configuration drift within a live instance is out of scope.
- Existing verifier-callback CSRF protection (OpenID4VP `state` parameter) remains in place and is reused by this lifecycle — no regression.

## Out of Scope

- Changes to the platform's overall disclosure model or encryption pipeline — only the orchestration around presentation actions changes.
- Changes to the OpenID4VP protocol itself or the internals of the HAIP verifier — this feature is about how the workflow engine orchestrates the verifier, not how the verifier works.
- The HAIP credential *offer* (issuance) flow — this feature only changes credential *presentation* (consumption). Issuance stays as a single-phase flow.
- Retrospective migration of *completed* historical HAIP presentations to the new lifecycle — they stay in their original form on the register, since the register is immutable by design.
- A UI for viewing the presentation timeline — this feature is backend-only. Any UI surface is a separate feature.
- Cross-register lifecycle flows — the three-event sequence is always on the originating action's register, not across registers.
