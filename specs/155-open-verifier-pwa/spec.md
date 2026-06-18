# Feature Specification: Open Verifier PWA

**Feature Branch**: `155-open-verifier-pwa`

**Created**: 2026-06-17

**Status**: Draft

**Input**: Approved design — `docs/superpowers/specs/2026-06-17-open-verifier-pwa-design.md`

## User Scenarios & Testing *(mandatory)*

The Sorcha reference verifier today is a bare test harness: a form, a QR, and a minimal outcome. This
feature turns it into an **installable app** that performs an **open**, present-then-cross-check
verification and explains *why* a credential is trustworthy — surfacing four independent validation
layers behind a clean verdict with progressive drill-down. "Open" means the verifier requires no
pre-shared list of trusted issuers: it resolves and verifies everything reachable from the presented
credential, names the issuer, and leaves the "is this issuer acceptable" judgement to the operator.

### User Story 1 - Ask a minimal-disclosure question and get a clear verdict (Priority: P1)

An operator (e.g. a counter clerk) opens the verifier, picks a preset question such as "Age over 18?",
and shows the citizen a QR code. The citizen scans it with their wallet, approves sharing only the
answer to that question plus their portrait, and the operator immediately sees a clear **Over 18 ✓**
verdict with the portrait and the issuer's name.

**Why this priority**: This is the core value — a usable, minimal-disclosure verification that works
end to end. Without it nothing else matters. It is also the smallest viable demo.

**Independent Test**: Run the verifier, choose "Age over 18?", present a matching credential from the
wallet, and confirm the verdict screen shows the pass/fail result, the portrait, and the issuer name —
and that only the age answer and portrait were shared (nothing else).

**Acceptance Scenarios**:

1. **Given** the verifier is open on the Ask screen, **When** the operator selects "Age over 18?" and starts, **Then** a scannable QR request is shown that asks only for the age-over-18 answer and the portrait.
2. **Given** the QR is displayed, **When** the citizen approves the request in their wallet, **Then** the operator sees a verdict screen with an unambiguous pass/fail headline, the portrait, and the issuing organisation's name within a few seconds.
3. **Given** a verdict is shown, **When** the operator inspects what was shared, **Then** only the requested answer and portrait are present and all other identity attributes were withheld.

---

### User Story 2 - Inspect the full validation trail (Priority: P2)

After a verdict appears, the operator (or an auditor) expands the result to understand exactly what was
proven: that the live holder presented it, who signed it, that it has not been revoked, and that it is
genuinely recorded on the public register. Each check is shown as a step that can be opened for its raw
detail.

**Why this priority**: This is the trust story that differentiates the open verifier from a black-box
pass/fail. It is highly valuable but depends on Story 1 existing first.

**Independent Test**: Complete a verification, then open each trail step and confirm each shows its
result and, when expanded, the underlying detail (protocol, issuer identity, revocation status,
register anchor).

**Acceptance Scenarios**:

1. **Given** a completed verification, **When** the operator views the verdict, **Then** four validation steps are listed — live presentation, issuer signature, revocation status, register anchor — each with a clear pass/fail/attention indicator.
2. **Given** the trail is shown, **When** the operator expands the "selective disclosure" detail, **Then** both the disclosed attributes and the withheld attributes are listed, making minimal disclosure visible.
3. **Given** the trail is shown, **When** the operator expands any step, **Then** that step reveals its human-readable supporting detail and collapses again on demand.

---

### User Story 3 - Cross-check the credential against the public register anchor (Priority: P2)

The operator taps an explicit "verify against the register" action on the verdict. The verifier locates
the credential's record on the public register, confirms it was genuinely recorded (inclusion proof),
and shows the anchoring detail. The operator can export a portable, offline-checkable proof bundle.

**Why this priority**: This is the headline "open verifier reads the public register" capability. It is
the most novel part and the reason for the feature, but it builds on Stories 1 and 2.

**Independent Test**: Complete a verification of a credential that is anchored on a public register,
trigger the register cross-check, and confirm the verifier reports the credential as anchored (with the
docket/seal detail) and offers an exportable proof bundle. Confirm a credential with no resolvable
anchor reports "not anchored" rather than failing silently.

**Acceptance Scenarios**:

1. **Given** a verified credential that carries its register anchor reference, **When** the operator triggers the register cross-check, **Then** the verifier confirms the credential is recorded on the public register and shows the sealing detail.
2. **Given** the register cross-check has succeeded, **When** the operator chooses to export, **Then** a portable verification bundle is produced that can be re-checked without contacting the verifier.
3. **Given** a credential whose anchor cannot be resolved on the register, **When** the cross-check runs, **Then** the verifier clearly reports the anchor as unverified without contradicting the other passing checks.

---

### User Story 4 - Install the verifier as an app (Priority: P3)

A user installs the verifier to their device home screen and launches it as a standalone app (its own
window/icon, branded splash), rather than a browser tab.

**Why this priority**: Installability is the framing ask and improves the demo's feel, but the
verification value (Stories 1–3) stands without it.

**Independent Test**: Open the verifier in a supported browser, confirm an install affordance appears,
install it, and confirm it launches as a standalone app reachable under the existing verifier address.

**Acceptance Scenarios**:

1. **Given** the verifier is opened in a supported browser, **When** the page loads, **Then** an install affordance is offered.
2. **Given** the verifier is installed, **When** the user launches it from the home screen, **Then** it opens in a standalone window with the verifier branding and reaches the Ask screen.

---

### Edge Cases

- The citizen declines disclosure or closes the wallet → the verifier shows a non-alarming "no response / declined" state, not an error, and the operator can restart.
- The presented credential does not match the requested question (wrong type or missing the requested answer) → the verdict is a clear fail with the reason, not a crash.
- The credential's issuer signature cannot be verified (issuer identity unresolvable) → the verifier fails closed for that layer and says so plainly, while still showing whatever else it could determine.
- The credential is signature-valid but revoked → the revocation step fails and the overall verdict reflects "not valid", even though the signature passed.
- The credential is valid and not revoked but has no resolvable register anchor → the anchor step is "unverified" while the other steps stand; the overall result distinguishes "could not anchor" from "failed".
- The request/QR expires before the citizen responds → the verifier offers to regenerate.
- The verifier is launched offline → it informs the operator that a connection is required (this verifier consults public data live).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The verifier MUST let an operator choose what to verify from preset questions (including at minimum "Age over 18?" and "Confirm identity") plus a custom option, where each preset maps to a specific credential type and a minimal set of requested attributes.
- **FR-002**: The verifier MUST present the chosen request to the citizen as a cross-device, scannable code using the established presentation flow, requesting only the attributes the question requires.
- **FR-003**: The verifier MUST receive the citizen's presentation and produce a single, unambiguous pass/fail verdict, displayed with the portrait (when disclosed) and the issuing organisation's identity.
- **FR-004**: The verifier MUST verify that the live holder presented the credential (presentation freshness and holder binding) and surface this as the "live presentation" layer.
- **FR-005**: The verifier MUST verify the issuer's signature by resolving the issuer's published identity (no pre-shared allowlist) and surface this as the "issuer signature" layer, failing closed if the signature cannot be verified.
- **FR-006**: The verifier MUST check the credential's revocation status against its referenced public status list and surface this as the "not revoked" layer.
- **FR-007**: The verifier MUST display which attributes were disclosed and which were withheld, making minimal disclosure visible to the operator.
- **FR-008**: The verifier MUST offer an explicit action to cross-check the credential against its anchor on the public register, confirming the credential was genuinely recorded (inclusion proof) and showing the sealing detail.
- **FR-009**: The verifier MUST resolve the register anchor using only references carried by the credential itself (its register identifier and credential identifier), requiring no operator configuration.
- **FR-010**: The system MUST provide a public way to locate a credential's issuance record on a register from the credential's own identifiers, so the anchor cross-check needs no privileged access.
- **FR-011**: The verifier MUST allow exporting a portable verification bundle that can be re-checked offline without contacting the verifier.
- **FR-012**: The verifier MUST present results as a clean verdict with each validation layer collapsible (progressive disclosure), each expandable to its raw supporting detail.
- **FR-013**: The verifier MUST clearly distinguish "failed" from "could not determine/unverified" for each layer, and reflect that distinction in the overall result.
- **FR-014**: The verifier MUST be installable to a device as a standalone app, launching to its Ask screen under the existing verifier address.
- **FR-015**: The verifier MUST surface the issuer's identity prominently and MUST NOT assert issuer reputation or trustworthiness — the trust judgement remains with the operator.
- **FR-016**: The "Age over 18?" question MUST be answerable by disclosing a pre-issued boolean age answer (plus portrait), without revealing date of birth, name, or address.

### Key Entities *(include if feature involves data)*

- **Verification Question (preset)**: A named question the operator can ask; maps to a required credential type and the minimal attributes requested.
- **Presentation Session**: A single in-progress verification — the request shown as a code, awaiting the citizen's response, resolving to an outcome.
- **Verdict**: The overall pass/fail result plus the disclosed attributes (portrait, the requested answer) and the issuer identity.
- **Validation Layer Result**: Per-layer outcome (live presentation, issuer signature, revocation, register anchor) with status and human-readable detail.
- **Register Anchor Reference**: The credential-carried pointer (register identifier + credential identifier) used to locate the issuance record on the public register.
- **Verification Bundle**: A portable, offline-checkable package summarising the verified credential and its proofs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can go from opening the app to a displayed verdict for "Age over 18?" in under 60 seconds, with no configuration beyond choosing the question.
- **SC-002**: For an over-18 check, only the age answer and portrait are shared; at least three other identity attributes (name, date of birth, address) are demonstrably withheld and shown as withheld.
- **SC-003**: The verdict screen shows all four validation layers, and each can be expanded to its supporting detail and collapsed again.
- **SC-004**: For an anchored credential, the operator can confirm register anchoring and export a bundle that independently re-verifies without contacting the verifier.
- **SC-005**: A revoked-but-validly-signed credential yields an overall "not valid" verdict with the revocation layer failing and the signature layer passing.
- **SC-006**: The verifier can be installed and launched as a standalone app on at least one supported desktop and one supported mobile browser.

## Assumptions

- The existing cross-device presentation transport (QR / direct response) is reused unchanged; this feature redesigns the operator-facing screens and adds the register cross-check, not the wire protocol.
- The motivating credential (Assured Identity) is issued with a pre-issued boolean age answer, a portrait, and a register-anchor reference; producing such a credential for the demo is in scope as setup.
- The issuing organisation has a properly published issuer identity so its signature is verifiable; an issuer without one is a setup error, not a verifier defect.
- The verifier consults public data (register, status list, issuer identity) live and therefore requires connectivity; offline/on-device verification is explicitly out of scope (roadmap).
- Hard trusted-issuer allowlists, zero-knowledge age predicates, the external certificate (EUDI) trust rail, and alternate credential formats are out of scope for this feature.
- The verifier reaches the public register and status-list endpoints over their public addresses; ensuring that path is reachable in the demo environment is a deployment concern handled in planning.
