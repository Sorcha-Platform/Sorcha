# Feature Specification: Web Step-Up Social Account Linking (B-UI)

**Feature Branch**: `173-web-step-up-account-linking`

**Created**: 2026-06-28

**Status**: Draft

**Input**: User description: "B-UI web step-up social account-linking: LinkExistingAccountPrompt consuming the Feature 168 anonymous /api/auth/social/link initiate+verify+confirm endpoints (link-pending token as principal) + /app LinkRequired fragment handling; v1 proof methods TOTP+Passkey (defer ReOAuth); isolate anonymous step-up from the shipped authenticated AuthChallengeDialog; do NOT re-consolidate Security components (F150 already shipped them)"

## Overview

Feature 168 (B-backend) closed a security hole: when a person signs in with a social provider that is **not yet connected** to any account, but whose verified email matches an **existing** account, the platform no longer silently links and signs them in. Instead the server returns a **LinkRequired** outcome carrying a short-lived, signed **link-pending token**, and refuses to issue a session until the person **proves ownership of the existing account**.

Feature 168 delivered only the server side. The web user is currently handed back to `/app` with `outcome=LinkRequired&linkPendingToken=<token>` in the URL fragment and **no way to continue** — the flow dead-ends.

This feature delivers the **web user-facing half**: the `/app` (Blazor web) host detects the `LinkRequired` fragment, presents a dedicated prompt that walks the person through proving ownership of their existing account (using a passkey or an authenticator code), and on success completes the link and signs them in. This is a **purpose-built anonymous step-up surface** — it does NOT reuse or merge with the authenticated step-up dialog that ships for signed-in users managing their own security, and it does NOT re-consolidate the already-shipped account-security components.

This specification covers **only the web (`/app`) surface**. The citizen wallet PWA (`/wallet`) receives the same `LinkRequired` fragment but its prompt is tracked separately.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prove ownership and link a new social provider via passkey (Priority: P1)

A person already has an account (created with email + password, or a different social provider) and has a passkey registered. They sign in to the web app using a **new** social provider whose verified email matches their existing account. The server returns them to `/app` in a "link required" state. The web app recognises this, explains that an account already exists for that email and that they must confirm it's them before the new sign-in method is connected. The person completes a passkey check, the new social identity is linked, and they are signed straight into the web app.

**Why this priority**: This is the primary journey that makes the Feature 168 security fix usable on the web. Without it, anyone whose social email matches an existing account is permanently locked out of completing sign-in on the web. Passkey is the strongest, lowest-friction proof and the v1 happy path.

**Independent Test**: Drive a social sign-in that produces a `LinkRequired` outcome for an account with a registered passkey. Confirm the web app surfaces the prompt instead of dead-ending, complete the passkey ceremony, and confirm the social identity is linked and a full web session is established.

**Acceptance Scenarios**:

1. **Given** the web app loads at `/app` with a `LinkRequired` outcome and a link-pending token in the fragment, **When** the page initialises, **Then** the link-existing-account prompt is shown (not the normal signed-out home), explaining that an existing account matched and proof is required, and the raw token is removed from the browser address bar/history.
2. **Given** the prompt is shown for an account whose strongest available proof is a passkey, **When** the person chooses to continue and completes the passkey check, **Then** the new social identity is linked to the existing account and the person is signed in to the web app exactly as a normal social sign-in would leave them.
3. **Given** the link has been completed, **When** the same person later signs in with that same social provider, **Then** they are signed in directly with no further prompt.

---

### User Story 2 - Prove ownership with an authenticator code when no passkey is available (Priority: P1)

A person whose existing account has two-factor authentication (authenticator app / TOTP) but no passkey goes through the same matched-email social sign-in. The prompt offers an authenticator-code challenge. They enter a valid 6-digit code, the link is made, and they are signed in.

**Why this priority**: TOTP is the second of the two v1 proof methods and covers accounts that have 2FA but no passkey. Without it, a large class of secured accounts could not complete linking on the web.

**Independent Test**: Drive a `LinkRequired` outcome for an account enrolled in TOTP and not eligible for passkey, complete the authenticator-code challenge with a valid code, and confirm the link and session are established; confirm an invalid code is rejected without linking.

**Acceptance Scenarios**:

1. **Given** the prompt is shown and the offered proof method is an authenticator code, **When** the person enters a valid code, **Then** the link completes and the person is signed in.
2. **Given** the authenticator-code challenge is shown, **When** the person enters an invalid or expired code, **Then** the prompt reports that the code was not accepted, no link is made, and the person can retry (subject to rate limiting).

---

### User Story 3 - Safe failure and abandonment (Priority: P2)

The person can decline or fail the proof without harming the existing account, and an expired or tampered link-pending token never results in a link or a session.

**Why this priority**: The security value of Feature 168 depends on the UI failing closed. This protects the existing account when proof is wrong, the token is stale, or the person walks away.

**Independent Test**: Present the prompt with an expired token, a tampered token, and a cancelled proof, and confirm each path ends with no link, no session, and a clear, non-leaky message.

**Acceptance Scenarios**:

1. **Given** the link-pending token in the fragment is expired or invalid, **When** the prompt attempts to start the challenge, **Then** the person is told the request has expired and is invited to start sign-in again, with no link made and no session issued.
2. **Given** the prompt is shown, **When** the person chooses to cancel, **Then** they are returned to the signed-out home with no link made and no session issued.
3. **Given** the proof tier the account requires cannot be satisfied by the methods offered in this version, **When** the prompt loads the challenge, **Then** the person is given a clear path to recover (e.g. instructed to sign in with their existing method) rather than a dead end.

---

### Edge Cases

- **No reusable v1 method**: An account whose only proof would be a bare password (no 2FA, no passkey) cannot be served by the v1 method set (passkey + authenticator code). The prompt MUST detect this and route the person to recover via their existing sign-in method, never silently link.
- **Provider already linked elsewhere / email collision**: If, by the time confirm is attempted, the social identity is already linked to a different account or the email now collides, the confirm is refused. The prompt MUST present this as a non-leaky failure and not establish a session.
- **Token consumed / replay**: A link-pending token or completed challenge that has already been redeemed MUST NOT link again or produce a second session.
- **Fragment refresh / deep-link**: Reloading the page after the token has been cleared from the address bar MUST NOT crash or partially link; it should fall back to the signed-out home.
- **Rate limiting**: Repeated failed proof attempts are throttled by the server; the prompt MUST surface the throttled state without exposing whether the target account exists.
- **Wrong account proof**: A proof that succeeds for a different account than the matched one MUST be refused at confirm with a non-leaky failure.
- **Cross-surface token**: A link-pending token minted for the wallet surface that arrives at the web surface (or vice versa) is handled by the server's tier derivation; the web prompt MUST not assume or alter the surface.

## Requirements *(mandatory)*

### Functional Requirements

#### Fragment detection & entry

- **FR-001**: The web (`/app`) host MUST detect the `LinkRequired` outcome and the accompanying link-pending token delivered in the post-sign-in URL fragment, and route the person to the link-existing-account prompt instead of the normal signed-out experience.
- **FR-002**: The host MUST remove the link-pending token from the browser address bar and history immediately on capture, so the token is not exposed in shareable URLs or back-navigation, consistent with how normal social sign-in tokens are handled.
- **FR-003**: If the fragment is absent, malformed, or the token cannot be captured, the host MUST fall back to the standard signed-out home with no error state leaked.

#### The prompt (LinkExistingAccountPrompt)

- **FR-004**: The prompt MUST explain, in plain language, that an account already exists for the email from the chosen sign-in method and that the person must confirm it's them before the new sign-in method is connected.
- **FR-005**: The prompt MUST begin the challenge by presenting the link-pending token as the principal to the server's challenge-initiate step (no signed-in session is required or assumed).
- **FR-006**: The prompt MUST support **two** proof methods in v1: a passkey check and an authenticator-app (TOTP) code. Re-authentication via a linked social provider (ReOAuth) is explicitly **deferred** and MUST NOT be offered in v1.
- **FR-007**: The prompt MUST present whichever proof method the server indicates for the account, and where the account supports more than one v1 method, SHOULD prefer the stronger (passkey) while allowing the person to switch to the authenticator-code method if available.
- **FR-008**: For a passkey proof, the prompt MUST run the browser passkey ceremony and submit the resulting assertion to the server's challenge-verify step.
- **FR-009**: For an authenticator-code proof, the prompt MUST collect a numeric code and submit it to the server's challenge-verify step.
- **FR-010**: On a successful verify, the prompt MUST redeem the link-pending token together with the resulting challenge proof at the server's link-confirm step to complete the link and obtain a session.
- **FR-011**: On successful confirm, the prompt MUST establish the web session from the returned tokens using the same session-establishment path as a normal social sign-in, leaving the person signed in.

#### Isolation & non-duplication constraints

- **FR-012**: The anonymous step-up prompt MUST be implemented as a **separate surface** from the shipped authenticated step-up dialog used by signed-in users; the two MUST NOT be merged or share an entry point. (The authenticated dialog requires a signed-in principal; this flow has none — only the link-pending token.)
- **FR-013**: This feature MUST NOT re-consolidate, relocate, or restructure the already-shipped account-security components (Feature 150). It consumes existing client services for proof execution where available but leaves those components in place.
- **FR-014**: The prompt MUST reuse the existing client capabilities for the passkey ceremony and authenticator-code submission rather than introducing parallel duplicates of that machinery.

#### Failure, security & feedback

- **FR-015**: An expired, invalid, or tampered link-pending token MUST result in no link and no session, and the person MUST be shown a clear "this request has expired — please sign in again" style message.
- **FR-016**: A rejected proof, insufficient proof tier, account mismatch, or link-time conflict MUST result in no session, MUST be presented as a non-leaky message (no disclosure of whether the target account exists beyond what the social flow already reveals), and MUST allow recovery (retry where permitted, or guidance to use the existing sign-in method).
- **FR-017**: The person MUST be able to cancel the prompt and return to the signed-out home with no link and no session.
- **FR-018**: When the account's required proof cannot be met by the v1 method set, the prompt MUST present a recovery path (sign in with the existing method) rather than a dead end.
- **FR-019**: All user-facing feedback in this flow MUST use the platform's inline-feedback / in-page surfaces (not retired toast surfaces) consistent with current UI conventions.

### Key Entities *(include if feature involves data)*

- **Link-pending token**: A short-lived (~5 minutes), signed, stateless credential issued by the server when an unconnected social sign-in matches an existing account. Carries the social identity claims and the target account reference. It is the *principal* for the anonymous challenge and confirm steps — there is no bearer session. The web client treats it as opaque.
- **Challenge (proof) token**: A single-use, short-lived (~5 minutes) token issued by the server after a successful proof, scoped to the social-link operation and bound to the target account. Presented alongside the link-pending token at confirm.
- **Proof method**: The way the person proves ownership of the existing account. v1 set = passkey, authenticator-app code (TOTP). ReOAuth (re-authenticate with a linked social) and bare password are out of v1 scope.
- **Session tokens**: The access/refresh tokens returned by the confirm step on success — the same shape and meaning as a normal social sign-in, used to establish the signed-in web session.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person whose social sign-in matches an existing account and who has a passkey can complete linking and reach the signed-in state in under 60 seconds and within 3 interactions (continue → passkey → done), with no dead end.
- **SC-002**: A person with an authenticator app (and no passkey) can complete linking by entering one valid code, with no dead end.
- **SC-003**: 100% of `LinkRequired` outcomes returned to the web surface are presented as an actionable prompt — zero dead-end landings — measured across the v1 method set.
- **SC-004**: In 100% of expired-token, invalid-token, wrong-proof, cancelled, and conflict cases, no link is created and no session is established (fail-closed), verified by automated tests.
- **SC-005**: The link-pending token never persists in the browser address bar or history after capture (not present on reload or back-navigation).
- **SC-006**: A completed link is permanent — a subsequent sign-in with the same provider signs the person in directly with no further proof prompt.
- **SC-007**: The authenticated security/step-up components shipped by Feature 150/116 remain unchanged (no edits to those component files), verifiable by diff.

## Assumptions

- **Backend dependency**: Feature 168's server endpoints (challenge initiate, challenge verify, link-confirm) and the `LinkRequired` fragment redirect to `/app/#outcome=LinkRequired&linkPendingToken=...` are available to the web host. This feature is the consumer; it does not modify those endpoints.
- **Surface scope**: Only the web (`/app`) surface is in scope. The citizen wallet PWA (`/wallet`) receives the same fragment but its prompt is a separate, separately-tracked effort.
- **Proof method scope**: v1 ships passkey + authenticator-code (TOTP) only. ReOAuth is deferred. Accounts whose only viable proof is a bare password (no 2FA, no passkey) are handled via a recovery path, not in-prompt linking, in v1.
- **Existing client machinery**: The passkey browser ceremony and authenticator-code submission already exist in the web client (shipped for the authenticated security surface) and are reused, not reimplemented.
- **Session establishment**: The web app already has a session-establishment path for tokens returned post-sign-in (the social-callback fragment flow); the confirm step's returned tokens feed the same path.
- **Feedback surfaces**: The platform's inline-feedback in-page surfaces are the sanctioned channel for user feedback in this flow (toasts are retired for user-facing pages).
- **Server-enforced security**: Token expiry, single-use semantics, proof-tier flooring, account-match assertion, conflict detection, and rate limiting are enforced server-side (Feature 168); the web prompt surfaces the resulting states but does not re-implement the policy.
