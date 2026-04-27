# Feature Specification: Account Linking & Auth-Method Management

**Feature Branch**: `116-account-linking`
**Created**: 2026-04-27
**Status**: Draft
**Input**: User description: "Account Linking & Auth-Method Management — let one PlatformUser (one verified email) carry multiple sign-in methods (password, OAuth socials Google/GitHub/Microsoft/Apple, FIDO2 passkeys) and manage them from a new 'Accounts' tab in Settings. Password set/change moves to the existing Security tab next to 2FA."

**Authoritative design**: `docs/superpowers/specs/2026-04-27-account-linking-design.md` (committed `ded4218c`). All locked decisions (Q1–Q6), data-model choices, endpoint surface, edge cases, and testing strategy live there. This spec is the user-facing / business-facing translation; the design doc is the technical source of truth.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Link and unlink social sign-in providers (Priority: P1)

A signed-in user with an email/password account opens **Settings → Accounts** and links their Google account so they can sign in either way. Later, they unlink Google after a re-authentication prompt.

**Why this priority**: This is the most-requested capability — users who registered with email/password want to add "Sign in with Google/GitHub/Microsoft/Apple" without abandoning their existing account. It also exercises the new tab, the post-login OAuth-link flow, the email-collision protection, and the re-authentication primitive end-to-end. Shippable as a standalone slice.

**Independent Test**: A user with email + password links Google. Subsequent sign-out and sign-in via Google lands on the same account (same email, same memberships, same wallets). Unlinking Google requires a re-authentication challenge and succeeds. Attempting to link a Google account whose email is already used by a different Sorcha user is rejected with a clear message.

**Acceptance Scenarios**:

1. **Given** a signed-in user with no linked social providers, **When** they click "Add Google" in the Accounts tab and complete the Google sign-in, **Then** Google appears in their list of linked providers, the user remains signed in, and a subsequent fresh sign-in via Google lands on the same account.
2. **Given** a signed-in user with Google linked, **When** they click "Unlink Google", **Then** they are challenged for a re-authentication proof (current password, time-based one-time code, passkey, or another linked provider) and on success the Google link is removed.
3. **Given** a signed-in user attempts to link a social provider whose email belongs to another Sorcha user, **When** the OAuth callback completes, **Then** the link is rejected with a message explaining that the social account is linked to another Sorcha account, and the user's session is unaffected.
4. **Given** a signed-in user who has only one sign-in method left (a single linked Google), **When** they view the Accounts tab, **Then** the "Unlink" action on that one provider is disabled with a tooltip explaining the user must keep at least one sign-in method.
5. **Given** a tampered OAuth callback (modified state parameter), **When** it reaches the server, **Then** it is rejected without modifying any account state.

---

### User Story 2 - Manage passkeys from settings (Priority: P2)

A signed-in user adds, renames, and removes FIDO2 / WebAuthn passkeys from the Accounts tab — for instance, registering "Work YubiKey" alongside "Home laptop", renaming one of them, or removing one when the device is decommissioned.

**Why this priority**: Passkey hygiene becomes important once users have more than one device. The capability to register and list passkeys already exists in the system and is reused; the new value is the dedicated UI for naming, renaming, and safe removal with audit retention.

**Independent Test**: A user signs in, adds a new passkey from the Accounts tab with a chosen display name, sees it in the list immediately with "Last used: never", renames it, and (after a re-authentication challenge) removes it. The removed passkey no longer authenticates but its row remains in the system as a revoked record for forensic audit.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they add a passkey from the Accounts tab and provide a display name, **Then** the passkey appears in their list with the display name and "Last used: never".
2. **Given** a passkey in the list, **When** the user clicks "Rename", changes the display name, and saves, **Then** the new name appears immediately without any re-authentication prompt.
3. **Given** a passkey in the list and other sign-in methods present, **When** the user clicks "Remove", **Then** they are challenged for a re-authentication proof and on success the passkey is revoked (no longer accepted at sign-in) but retained for audit.
4. **Given** a passkey that has been auto-disabled by cloned-authenticator detection, **When** the user views the Accounts tab, **Then** that row is shown with a warning indicator, can still be removed (without re-authentication, since it is already non-functional), and cannot be renamed.
5. **Given** a user whose only remaining sign-in method is a single passkey, **When** they view that passkey, **Then** the "Remove" action is disabled with the last-method tooltip.

---

### User Story 3 - Set, change, or remove password from settings (Priority: P3)

A user manages their password from inside Settings: a passkey-only or social-only user can add a password; an existing password user can rotate it; either user can remove the password if they have other sign-in methods.

**Why this priority**: Self-service password lifecycle in-session is a baseline convenience that today is partially covered only by the forgot-password flow. Users currently cannot rotate a password without leaving the application. Adding password management completes the symmetry of the Accounts tab.

**Independent Test**: A passkey-only user opens Settings → Accounts, sets a password (the operation is gated by a re-authentication challenge against their existing passkey), and signs out / back in with email + password. An existing password user opens Settings → Security, clicks "Change password", completes the re-authentication challenge, and rotates the password.

**Acceptance Scenarios**:

1. **Given** a user with at least one sign-in method but no password, **When** they click "Set a password" in the Accounts tab and complete the re-authentication challenge, **Then** the password is saved and is usable for subsequent sign-ins.
2. **Given** a user with a password, **When** they click "Change password" in the Security tab and complete the re-authentication challenge, **Then** the new password replaces the old one.
3. **Given** a user with a password and other sign-in methods, **When** they click "Remove password" and complete the challenge, **Then** the password is cleared and email + password sign-in stops working for that account.
4. **Given** a user whose only remaining sign-in method is the password, **When** they view the Accounts tab, **Then** the "Remove" action on the password is disabled with the last-method tooltip.

---

### User Story 4 - View all sign-in methods in one place (Priority: P4)

A user opens Settings → Accounts and immediately sees, on a single page, every sign-in method currently attached to their account: whether a password is set, which social providers are linked (with the provider's email and last-used timestamp), and which passkeys are registered (with display name, device type, and last-used timestamp).

**Why this priority**: Transparency-only slice. Even without any add / remove actions implemented, a user can audit their own account from the new tab. Every higher-priority story (P1, P2, P3) depends on this aggregate view existing.

**Independent Test**: A user with mixed methods (password + Google + 2 passkeys) opens the Accounts tab and sees all four rows with accurate metadata. The view loads in a single round-trip.

**Acceptance Scenarios**:

1. **Given** a user with multiple sign-in methods, **When** they open the Accounts tab, **Then** they see one section each for password, linked providers, and passkeys, with accurate last-used timestamps.
2. **Given** a user with no linked social providers, **When** they view the Linked sign-in providers section, **Then** they see an empty-state message and the four "Add" pills (Google, GitHub, Microsoft, Apple).
3. **Given** a user with both Google and GitHub linked, **When** they view the Add pills, **Then** Google and GitHub appear visually struck-through and disabled, while Microsoft and Apple are active.

---

### Edge Cases

- **Email collision on link**: an OAuth provider returns an email that belongs to a different Sorcha account → the link is rejected with a clear message; the user's session is unaffected; no merge is attempted.
- **Provider returns no email** (Apple "Hide my email", private GitHub email): the link still succeeds; the row displays "(no email shared)"; no collision check possible, but the provider's unique subject identifier still prevents duplicate links.
- **Last-method floor race**: a user opens two browser tabs and tries to remove two methods that would together leave zero — the second remove is rejected by the server even though both passed the UI check.
- **Re-used or expired re-authentication challenge**: a challenge used twice, used for a different operation than it was issued for, or used after expiry, is rejected with a clear error and the user is re-prompted.
- **Tampered OAuth state parameter**: a callback whose state has been modified is rejected before any account changes occur.
- **User unlinks the provider they are currently signed in via**: the active session continues to work until natural expiry; the next sign-in via that provider correctly fails and the user falls back to another method.
- **Cloned-authenticator detection during normal use**: an existing passkey is auto-disabled; the Accounts tab shows the warning state and offers Remove; rename is disabled.
- **Concurrent passkey rename from two tabs**: last-write-wins on display name; the UI reconciles by re-loading the list.
- **Two-factor disable as a sensitive operation**: disabling two-factor authentication from the Security tab requires a re-authentication challenge, mirroring removal of any sign-in method.
- **Bootstrap user with zero sign-in methods** (only reachable via data corruption): allowed to set their first password unguarded, since no other proof of identity is possible.

## Requirements *(mandatory)*

### Functional Requirements

#### Aggregate view

- **FR-001**: The system MUST present a single "Accounts" tab in Settings, ordered as the first tab, that lists every sign-in method currently attached to the signed-in user.
- **FR-002**: The Accounts tab MUST surface, for each method, enough metadata for the user to recognise it: provider name and account email for social links; display name, device type, and registration date for passkeys; "Last changed" date for the password.
- **FR-003**: The Accounts tab MUST surface a "Last used" timestamp for each method (or "Never used" where applicable) so users can identify abandoned credentials.
- **FR-004**: The system MUST disable the destructive action ("Remove" / "Unlink" / "Remove password") on whichever method would leave the user with zero remaining sign-in methods, and MUST display an explanatory tooltip.
- **FR-005**: The system MUST treat two-factor authentication enrolment as a *second factor*, not a sign-in method, when computing the last-method floor.

#### Adding methods

- **FR-006**: A signed-in user MUST be able to link an additional OAuth social provider (Google, GitHub, Microsoft, Apple) without re-authentication, completing through a standard OAuth round-trip from the Accounts tab.
- **FR-007**: The system MUST reject any social-link attempt where the OAuth provider returns an email that belongs to a different Sorcha account, with a user-facing message explaining the conflict and offering no automatic merge.
- **FR-008**: The system MUST allow a social link even when the OAuth provider returns no email, relying on the provider's unique subject identifier to prevent duplicates.
- **FR-009**: A signed-in user MUST be able to register an additional FIDO2 / WebAuthn passkey from the Accounts tab without re-authentication, providing a non-empty display name at registration time.
- **FR-010**: A signed-in user with no password set MUST be able to set a password from the Accounts tab.
- **FR-011**: Setting a first password MUST require a re-authentication challenge unless the user has zero other sign-in methods (the bootstrap case, only reachable via data corruption).

#### Removing methods

- **FR-012**: A signed-in user MUST be able to unlink a social provider, remove a passkey, or remove their password from the Accounts tab — provided the action does not leave them with zero sign-in methods.
- **FR-013**: Every removal action MUST require a fresh re-authentication challenge before it executes.
- **FR-014**: Removing a passkey MUST preserve the underlying record for forensic audit; the passkey MUST no longer authenticate, but its registration metadata, last-used timestamp, and removal reason MUST remain queryable.
- **FR-015**: Unlinking a social provider MUST hard-delete the link record (no audit retention required); the provider's own activity log is the canonical source of historical data.
- **FR-016**: Removing a passkey that has already been auto-disabled by the cloned-authenticator detector MUST NOT require re-authentication, since the passkey is already non-functional.

#### Changing existing methods

- **FR-017**: A user with a password MUST be able to change it from the Security tab; the change MUST require a re-authentication challenge but MUST NOT require re-entering the current password (the challenge already proves possession).
- **FR-018**: A user MUST be able to rename a passkey display name without re-authentication.

#### Re-authentication challenge primitive

- **FR-019**: The system MUST provide a single shared re-authentication challenge mechanism used by every gated operation (removal, password change, two-factor disable).
- **FR-020**: The challenge MUST select the strongest available proof type for the user, in this order: time-based one-time code (if two-factor is enrolled) → current password (if set) → passkey step-up (if any active passkey) → re-authentication via a still-linked OAuth provider.
- **FR-021**: A user with multiple proof types enrolled MUST be able to switch to a different proof type from within the challenge dialog.
- **FR-022**: Each successful challenge MUST yield a single-use, short-lived authorisation that is bound to one specific operation type (a token issued for "remove a passkey" cannot be replayed for "change password").
- **FR-023**: An attempt to reuse, replay after expiry, or cross-use a challenge authorisation MUST be rejected without modifying any account state.

#### Two-factor disable adoption

- **FR-024**: The existing "disable two-factor authentication" action MUST adopt the same re-authentication challenge primitive, closing the gap where a hijacked session could disable 2FA unguarded and then proceed to prune recovery methods unguarded.

#### Settings tab restructuring

- **FR-025**: The system MUST add the new "Accounts" tab as the first (leftmost) tab in Settings.
- **FR-026**: The existing "Connections" tab MUST be renamed to "Service Profiles" with an updated icon to remove the naming clash; its body and behaviour MUST remain unchanged.
- **FR-027**: The Security tab MUST gain a Password section above the existing two-factor section, hosting the change-password action; the same control MUST also appear in the Accounts tab so both user mental models lead to the same outcome.

#### Sessions and downstream effects

- **FR-028**: Removing a sign-in method MUST NOT forcibly invalidate the user's current session — the active access token remains valid until natural expiry.
- **FR-029**: Concurrent attempts to remove different sign-in methods that would together leave the user at zero MUST result in exactly one success and one server-side rejection (the second remove is rejected with a clear last-method-protected error).

### Key Entities *(include if feature involves data)*

- **Sign-in method**: An umbrella term covering the three concrete kinds — password presence (boolean), linked social provider (provider + provider's account identifier + email + linked-at + last-used), and registered passkey (display name + device type + status + registered-at + last-used). The user's *count of active sign-in methods* governs the last-method floor.
- **Re-authentication challenge**: A short-lived, single-use authorisation issued to a user after they prove possession of an enrolled factor. Each challenge is bound to a specific scoped operation (e.g., "remove a sign-in method", "change password", "disable two-factor"). Cannot be replayed across operations or after expiry.
- **Audit-retained passkey**: A revoked passkey whose registration metadata is preserved for forensic review even though it can no longer authenticate.

## Assumptions

- The system already supports a single platform-wide identity per verified email, and the underlying storage already permits a single user to carry zero or more passwords, social links, and passkeys. (Confirmed by exploration during design.)
- The existing OAuth provider integrations (Google, GitHub, Microsoft, Apple) are sufficient for the link flow; no new providers are added by this feature.
- The existing FIDO2 / WebAuthn registration and authentication flows are reused without protocol-level changes.
- The system runs pre-release, so database schema changes can be squashed into the existing initial migration rather than versioned forward.
- The feature is end-user-facing only; administrator-side reset of another user's sign-in methods is explicitly out of scope.
- Account merge (combining two existing Sorcha accounts after an email collision) is explicitly out of scope.
- Email change for a user is explicitly out of scope; the email shown in the Accounts tab is read-only.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with one existing sign-in method can link a second sign-in method (social or passkey) and successfully sign in via the new method within five minutes of opening Settings, with no support intervention.
- **SC-002**: Zero users can place themselves into a permanently locked-out state through the Accounts UI under any combination of clicks, including across multiple concurrent browser tabs.
- **SC-003**: 100% of attempts to link a social provider whose email belongs to a different Sorcha account are rejected with a clear, user-actionable message; no automatic merging occurs.
- **SC-004**: 100% of removal actions, password changes, and two-factor disables require a fresh re-authentication challenge before they execute (i.e., a hijacked session cannot quietly prune the legitimate owner's recovery methods).
- **SC-005**: Reusing, replaying, or cross-operation-using a re-authentication authorisation succeeds 0% of the time.
- **SC-006**: The Accounts tab loads its full list of methods (password status + linked providers + passkeys) in a single round-trip and renders within two seconds on a representative end-user connection.
- **SC-007**: A user removing a passkey can, six months later, look up that revoked passkey's registration metadata and removal date for audit purposes (forensic-retention requirement).
- **SC-008**: A user who unlinks a social provider continues to use their current session uninterrupted until natural session expiry; no forced sign-out occurs.
- **SC-009**: Renaming a passkey takes a single click-edit-save action with no re-authentication prompt, completing in under ten seconds.
- **SC-010**: After this feature ships, the percentage of password-reset support requests that could have been self-served by in-app password change drops to effectively zero.
