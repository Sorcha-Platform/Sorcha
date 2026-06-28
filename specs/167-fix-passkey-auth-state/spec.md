# Feature Specification: Fix Passkey Login Auth-State Notification (Auth Hardening C)

**Feature Branch**: `167-fix-passkey-auth-state`

**Created**: 2026-06-27

**Status**: Draft

**Input**: User description: "Auth hardening C: fix passkey login leaving web Profile/Security pages showing not-signed-in; re-notify auth state after fragment-token consume. Spec: docs/superpowers/specs/2026-06-27-auth-login-hardening-design.md Workstream C"

## Context

This is **Workstream C** of the Auth Login Hardening effort. It addresses a specific, reproducible
defect in the **web** Sorcha UI: completing a passkey (WebAuthn) sign-in returns the user to the app,
but parts of the app — notably the **Profile** and **Security** pages — continue to render as though
the user is *not signed in*, even though a valid session token has in fact been received and accepted.

The sign-in handoff delivers the freshly issued token to the web app, which consumes it and establishes
the authenticated session. The defect is that the app does not consistently **re-announce** the change
from "not signed in" to "signed in" to the parts of the UI that were already rendered. Pages that had
already decided the user was anonymous keep that stale decision until the user manually reloads or
navigates again.

> **Note on referenced design doc**: The cited design document
> (`docs/superpowers/specs/2026-06-27-auth-login-hardening-design.md`, Workstream C) was not present in
> the repository at the time this spec was authored. This specification was derived from the feature
> description and the observed behaviour of the existing web sign-in handoff. If the design document is
> added later, this spec should be reconciled against it.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Passkey sign-in lands on a correctly-signed-in app (Priority: P1)

A returning user signs in to the web application using their passkey. After the passkey ceremony
completes and they are returned to the app, every page they visit — including Profile and Security —
reflects that they are signed in, without requiring a manual page reload.

**Why this priority**: This is the core defect. A user who has successfully authenticated but is shown
"not signed in" experiences the product as broken and may believe their credentials failed, eroding
trust in the strongest available authentication method (passkeys). It must be fixed first.

**Independent Test**: Sign in with a passkey on the web app, then navigate directly to the Profile page
and the Security page. Both must render the signed-in experience (the user's profile / their security
settings), not an anonymous or "not signed in" state, with no manual refresh.

**Acceptance Scenarios**:

1. **Given** a user with a registered passkey who is not currently signed in, **When** they complete a
   passkey sign-in and are returned to the web app, **Then** the application reflects a signed-in state
   immediately, without a manual page reload.
2. **Given** a user has just completed passkey sign-in and arrived in the app, **When** they open the
   Profile page, **Then** their profile is shown (signed-in experience), not a "not signed in" state.
3. **Given** a user has just completed passkey sign-in and arrived in the app, **When** they open the
   Security page, **Then** their security settings are shown (signed-in experience), not a "not signed
   in" state.
4. **Given** a user is returned to the app after sign-in and lands on a specific destination page
   (their intended return location), **When** that page first renders, **Then** it renders as signed-in.

---

### User Story 2 - Sign-in via other methods continues to land signed-in (Priority: P2)

A user who signs in by any supported method that uses the same post-login handoff (e.g. social/SSO or
password sign-in that returns to the web app the same way) also arrives at a correctly-signed-in app,
with Profile and Security pages reflecting the signed-in state.

**Why this priority**: The fix should harden the shared post-login handoff, not just the passkey path.
Verifying the other entry points that share the handoff prevents the same class of defect resurfacing
through a different door. It is P2 because passkey is the reported, confirmed failure.

**Independent Test**: Repeat the User Story 1 navigation checks after signing in via each other method
that returns to the web app through the same handoff; all must show signed-in Profile and Security
pages without a reload.

**Acceptance Scenarios**:

1. **Given** a user signs in via a non-passkey method that uses the post-login handoff, **When** they
   are returned to the web app, **Then** the app reflects signed-in state without a manual reload.
2. **Given** such a user, **When** they open Profile or Security, **Then** each page renders the
   signed-in experience.

---

### Edge Cases

- **Already-signed-in user repeats sign-in (e.g. switches org/account)**: the app must reflect the new
  session, not a stale prior one, and must not show a transient "not signed in" state on Profile or
  Security after the new sign-in completes.
- **Handoff carries an expired or invalid token**: the app must remain in (or fall back to) a
  not-signed-in state and route the user to sign in again — it must not appear signed-in on the strength
  of an unusable token, and must not get stuck in a flapping/looping notification state.
- **No token is present in the handoff** (normal anonymous visit / direct navigation): pages that
  require sign-in must continue to behave as today (prompt sign-in); there must be no spurious
  "signed in then signed out" flicker introduced by the re-notification.
- **Re-notification fires more than once / token already consumed**: repeated or duplicate
  announcements must be safe (idempotent) and must not cause flicker, redundant network calls, or
  re-consumption of an already-consumed one-time token.
- **Page already rendered as anonymous before the token arrived**: such pages must update to the
  signed-in state once the session is established, without the user navigating or reloading.
- **Slow handoff**: if establishing the session takes a moment, pages may briefly show a loading state,
  but must resolve to signed-in (not to "not signed in") once the session is established.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: After the web app consumes a sign-in token delivered by the post-login handoff and
  establishes an authenticated session, the system MUST announce the authentication-state change to all
  parts of the UI so that already-rendered components update to the signed-in state.
- **FR-002**: Following a successful passkey sign-in, the Profile page MUST render the signed-in
  experience without requiring a manual page reload.
- **FR-003**: Following a successful passkey sign-in, the Security page MUST render the signed-in
  experience without requiring a manual page reload.
- **FR-004**: The auth-state announcement MUST be triggered only when a token is successfully consumed
  and a valid session is established; it MUST NOT announce a signed-in state for an absent, expired, or
  invalid token.
- **FR-005**: The auth-state announcement MUST be safe to occur when no fresh token is present (normal
  anonymous navigation) without producing a visible "signed in / signed out" flicker.
- **FR-006**: Re-announcement MUST be idempotent with respect to a single sign-in: a one-time token
  MUST NOT be consumed more than once, and repeated announcements MUST NOT cause flicker or redundant
  work.
- **FR-007**: The fix MUST cover all sign-in methods that return to the web app through the same
  post-login handoff, not only passkey.
- **FR-008**: Existing behaviour for users who are already signed in (including re-sign-in to switch
  org/account) MUST continue to work, with the app reflecting the most recent valid session and not a
  stale one.
- **FR-009**: The change MUST be limited to the web UI's post-login auth-state handling and MUST NOT
  alter how tokens are issued or how the handoff transports them.

### Key Entities *(include if feature involves data)*

- **Authenticated session indicator**: the app-wide signal of whether the current user is signed in and
  who they are. Consumed by pages and components (including Profile and Security) to decide between the
  signed-in and not-signed-in experience. The defect is that this signal is established but not
  re-broadcast to already-rendered consumers.
- **Post-login handoff token**: the one-time, freshly issued credential delivered to the web app when a
  sign-in completes. Consuming it establishes the authenticated session; it must be consumed at most
  once.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of passkey sign-ins on the web app result in the Profile and Security pages rendering
  the signed-in experience on first visit after sign-in, with no manual reload.
- **SC-002**: The number of "appears not signed in after a successful sign-in" occurrences on the web app
  drops to zero in verification testing (was reproducible before the fix).
- **SC-003**: A user reaches a correctly-signed-in destination page after passkey sign-in without any
  manual page reload or re-navigation step.
- **SC-004**: No regression: anonymous (not-signed-in) navigation shows no spurious signed-in flicker,
  and re-sign-in to switch org/account continues to reflect the new session correctly, across automated
  verification.
- **SC-005**: All sign-in methods that share the post-login handoff land on a signed-in Profile and
  Security page without reload in verification testing.

## Assumptions

- The reported defect is specific to the **web** Sorcha UI; the wallet PWA and other clients are out of
  scope for this workstream except where they share the exact same post-login handoff component.
- The token delivered by the post-login handoff is itself valid for the signed-in cases under test; this
  workstream addresses the *notification of state change*, not token issuance or transport.
- The existing one-time consumption semantics of the handoff token are correct and must be preserved;
  this change adds a re-announcement of the resulting state, not a second consumption.
- "Signed-in experience" for Profile and Security means the same authenticated content those pages show
  today once a session is correctly established; this workstream does not redesign those pages.
- The post-login handoff is shared across sign-in methods (passkey, social/SSO, password) that return to
  the web app, so fixing the shared handoff fixes the class of defect for all of them.
- Verification is performed in an environment where passkey sign-in can be exercised end-to-end against
  the web app.
