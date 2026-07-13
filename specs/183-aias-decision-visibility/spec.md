# Feature Specification: AIAS decision integrity & visibility

**Feature Branch**: `183-aias-decision-visibility`

**Created**: 2026-07-12

**Status**: Draft

**Input**: Fix the AIAS Assured Identity (Feature 174 / M1) web-app happy path — every real citizen application is currently auto-rejected — and make the reject outcome visible to the applicant. Approved design: `docs/superpowers/specs/2026-07-12-aias-emailverified-claim-source-design.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A genuine applicant receives their credential (Priority: P1)

A citizen with a verified email account applies to Acme Identity Assurance Services (AIAS) for an Assured Identity through the web app: they fill in their name, date of birth, a real address, their email, and optionally a photo, then submit. The autonomous Assure-ID agent assesses the application and, when everything checks out (real postcode, clean language, verified email), approves it and issues the applicant an AssuredIdentityCredential into their wallet.

**Why this priority**: This is the demo's core promise and it is currently 100% broken — *every* application submitted through the web UI is rejected on the email-verification check, so no real user can ever receive a credential. Nothing else in the feature matters until a genuine applicant can succeed.

**Independent Test**: Sign up a fresh citizen, verify their email, submit an AIAS application with a real UK postcode and a photo, and confirm the agent approves and the credential is delivered to the wallet — no manual payload manipulation required.

**Acceptance Scenarios**:

1. **Given** a citizen whose email is verified, **When** they submit an AIAS application with a real postcode and clean details, **Then** the agent approves it and an AssuredIdentityCredential is delivered to their wallet.
2. **Given** a citizen whose email is NOT verified, **When** they submit an otherwise-valid AIAS application, **Then** the agent rejects it with the email-verification reason and no credential is issued.
3. **Given** any citizen applicant, **When** the application is submitted, **Then** the applicant's true email-verified status is carried on the submission as part of the tamper-evident record (not inferred or defaulted).

---

### User Story 2 - A rejected applicant learns why (Priority: P2)

When AIAS rejects an application, the applicant is told — durably — that a decision was made and why (the on-brand reason, e.g. *"AIAS needs a verified email before it can assure you. Confirm your email and reapply."*). The notice is waiting for them in their notifications even if they had navigated away, logged out, or switched device.

**Why this priority**: A reject route the applicant can't see is a black hole — the reported experience was "I had no idea why I was failing." Making rejection visible is what turns the reject path into a real, demonstrable outcome. It depends on P1 being correct (so rejections are genuine), hence P2.

**Independent Test**: Cause a gate rejection, then confirm a durable notification carrying the on-brand reason appears for the applicant and survives a page reload and re-login.

**Acceptance Scenarios**:

1. **Given** an application that the agent rejects, **When** the decision is recorded, **Then** the applicant receives a durable notification whose content includes the on-brand rejection reason.
2. **Given** a rejection notification was created, **When** the applicant reloads the app or signs in again (including on another device), **Then** the notification and its reason are still present.
3. **Given** the notification-writing step fails for any reason, **When** the decision is processed, **Then** the underlying workflow decision still completes normally (the notification is best-effort and never blocks or reverses the decision).
4. **Given** an application that the agent approves, **When** the decision is recorded, **Then** the applicant is guided toward claiming their credential through the existing notification surfaces (no new duplicate approval notice is created).

---

### Edge Cases

- **Session lost / expired at submit time**: if the applicant's verified status cannot be determined at submission, the application is treated as **not** verified (fail closed) rather than assumed verified.
- **Field carrying claim data is on no visible form page**: the applicant's verified status must still be captured on the submission even though there is no on-screen control for it.
- **A blueprint reject route with no recipient or no reason field configured**: no notification is attempted; the decision still completes.
- **Repeated processing of the same decision** (retries, replays): the applicant sees at most one notification per decision, not duplicates.
- **The applicant is not the account that started the workflow**: the notification is addressed to the participant who actually started the application, resolved from the workflow's own participant bindings.

## Requirements *(mandatory)*

### Functional Requirements

#### Decision integrity (US1)

- **FR-001**: The system MUST carry the applicant's real email-verified status on every web-submitted AIAS application, sourced from the authenticated account's verified state at submission time.
- **FR-002**: The carried email-verified status MUST be part of the tamper-evident submission record (covered by the applicant's signature), not derived after the fact by any downstream service.
- **FR-003**: When the applicant's verified status cannot be determined, the system MUST record it as *not verified* (fail closed).
- **FR-004**: The mechanism that carries account-derived status onto a submission MUST be reusable — declared on the application definition, not hard-coded to this one field — so future applications can carry other account-derived values the same way.
- **FR-005**: A genuine, verified applicant with a valid application MUST result in approval and credential delivery; an unverified applicant MUST result in rejection with the email-verification reason.

#### Decision visibility (US2)

- **FR-006**: When an application is rejected via a terminal reject outcome that opts in, the system MUST create a durable notification for the applicant who started the workflow.
- **FR-007**: The rejection notification MUST include the on-brand reason recorded with the decision.
- **FR-008**: The notification MUST persist across sessions and devices (survive reload, logout/login, and device switch).
- **FR-009**: The recipient of the notification MUST be resolved as the workflow's starting participant, using the workflow's own participant bindings.
- **FR-010**: Notification creation MUST be best-effort — a failure to write the notification MUST NOT block, delay, or reverse the underlying decision or workflow progression.
- **FR-011**: Repeated processing of the same decision MUST NOT produce duplicate notifications.
- **FR-012**: Which outcomes notify, who they address, the reason source, and the notice title/severity MUST be declared on the application definition (per reject route), not hard-coded.
- **FR-013**: Approval outcomes MUST continue to be surfaced through the existing notification surfaces; this feature MUST NOT add a duplicate approval notice.

#### Regression protection (both stories)

- **FR-014**: The demo rehearsal harness MUST exercise the real web-submission shape — it MUST NOT hard-code the email-verified value — and MUST include a case that submits with no verified status and asserts rejection with no credential.
- **FR-015**: Automated tests MUST prove the account-derived value is carried onto a submission even when its field appears on no visible form page, and MUST cover verified→carried-true, unverified→carried-false, missing→fail-closed-false, and field-without-the-binding→not-carried.
- **FR-016**: Automated tests MUST prove that an opted-in terminal reject creates exactly one applicant notification carrying the reason, that a non-opted-in route creates none, and that a notification-write failure does not fail the decision.

### Key Entities *(include if feature involves data)*

- **Claim-source binding**: a declaration on an application field naming which account-derived value (a verified-status signal) should be stamped onto the submission for that field. Reusable across applications.
- **Decision notice declaration**: a declaration on a reject outcome naming the recipient participant, the reason to surface, and the notice's title and severity.
- **Applicant decision notification**: a durable, per-user notification record carrying a title, the on-brand reason, a severity, and a link back to the application — rendered in the user's existing notifications surface.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fresh, email-verified citizen can go from sign-up to holding an AssuredIdentityCredential through the web app in a single sitting, with no manual data manipulation — reproducible on the live environment.
- **SC-002**: 100% of genuine verified applications with valid details are approved (the prior rate was 0%).
- **SC-003**: An unverified applicant is rejected with the email reason, demonstrating the gate is real rather than always-approve.
- **SC-004**: A rejected applicant can find the reason for rejection in their notifications within one interaction of the decision, and the reason is still there after a reload and a re-login.
- **SC-005**: No decision or workflow progression is ever blocked, delayed, or reversed by the notification step (verified by fault-injection test).
- **SC-006**: The demo rehearsal passes both an approval case and an unverified-rejection case that mirror the real web-submission shape.

## Assumptions

- The authenticated account already carries its email-verified status in a form the web client can read at submission time (established by the existing platform auth). This feature consumes that; it does not add new email-verification flows.
- Approval is already surfaced to the applicant through existing notification surfaces (a claim prompt when the credential is ready, and a credential-received notice on delivery); only the *reject* path lacks a durable, reasoned notification today.
- The AIAS application definition is re-provisioned on the target environment so it carries the new declarations; delivery is a redeploy of the affected components plus that re-provision, with no data reset.
- A citizen "My Applications" history page, email-on-decision, and generalising the notice recipient beyond an explicitly named participant are out of scope and tracked as a follow-up.
- The two already-working reject routes (non-existent postcode, profanity) and the autonomous agent's decision rules are unchanged.
