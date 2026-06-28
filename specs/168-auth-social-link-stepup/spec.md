# Feature Specification: Auth Hardening B-Backend — Step-Up-Gated Social Account Linking

**Feature Branch**: `168-auth-social-link-stepup`

**Created**: 2026-06-27

**Status**: Draft

**Input**: User description: "Auth hardening B backend: replace silent social auto-link with a link-pending token + LinkSocial step-up challenge + link-confirm endpoint. Spec: docs/superpowers/specs/2026-06-27-auth-login-hardening-design.md Workstream B-backend"

## Overview

Today, when a person signs in with a social provider (Google / Microsoft / GitHub / Apple) that
is **not yet connected** to any Sorcha account, but the verified email from that provider matches
an **existing** Sorcha account, the platform **silently links** the social identity to that account
and signs the person straight in. This is a security hole: anyone who controls a social account
whose email happens to match a Sorcha account email gains control of the Sorcha account, with **no
proof of the existing account** ever required.

This feature replaces that silent auto-link with an explicit, **step-up-gated** linking flow on the
**server side**. When an unconnected social sign-in matches an existing account, the platform no
longer issues a session. Instead it returns a "link required" outcome carrying a short-lived,
signed **link-pending token**. To complete the link the person must first **prove ownership of the
existing account** (passkey, an already-linked social, or password + 2FA) via the existing step-up
challenge mechanism, then redeem both proofs at a new **link-confirm** endpoint, which performs the
link and issues the session.

This specification covers **only the backend (Tenant Service) workstream (B-backend)**. The
accompanying user-facing prompt component, the shared-component consolidation, the social-icon work
(Workstream A), and the passkey auth-state bug (Workstream C) are out of scope here and are tracked
separately.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Unconnected social matching an existing account requires proof before linking (Priority: P1)

A person already has a Sorcha account (created with, say, email + password, or a different social
provider). They sign in using a **new** social provider that the account has never connected, whose
verified email matches their existing account. Instead of being silently linked and logged in, the
platform recognises the match and asks them to **prove** they own the existing account before it
connects the new social identity. Only after a successful proof is the link made and a session issued.

**Why this priority**: This is the core security fix and the entire reason for the feature. Without
it, the silent auto-link vulnerability remains. Everything else is supporting machinery.

**Independent Test**: Drive the social callback for an account that has an existing record with a
matching verified email and an unconnected `(provider, subject)`. Confirm **no session/JWT is
issued** and a "link required" outcome with a link-pending token is returned. Then complete a valid
step-up challenge, redeem it at link-confirm, and confirm the social identity is now linked and a
full session is issued.

**Acceptance Scenarios**:

1. **Given** an existing account with a verified email and no link to `(provider, subject)`, **When**
   the person completes a social sign-in with that provider whose verified email matches the account,
   **Then** the platform returns a **LinkRequired** outcome with a signed, short-lived link-pending
   token and issues **no** session.
2. **Given** a valid link-pending token, **When** the person presents it together with a successfully
   completed step-up challenge proving the existing account, **Then** the platform links the social
   identity to the existing account and issues a full session (the same session it would have issued
   for a normal social sign-in).
3. **Given** the link has been completed, **When** the same person later signs in with that same
   social provider, **Then** they are recognised as already-linked and signed in directly with **no**
   further step-up prompt (the link is permanent).

---

### User Story 2 - Linking is refused without a valid, matching proof (Priority: P1)

The link must only happen when the proof genuinely belongs to the **same** existing account that the
link-pending token targets. An attacker who holds only the matching social account — but cannot prove
the existing Sorcha account — must be unable to complete the link.

**Why this priority**: This is the safety property that makes Story 1 meaningful. If the proof check
can be bypassed, skipped, replayed, or satisfied against a different account, the security fix is
defeated.

**Independent Test**: Attempt link-confirm with (a) no challenge proof, (b) an expired link-pending
token, (c) a challenge proof scoped to a different operation, and (d) a challenge proof belonging to
a different account than the link-pending token targets. Confirm each is rejected and no link is made.

**Acceptance Scenarios**:

1. **Given** a valid link-pending token, **When** link-confirm is called **without** a step-up
   challenge proof, **Then** the request is rejected (unauthorized) and no link is made.
2. **Given** a link-pending token that has expired, **When** link-confirm is called with any proof,
   **Then** the request is rejected and no link is made.
3. **Given** a valid link-pending token for account A, **When** link-confirm is called with a step-up
   challenge proof that belongs to account B, **Then** the request is rejected (the challenge subject
   must equal the link-pending token's target account) and no link is made.
4. **Given** a step-up challenge proof that was issued for a different operation (not social linking),
   **When** it is presented to link-confirm, **Then** it is rejected (proofs are operation-scoped).
5. **Given** a link-pending token whose signature has been tampered with, **When** it is presented to
   link-confirm, **Then** it is rejected.

---

### User Story 3 - Step-up proof strength matches the account's own configured methods (Priority: P2)

The strength of proof demanded must be the strongest the account actually has, and must not demand
more than the account is configured with. If the account has 2FA enrolled, a password alone is not
enough (password **and** 2FA are required). If the account has only a password and no 2FA, the
password is the account's existing strength and is accepted on its own. A passkey or an
already-linked social (re-authenticating with it) is also an accepted proof.

**Why this priority**: A clear, testable policy prevents the step-up bar from silently drifting and
ensures the experience is neither insecure (too weak) nor impossible (demanding a method the account
doesn't have). It builds on Story 1/2 but is a refinement of the proof rules.

**Independent Test**: For each account configuration — (passkey), (linked social), (password only,
no 2FA), (password + 2FA), (password + 2FA + passkey) — initiate a social-link step-up and confirm
the offered/accepted proof method matches the stated policy.

**Acceptance Scenarios**:

1. **Given** an existing account with a passkey, **When** a social-link step-up is initiated, **Then**
   a passkey proof is accepted to complete the link.
2. **Given** an existing account with another social already linked, **When** a social-link step-up is
   initiated, **Then** re-authenticating with that linked social is an accepted proof.
3. **Given** an existing account with a password and **2FA enrolled**, **When** a social-link step-up
   is initiated, **Then** a password alone is **insufficient** and password + 2FA is required.
4. **Given** an existing account with a password and **no 2FA**, **When** a social-link step-up is
   initiated, **Then** the password alone is an accepted proof (the bar is not raised above the
   account's own configured methods).

---

### User Story 4 - Cancelling the link leaves both accounts untouched (Priority: P2)

If the person decides not to link (or simply abandons the flow), nothing is connected and no session
is created. The link-pending token expires harmlessly. Their existing account and the social identity
remain exactly as they were.

**Why this priority**: A clean abort path is required for both safety and usability — an abandoned or
declined link must never partially mutate state.

**Independent Test**: Obtain a link-pending token via Story 1, then never call link-confirm (or let
the token expire). Confirm no link row is created, no session is issued, and the account is unchanged.

**Acceptance Scenarios**:

1. **Given** a link-pending token, **When** link-confirm is never called and the token expires,
   **Then** no link exists, no session was issued, and the account is unchanged.
2. **Given** an expired link-pending token, **When** it is later presented to link-confirm, **Then**
   the request is rejected and no state changes.

---

### User Story 5 - No-match and already-linked behaviour is preserved (Priority: P3)

The change must be surgical: it only affects the *unconnected-social-matching-an-existing-account*
case. A social sign-in that matches **no** account, and a social sign-in whose `(provider, subject)`
is **already linked**, must behave exactly as they do today.

**Why this priority**: Guards against regression. The new flow must not alter the two unaffected paths
(new-account creation / login-only refusal, and direct already-linked sign-in).

**Independent Test**: Drive the social callback for (a) an email matching no account and (b) an
already-linked `(provider, subject)`. Confirm identical behaviour to today — respectively account
creation (or login-only refusal on wallet surfaces) and a direct sign-in.

**Acceptance Scenarios**:

1. **Given** a social sign-in whose verified email matches **no** existing account on an
   account-creation surface, **When** the callback completes, **Then** a new account is created and a
   session is issued (unchanged from today).
2. **Given** a social sign-in whose verified email matches no existing account on a login-only
   surface (e.g. the citizen wallet), **When** the callback completes, **Then** it is refused exactly
   as today (no account is created).
3. **Given** a social sign-in whose `(provider, subject)` is already linked, **When** the callback
   completes, **Then** the person is signed in directly with no link prompt (unchanged from today).

---

### Edge Cases

- **Provider email unverified**: If the incoming social profile does not assert a verified email, the
  match-and-link path must not be entered — the platform must not treat an unverified social email as
  proof of identity overlap. (Existing refusal behaviour for unverified provider email is preserved.)
- **Existing account email unverified**: If the matching existing account's own email is not verified,
  the linking path must not be entered (preserving existing refusal behaviour).
- **Concurrent link attempts / race**: Two link-confirm attempts for the same `(provider, subject)`
  must result in at most one link; a losing race must surface a collision rather than a duplicate.
- **`(provider, subject)` already linked to a different account** by the time link-confirm runs: must
  surface a collision (conflict), not silently overwrite.
- **Provider email now belongs to a different account** by the time link-confirm runs: must surface a
  collision (conflict), not merge.
- **Reused / replayed link-pending token**: a token is single-purpose and short-lived; expiry and the
  paired step-up challenge bound its usefulness.
- **Clock skew on token expiry**: expiry is enforced server-side; tokens are short-lived (~5 minutes).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When a social sign-in presents an **unconnected** `(provider, subject)` whose
  **verified** email matches an **existing** account with a **verified** email, the system MUST NOT
  silently link the identities and MUST NOT issue a session.
- **FR-002**: In the FR-001 case, the system MUST return a distinct **LinkRequired** outcome that
  carries a signed, short-lived **link-pending token**.
- **FR-003**: The link-pending token MUST encode the social provider, the provider subject, the
  social email, the display name, and the **target existing account identifier**, and MUST carry a
  short expiry (~5 minutes).
- **FR-004**: The link-pending token MUST be integrity-protected (signed) such that any tampering with
  its contents or expiry causes validation to fail. It MUST be verifiable server-side without new
  persistent storage.
- **FR-005**: The system MUST provide a new endpoint that accepts a link-pending token **and** a
  step-up challenge proof, and only on success links the social identity to the target account and
  issues a full session.
- **FR-006**: The link-confirm endpoint MUST reject the request when no valid step-up challenge proof
  is supplied.
- **FR-007**: The link-confirm endpoint MUST assert that the step-up challenge proof's subject is the
  **same account** that the link-pending token targets; a mismatch MUST be rejected.
- **FR-008**: The step-up challenge proof MUST be scoped to the social-linking operation; a proof
  issued for any other operation MUST be rejected (no cross-operation replay).
- **FR-009**: The system MUST define a dedicated step-up operation for social linking and surface it
  through the existing challenge mechanism so the standard proof methods (passkey, re-authenticated
  linked social, password, 2FA) apply.
- **FR-010**: The accepted proof for social linking MUST follow this policy: **passkey** is accepted;
  **re-authentication with an already-linked social** is accepted; if the account has **2FA enrolled**,
  password alone is **insufficient** (password **and** 2FA required); if the account has a password and
  **no 2FA**, **password alone** is accepted. The bar MUST NOT be raised above the methods the account
  is actually configured with.
- **FR-011**: On successful link-confirm, the system MUST issue the **same** session/token that a
  normal successful social sign-in issues for that account.
- **FR-012**: The link operation invoked by link-confirm MUST reuse the existing linking logic and its
  collision results: an `(provider, subject)` already linked to a different account, or a provider
  email already belonging to a different account, MUST surface as a **conflict** rather than a link.
- **FR-013**: When the social email matches **no** existing account, behaviour MUST be unchanged: an
  account-creation surface creates a new account and signs in; a login-only surface (e.g. citizen
  wallet) is refused as today.
- **FR-014**: When `(provider, subject)` is **already linked**, behaviour MUST be unchanged: the
  person is signed in directly with no link prompt.
- **FR-015**: An expired, tampered, malformed, or absent link-pending token at link-confirm MUST be
  rejected with no state change.
- **FR-016**: Cancelling or abandoning the link (never calling link-confirm) MUST leave the existing
  account and the social identity unchanged; the link-pending token simply expires.
- **FR-017**: The new outcome and its refusal/branch reasons SHOULD be recorded in the existing
  social-login telemetry so the change is observable (e.g. counts of LinkRequired, link-confirm
  success, link-confirm conflict, link-confirm rejected).
- **FR-018**: The link-confirm endpoint MUST be governed by the platform's standard rate-limiting and
  MUST return appropriate, non-leaky status codes (unauthorized vs forbidden vs conflict) so failures
  are testable and do not reveal whether a target account exists beyond what the social flow already
  exposes.

### Key Entities *(include if feature involves data)*

- **Link-pending token**: A signed, short-lived, stateless credential representing "this verified
  social identity matched this existing account; linking is pending proof". Encodes provider, subject,
  social email, display name, target account identifier, and expiry. Not persisted.
- **Step-up challenge proof (LinkSocial scope)**: An operation-scoped credential proving the person
  controls the existing account, produced by the existing challenge mechanism using whichever proof
  method the account supports under the FR-010 policy.
- **Social link**: The durable association between an existing account and a `(provider, subject)`
  social identity, created only on successful link-confirm and subject to existing collision rules.
- **Existing account (target)**: The Sorcha account whose verified email matched the incoming social
  email; the subject of both the link-pending token and the required proof.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of unconnected-social sign-ins that match an existing verified account result in a
  LinkRequired outcome with **no** session issued (zero silent auto-links) — verified by integration
  tests covering the matching path.
- **SC-002**: 0% of link completions occur without a valid, account-matching, operation-scoped step-up
  proof — every rejection case (absent proof, expired token, wrong account, wrong operation, tampered
  token) is covered by a passing test and results in no link.
- **SC-003**: A legitimate account owner can complete the link in a single step-up within the token's
  ~5-minute validity window, after which they are signed in.
- **SC-004**: The two unaffected paths (no-match → create/refuse; already-linked → direct sign-in)
  show **no behavioural change**, demonstrated by tests that pass identically to pre-change behaviour.
- **SC-005**: The step-up proof policy (FR-010) is enforced exactly as stated across all five account
  configurations, each covered by a unit test, so the bar cannot drift silently.
- **SC-006**: Collisions at link-confirm time (already-linked-elsewhere, email-belongs-to-another)
  surface as conflicts and never produce a duplicate or overwritten link.

## Assumptions

- The existing OAuth provider integration, the step-up challenge ladder/service, the TOTP engine, and
  the social-link logic are reused unchanged; this feature adds an outcome, a token, an operation
  scope, and one endpoint — it does not rewrite those subsystems.
- The link-pending token reuses the same deployment-stable HMAC signing approach already used for the
  short-lived login token, keeping the design stateless (no new persistence).
- "Matching account" means the verified social email equals the verified email of exactly one existing
  account; provider-unverified and existing-unverified emails do not enter the linking path
  (preserving today's refusals).
- The session/JWT issued on successful link-confirm is identical to the one issued for a normal social
  sign-in for that account (same tier/audience derivation as today).
- Account-creation vs login-only surface behaviour (web creates; citizen wallet refuses) is determined
  exactly as it is today and is unchanged by this feature.
- This workstream is **backend-only**. The user-facing link prompt, the shared Security components, the
  social icons (Workstream A), and the passkey auth-state bug (Workstream C) are out of scope and
  tracked separately.
- The user-visible behaviour change (existing users who relied on silent linking now see a one-time
  step-up) is intended and will be reflected in the authentication documentation.

## Out of Scope

- The user-facing `LinkExistingAccountPrompt` component and any host/page that renders it (Workstream
  B-UI).
- Consolidation of proactive management surfaces (add-social, 2FA enrol, passkey management) into the
  shared component library (Workstream B-management).
- Social provider brand icons (Workstream A) and the passkey "not logged in" auth-state bug
  (Workstream C).
- App/PWA parity for any of the above (Step 2 of the parent design).
- Changes to OAuth provider integration, the challenge ladder internals, or the TOTP engine.
