# Feature Specification: Unified Account Security Surface

**Feature Branch**: `150-account-security`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "Consolidate the fragmented account-security management into a single discoverable 'Security' home, surfaced in the user profile menu, shared verbatim between the web app and the citizen wallet app. Add email and SMS one-time-code second factors as honestly-labelled lower-assurance methods, kept safe by an assurance-aware floor rule and always-notify. SMS is available only where an operator has configured a provider. Finish the incomplete step-up proof methods. 2FA stays optional (policy-ready). Out of scope: signup-time nudges, mandatory-2FA enforcement, recovery-email channel."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - One discoverable home for all sign-in & security options (Priority: P1)

A signed-in user opens their profile menu, sees a **Security** entry between *My Profile* and *My Devices*, and lands on a single page that shows every way they sign in (password, passkeys, social accounts), their two-factor methods, and their recovery options — each labelled with how strong it is. From this one page they can add, rename, and remove methods, and the system stops them from doing anything that would weaken or lock out their account.

**Why this priority**: This is the foundational slice. It fixes the actual problem (management exists but is buried across two Settings tabs with no top-level entry), and it is the shared surface every later slice renders into. On its own it delivers a coherent, discoverable, safe security experience — a viable MVP.

**Independent Test**: Sign in, open the profile menu, confirm *Security* appears between *My Profile* and *My Devices*, open it, see the three labelled groups, perform an add and a step-up-gated remove, and confirm a weaker method cannot authorise removing a stronger one.

**Acceptance Scenarios**:

1. **Given** a signed-in user, **When** they open the profile menu, **Then** a *Security* item appears between *My Profile* and *My Devices* and opens the Security home.
2. **Given** the Security home, **When** it loads, **Then** methods are grouped as *How you sign in*, *Two-factor authentication*, and *Recovery*, and every method row shows an assurance label (Strongest / Strong / Basic).
3. **Given** a user with a passkey (Strongest) and a Basic factor, **When** they try to remove the passkey and are asked to verify, **Then** only equal-or-stronger methods are offered as proof and the Basic factor cannot complete the removal.
4. **Given** a user who adds, renames, removes, or disables any method, **When** the change succeeds, **Then** they receive both an in-app notification and an email recording the change.
5. **Given** a user with exactly one sign-in method, **When** they attempt to remove it, **Then** the system prevents it and explains why.
6. **Given** a user starting a sensitive change, **When** they are asked to verify with a passkey or by re-authenticating a linked social account, **Then** those verification methods work (they are no longer placeholders).

---

### User Story 2 - Use an emailed code as a second factor (Priority: P2)

A user who does not want to install an authenticator app turns on **Email code** as a second factor from the Security home. At sign-in, after their first factor, they receive a one-time code by email and enter it to finish signing in.

**Why this priority**: Adds accessible two-factor protection for authenticator-averse users using the platform's existing email capability. It builds on US1's surface and the shared verification machinery.

**Independent Test**: Enable *Email code*, sign out, sign in with the first factor, receive and enter the emailed code, reach the app; confirm the code is single-use and rejected after expiry.

**Acceptance Scenarios**:

1. **Given** a user with a verified email on the Security home, **When** they enable *Email code*, **Then** a confirmation code is sent and, once entered, email becomes an active **Basic** second factor.
2. **Given** email two-factor is enabled, **When** the user signs in with their first factor, **Then** they are prompted for an emailed code and can complete sign-in by entering it.
3. **Given** an emailed code, **When** it is used once or after it expires, **Then** any reuse or post-expiry use is rejected.
4. **Given** email is the user's only second factor, **When** they try to use it to remove a stronger sign-in method, **Then** the floor rule blocks it.
5. **Given** repeated code requests, **When** the request rate exceeds the limit, **Then** further requests are throttled with a clear message.

---

### User Story 3 - Use an SMS code as a second factor where available (Priority: P3)

Where the operator has configured an SMS provider, a user can verify ownership of a mobile number and turn on **SMS code** as a second factor. Where no provider is configured, the option does not appear at all.

**Why this priority**: SMS is the highest-cost, highest-risk channel and is gated behind operator configuration. It reuses the one-time-code machinery from US2, so it lands after it.

**Independent Test**: With an SMS provider configured, verify a phone number, enable *SMS code*, and complete a sign-in using an SMS code; with no provider configured, confirm the SMS option is absent.

**Acceptance Scenarios**:

1. **Given** an operator-configured SMS provider, **When** a user opens the *Two-factor authentication* group, **Then** *SMS code* is offered.
2. **Given** no configured SMS provider, **When** a user opens the *Two-factor authentication* group, **Then** *SMS code* is not shown.
3. **Given** a user enabling *SMS code*, **When** they supply a mobile number, **Then** they must verify it with a code before SMS two-factor activates.
4. **Given** SMS two-factor is active, **When** codes are requested, **Then** per-number send limits apply and reuse/expiry are enforced.
5. **Given** SMS code is the user's only second factor, **When** it is used as a step-up proof, **Then** it is treated as **Basic** and the floor rule applies.

---

### User Story 4 - Manage security the same way in the wallet app (Priority: P4)

A citizen using the mobile wallet app opens the same **Security** home — passkeys, two-factor methods, recovery — and manages everything there, with passkeys clearly distinct from the wallet's own paired devices.

**Why this priority**: Closes the parity hole so a citizen is the same person on web and in the wallet. It renders US1's shared surface on the wallet host, so it depends on US1 existing.

**Independent Test**: In the wallet app, open *Security*, confirm the same three groups and actions are present and functional, and confirm *Passkeys* is visibly separate from *My Devices*.

**Acceptance Scenarios**:

1. **Given** a citizen in the wallet app, **When** they open *Security*, **Then** they see the same three-group Security home as the web app.
2. **Given** the wallet Security home, **When** they add or remove a method, **Then** it behaves identically to the web app (same floor rule, same notifications).
3. **Given** the wallet app, **When** they view *Security → Passkeys* and the wallet's *My Devices*, **Then** the two are clearly distinct and never implied to be the same thing.
4. **Given** a citizen linking a social account from the wallet app, **When** they complete the provider flow, **Then** they return to the wallet Security home with the account linked.

---

### Edge Cases

- **Last method**: a user attempts to remove their only remaining sign-in method → blocked with an explanation.
- **Downgrade attempt**: a user attempts to use a Basic factor (email/SMS) to remove or disable a Strong/Strongest method → blocked by the floor rule, with guidance on which method to verify with instead.
- **Provider outage**: the email or SMS provider is unavailable when a code is requested → the user sees an error and can fall back to another enrolled factor; they are never locked out by a single channel's failure.
- **SMS de-configured after enrolment**: an operator removes SMS provider configuration after a user enabled SMS → SMS prompts are suppressed and the user is guided to rely on / add another factor; they are never locked out because other factors remain.
- **Code reuse / expiry**: a one-time code is entered a second time or after it expires → rejected.
- **Exhausted factors**: a user has lost every factor → recovery proceeds via backup codes; existing out-of-band account recovery remains the final fallback (unchanged by this feature).
- **Brute force**: repeated failed code attempts → throttling and temporary lockout/backoff.
- **Concurrent edits**: two sessions race to change security settings → the server is authoritative and re-evaluates the floor and last-method rules on every change.
- **Cross-host navigation**: a user on the web app versus the wallet app → reaches the same Security home with working navigation on each host (no broken links).
- **Shared device**: a co-located person picks up an unlocked session → sensitive changes still require a step-up proof, and always-notify alerts the real owner of any change.

## Requirements *(mandatory)*

### Functional Requirements

#### Discoverability & consolidation

- **FR-001**: The system MUST present a single *Security* destination reachable in one action from the user profile menu, positioned between *My Profile* and *My Devices*.
- **FR-002**: The *Security* destination MUST consolidate all authentication-method management currently split across separate Settings areas, and the former locations MUST redirect to it (no authentication management remains under Settings).
- **FR-003**: The Security home MUST organise methods into three groups: *How you sign in* (password, passkeys, social accounts), *Two-factor authentication* (authenticator, email code, SMS code), and *Recovery* (backup codes).
- **FR-004**: Each method row MUST display a visible assurance level of **Strongest**, **Strong**, or **Basic**.
- **FR-005**: Users MUST be able to add, rename (where the method supports a label), and remove each method from the Security home.

#### Safety, authorization, and notification

- **FR-006**: The system MUST prevent a user from removing their last remaining sign-in method.
- **FR-007**: The system MUST enforce that a step-up verification proof may authorise a destructive or downgrade change to a method only when the proof's assurance level is greater than or equal to the target method's assurance level (the "floor rule"). Consequently a Basic proof MUST NOT be able to remove a passkey, disable the authenticator, or change the password.
- **FR-008**: The Security home MUST reflect, per method, whether it can currently be removed and what proof strength would be required; this determination MUST be made by the server, and the client MUST NOT decide it independently.
- **FR-009**: The system MUST notify the user of every security-state change (method added, renamed, removed, enabled, or disabled; phone changed) via **both** an in-app notification **and** an email.
- **FR-010**: All authorization, floor-rule, and last-method decisions MUST be enforced on the server regardless of client state.
- **FR-011**: A notification-delivery failure MUST NOT roll back or block the underlying security change.

#### Step-up completeness

- **FR-012**: Step-up verification MUST support **passkey** and **social re-authentication** as proof methods, completing the set alongside the existing authenticator-code and password proofs.

#### Email one-time-code factor

- **FR-013**: A user with a verified email MUST be able to enable an emailed one-time code as a **Basic** second factor.
- **FR-014**: Enabling the email factor MUST confirm the inbox is currently reachable (deliver and require entry of a confirmation code) before activation.
- **FR-015**: At sign-in, a user with the email factor enabled MUST be able to complete their second factor by entering an emailed code.
- **FR-016**: Emailed codes MUST be single-use and MUST expire after a bounded time; reuse or use after expiry MUST be rejected.
- **FR-017**: The system MUST rate-limit one-time-code requests per user and per channel.
- **FR-018**: Security emails (both one-time codes and change notifications) MUST be sent through the platform's standard transactional-email pipeline with consistent Sorcha branding, not assembled ad hoc.

#### SMS one-time-code factor (configuration-gated)

- **FR-019**: The *SMS code* option MUST be available only when an operator has configured an SMS provider, and MUST be entirely absent otherwise.
- **FR-020**: Enabling the SMS factor MUST require the user to verify ownership of the mobile number (deliver and require entry of a code) before activation.
- **FR-021**: The system MUST store the user's mobile number and its verified state, captured at SMS-enable time and not during signup.
- **FR-022**: SMS codes MUST be single-use, time-bounded, and subject to per-number send limits to control cost and abuse.

#### Assurance model & audience

- **FR-023**: Email-code and SMS-code factors MUST be treated as **Basic** assurance everywhere assurance is evaluated (both at sign-in and during step-up).
- **FR-024**: The Security home MUST function identically for general platform users and for wallet (citizen) users, presenting one surface to both.
- **FR-025**: The same Security home MUST be available in the citizen wallet app with full management parity.
- **FR-026**: The Security home's passkeys MUST be presented as distinct from the wallet's device pairing (*My Devices*); the experience MUST NOT conflate WebAuthn login authenticators with wallet credential-delegation devices.
- **FR-027**: In-app navigation to the Security home MUST resolve correctly on each host (web app and wallet app) with no broken links.

#### Policy readiness

- **FR-028**: Two-factor authentication MUST be optional for all users in this release; the design MUST NOT preclude an organisation or the platform later requiring it without rearchitecting.

### Key Entities *(include if feature involves data)*

- **Authentication Method**: a way a user signs in or proves identity — password, passkey, social account, authenticator code, email code, SMS code, or backup codes. Attributes: kind, role (sign-in / second factor / recovery), assurance level, current removable state.
- **Assurance Level**: the relative strength of a method — **Strongest**, **Strong**, or **Basic** — derived from the method kind, not user-set.
- **Step-up Verification**: a short-lived proof a user supplies to authorise a sensitive change; carries the assurance level of the method used to satisfy it.
- **One-Time Code**: a single-use, time-bounded code delivered by email or SMS; consumed on first valid use and invalid thereafter.
- **User Contact (Phone)**: a user's mobile number and its verified state, used only for SMS codes.
- **Security Notification**: the durable in-app entry plus email generated on every security-state change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A signed-in user can open account-security management from the profile menu in a single click, without navigating through Settings tabs.
- **SC-002**: 100% of the authentication-method management actions available before this feature remain available afterward, now reachable from one page.
- **SC-003**: Across every combination of methods, a lower-assurance factor can never complete the removal or downgrade of a higher-assurance method (verified exhaustively).
- **SC-004**: Every security-state change produces a user-visible in-app notification and an email within one minute.
- **SC-005**: A user can enable the email factor and use an emailed code to complete sign-in; codes are rejected on reuse and after expiry.
- **SC-006**: Where an SMS provider is configured, a user can verify a number and complete sign-in with an SMS code; where it is not configured, the SMS option never appears.
- **SC-007**: The identical Security experience is available and fully functional in the wallet app, with passkeys and wallet devices presented as clearly separate.
- **SC-008**: A user is never able to remove their last sign-in method.
- **SC-009**: Each of the four delivery slices can be demonstrated working independently of the slices that follow it.

## Assumptions

- The existing authentication backend (auth-method aggregate read, step-up challenge, password / social / passkey lifecycle, and authenticator/TOTP two-factor) is **reused and extended**, not rebuilt — this feature is the successor to the prior account-linking work (Feature 116).
- Email delivery uses the platform's existing transactional-email capability (Feature 112), already in production; the email factor adds a new templated message type, not new mail infrastructure.
- Change notifications use the platform's existing in-app notification/inbox capability (Feature 118).
- One-time-code defaults (tunable, not user-facing): 6-digit numeric codes, ~10-minute expiry, single-use, with conservative per-user/per-channel request limits and per-number SMS send caps.
- Recovery in this release is **backup codes only** (already provided to authenticator users); there is no separate recovery-email channel.
- The mobile number is captured **only** when enabling the SMS factor; existing signup flows are unchanged.
- Two-factor authentication is **optional** for all users in this release.
- The web app and wallet app are reached under distinct base paths; navigation to the Security home must be base-relative on each so links work on both hosts (the design records the exact constraint).
- The full, approved technical design — architecture, assurance/floor policy, data model, endpoint surface, and four-phase delivery plan — is recorded in `docs/superpowers/specs/2026-06-10-unified-account-security-design.md` and governs the planning phase.
- **Out of scope** for this feature: sign-up-time method nudges, mandatory-2FA enforcement and grace periods, and recovery-email as a distinct channel.
