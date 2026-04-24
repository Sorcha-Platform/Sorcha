# Feature Specification: Transactional Email & Verification Sweep

**Feature Branch**: `112-email-sweep`
**Created**: 2026-04-24
**Status**: Draft
**Input**: User description: "Transactional email & verification sweep for Tenant Service. Unify verify / invite / password-reset on a single Scriban-templated path with shared Sorcha-branded base layout and plaintext multipart fallback. Fix two latent plaintext-token bugs. Introduce a welcome email firing exactly once per user. Two welcome templates — welcome-public primes users for the recovery-phrase moment; welcome-invited carries the inviting organisation's branding. Invitation email carries inviting org's branding. Scope A+ as agreed."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - New user confirms their email after public signup (Priority: P1)

A new public user signs up with email and password at the Sorcha sign-up page. They expect an email with a clear branded message and a clickable button that confirms their email address. Today they receive a plain, unbranded message that includes only a long opaque token string, with no link and no guidance on what to do with it — the flow is effectively broken.

**Why this priority**: This is the very first email a Sorcha user ever receives. A broken first impression directly blocks account activation and damages trust. The current state is a release blocker — we cannot ship to real users with a plaintext-token verification email.

**Independent Test**: Sign up with a new email address on a staging environment. Open the email that arrives. The email must carry Sorcha branding, a warm greeting, a single clearly labelled button ("Confirm my email"), and reasonable copy explaining what the link does, how long it works for, and what to do if the sign-up wasn't theirs. Clicking the button confirms the email and lands them in the product.

**Acceptance Scenarios**:

1. **Given** a new user has just submitted the sign-up form, **When** the verification email arrives, **Then** it contains Sorcha branding, a personal greeting using the user's display name, a visible call-to-action button that links to the verification URL, and a plaintext fallback readable in any mail client.
2. **Given** the verification email has arrived, **When** the user taps the call-to-action button within 24 hours, **Then** their email address is marked verified and they are landed on the product.
3. **Given** the verification email has arrived, **When** more than 24 hours have passed, **Then** the link reports expiry and offers a path to request a new verification email.
4. **Given** any email client renders the message, **When** HTML is blocked or stripped, **Then** the plaintext fallback conveys the same intent and includes a copy-paste verification URL.

---

### User Story 2 - Organisation admin invites a new colleague (Priority: P1)

An administrator at an organisation (for example, Acme Verification Co.) adds a new user by email address and assigns them a role. The invited person expects an email that clearly states *which* organisation is inviting them, what role they will have, visual branding consistent with the inviting organisation, and a single clear button to accept. Today they receive a plaintext message with only the inviter's name and an opaque token — no organisation name, no role, no link, no branding.

**Why this priority**: Organisational invitations are the primary B2B onboarding path. A plaintext-token invitation reads as spam or a phishing attempt and prevents new users from joining with confidence. This is the second release-blocking email bug.

**Independent Test**: From a branded test organisation (with name, logo URL, and primary colour configured), send an invitation to an external email address. The email must display the inviting organisation's name in the subject and body, show the organisation's logo in the header, use the organisation's primary colour on the action button, state the role assigned, and offer a clear "Accept invitation" call-to-action that opens the invitation-accept page.

**Acceptance Scenarios**:

1. **Given** an administrator submits an invitation from a branded organisation, **When** the invited person opens the email, **Then** the header shows the organisation's logo (or its name if no logo is set), the body mentions the organisation name and the assigned role, and the action button uses the organisation's primary colour.
2. **Given** an administrator submits an invitation from an organisation without branding configured, **When** the invited person opens the email, **Then** the email still clearly identifies the inviting organisation by name and presents a working accept-invitation action, falling back to Sorcha's default visual treatment.
3. **Given** the invitation email has arrived, **When** the invited person taps "Accept invitation" within the invitation window, **Then** they land on the invitation-accept flow with the token prefilled.
4. **Given** the invitation email has arrived, **When** more than the invitation window has passed, **Then** the accept link reports expiry and directs the person to contact the inviter for a fresh invitation.

---

### User Story 3 - New user receives a welcome that prepares them for the recovery-phrase moment (Priority: P2)

After a new public user confirms their email (or, for social/passkey signup paths, after they complete their first successful sign-in), they receive a single welcome email. The email greets them warmly, points them to their dashboard, and — critically — primes them for the wallet recovery-phrase moment they'll encounter soon. The message must introduce the recovery-phrase concept calmly and clearly without creating panic, without leaking any secret material into email, and without promising a recovery path that does not exist.

**Why this priority**: The recovery phrase is shown exactly once during wallet creation and is not stored in any Sorcha system. A new user who hasn't been warned can easily dismiss it, locking themselves out of any wallet they subsequently create. The welcome email is the only proactive educational moment we have before that first wallet-creation screen. It is not a release blocker on day one (the wallet-creation screen itself shows the phrase) but it is a meaningful safety intervention and a signature "you're cared for" moment.

**Independent Test**: Verify a new public email-password signup; then separately complete a social/passkey signup. Each user receives exactly one welcome email at the right moment. The message contains a greeting by display name, a dashboard call-to-action, a calm explanation of the recovery-phrase concept, a short "what's next" list of starter links, and a sign-off. The message does not contain any recovery phrase content. A second trigger (e.g., logging in again) does not produce a second welcome email.

**Acceptance Scenarios**:

1. **Given** a public email-password user clicks the verification link, **When** their email is marked verified, **Then** they receive a welcome email within one minute, and the system records that the welcome was sent.
2. **Given** a social-login or passkey user completes sign-in for the first time with their email already verified by the identity provider, **When** the sign-in succeeds, **Then** they receive a welcome email within one minute, and the system records that the welcome was sent.
3. **Given** a user has already received a welcome email, **When** they verify again, sign in again, or any trigger fires again, **Then** no additional welcome email is sent.
4. **Given** a public signup flow has completed and a welcome email is dispatched, **When** the recipient reads the welcome, **Then** it includes a clear explanation that when they create their first wallet they will be shown a 12-word recovery phrase, that they must save it at that moment because it cannot be recovered, and reassurance that this is by design.
5. **Given** a user who joined through an organisation invitation rather than a public signup, **When** their welcome email arrives, **Then** the content is tailored to the organisation context (mentions the organisation by name, confirms their role) and does not include the recovery-phrase section, because their account is org-managed.

---

### User Story 4 - User resets a forgotten password (Priority: P2)

A user who has forgotten their password requests a reset link. They receive a branded Sorcha email with a clear call-to-action button that takes them to the password-reset form. Today the password-reset email already works and delivers a clickable button, but its visual treatment is hand-rolled and does not share the layout, colour palette, or tone of voice used elsewhere.

**Why this priority**: The flow is functionally correct today. The improvement is consistency and trust: a password-reset message that looks identical in identity and voice to every other Sorcha transactional message reassures recipients that the email is genuine (and reduces phishing confusion).

**Independent Test**: Request a password reset for a known account. The email that arrives must carry the same header, footer, typography, colour palette, tone of voice, and "reply for help" affordance as the verification and welcome emails. The reset button must work within the reset window.

**Acceptance Scenarios**:

1. **Given** a user has requested a password reset, **When** the email arrives, **Then** the visual treatment matches the other Sorcha transactional emails (header, footer, colours, font).
2. **Given** the reset email has arrived, **When** the user taps "Reset password" within the reset window, **Then** they land on the password-reset form with the token validated.
3. **Given** any mail client renders the message, **When** HTML is blocked, **Then** the plaintext fallback provides the reset link and instructions.

---

### Edge Cases

- **Organisation has incomplete branding**: invitation email falls back per-field to Sorcha defaults (missing logo → Sorcha logo; missing colour → Sorcha colour) while still using the organisation's name.
- **User belongs to multiple organisations by the time welcome fires**: the welcome picks the earliest-joined standard organisation as the "inviting" organisation for the invited-welcome template.
- **User is in the public organisation only (no standard-org memberships)**: welcome uses the public-user template with the recovery-phrase section.
- **User's email address contains international characters**: email is sent to the address as provided; the sender correctly encodes the envelope and headers.
- **Email backend is temporarily unavailable**: the send path raises an error that is logged; the calling flow's existing error handling applies. No retries, no outbox (explicitly out of scope).
- **User verifies their email, but the welcome dispatch fails**: the verification itself must still succeed; the welcome failure is logged but does not block verification.
- **User signs up, never verifies, then receives a later welcome trigger via an alternative signup path**: if the account is not email-verified at the moment the welcome trigger fires, no welcome is sent.
- **Template renders correctly in common mail clients**: the layout works in Gmail (web + mobile), Outlook (web + desktop), Apple Mail (macOS + iOS). No dependency on webfonts, external CSS, or JavaScript.
- **Email is forwarded or quoted**: because no secret material is in the body (only short-lived URL tokens), forwarding leaks at most a time-limited action link.

## Requirements *(mandatory)*

### Functional Requirements

**Verification email**

- **FR-001**: When a new user completes email-password signup, the system MUST send them a verification email containing a clickable button that links to the verification URL, not a plaintext token.
- **FR-002**: The verification email MUST render both an HTML body and a plaintext body as a multipart message, so mail clients without HTML support (or users with HTML disabled) still receive a working copy-paste URL.
- **FR-003**: The verification link MUST expire after 24 hours; expiry MUST be stated in the email body.

**Invitation email**

- **FR-004**: When an administrator invites a new user to an organisation, the invited person MUST receive an email that states the organisation's name, the assigned role, and provides a clickable "Accept invitation" button linking to the accept flow, not a plaintext token.
- **FR-005**: Where the inviting organisation has a logo URL configured, the invitation email MUST display that logo in the header.
- **FR-006**: Where the inviting organisation has a primary brand colour configured, the invitation email MUST use that colour on the action button.
- **FR-007**: Where any organisational branding field is not configured, the invitation email MUST fall back to Sorcha default branding for that field without failing the send.

**Password reset email**

- **FR-008**: When a user requests a password reset, the system MUST send them an email with a clickable button that links to the reset form, using the same visual treatment as the verification and welcome emails.

**Welcome email**

- **FR-009**: The system MUST send a welcome email exactly once per user across the lifetime of their account. This applies regardless of whether the user signs up with email-password, social login, or passkey.
- **FR-010**: For email-password signups, the welcome email MUST be sent when the user successfully verifies their email address.
- **FR-011**: For signups where the email address is pre-verified by the identity provider (social login, passkey), the welcome email MUST be sent on the user's first successful sign-in.
- **FR-012**: The system MUST NOT send a welcome email to a user whose email address is not yet verified.
- **FR-013**: The system MUST track per user whether the welcome email has been sent, and MUST NOT send a second one on any subsequent trigger.
- **FR-014**: Users whose only organisation membership is the public organisation MUST receive the "public" variant of the welcome email, which introduces the recovery-phrase concept in calm, anticipatory language.
- **FR-015**: Users who have membership in a standard (non-public) organisation MUST receive the "invited" variant of the welcome email, which is tailored to their joining organisation and its branding and does NOT include recovery-phrase content.
- **FR-016**: The welcome email MUST NOT contain any recovery-phrase content, mnemonic words, or instructions on how to retrieve a phrase later (because no such retrieval path exists). It MUST frame the recovery-phrase moment as something that will happen at first wallet creation.

**Visual identity and tone**

- **FR-017**: Every transactional email the Tenant Service sends MUST share a single visual layout (header, footer, typography, spacing), so recipients can recognise Sorcha emails at a glance and distinguish them from phishing.
- **FR-018**: Every transactional email MUST use a single consistent tone — professional, friendly, one clear action per message, no alarmist language.
- **FR-019**: Every transactional email footer MUST carry a reply-to address or help contact that the recipient can use if confused or in trouble.

**Reliability and delivery**

- **FR-020**: A welcome-email send failure MUST NOT block or reverse the event that triggered it (verification success, first login). The failure MUST be logged.
- **FR-021**: The system MUST NOT expose or leak verification tokens, invitation tokens, reset tokens, or any secret material in email server logs, application logs, or monitoring telemetry at levels above DEBUG.

**Maintainability**

- **FR-022**: Dead code left behind by a prior incomplete refactor — typed-method variants of the email-send operation that are defined but never called anywhere today — MUST be removed as part of this change, so that the single supported way to send a transactional email is the new unified path.

### Key Entities *(include if feature involves data)*

- **Welcome Send Marker**: A per-user record that tracks whether the welcome email has been dispatched to that user, and when. Ensures the welcome email is sent exactly once per user regardless of trigger path. Added as a field on the existing Platform User record.
- **Email Branding**: Resolved per-message from either the Sorcha platform defaults or the inviting organisation's branding record. Conveys sender name, logo, primary colour, tagline, and reply-to address into the template rendering model.
- **Transactional Email Templates**: Six named templates (base layout, verification, invitation, password reset, public welcome, invited welcome), each shipping as a pair (HTML + plaintext), bundled with the Tenant Service. The base layout is shared; each concrete template supplies the unique body content and references the base.

## Assumptions

- The Tenant Service continues to own all transactional email outbound to users, as today. No other service gains the ability to send transactional email to users in this change.
- The existing email-backend selection logic (cloud backend when a cloud connection string is present, SMTP otherwise) remains unchanged.
- Organisation branding data already persisted on the Organisation record (name, logo URL, primary colour, tagline) is the authoritative source for per-org visual treatment on invitation emails. No new admin UI for capturing that branding is part of this change; administrators use whatever mechanism currently exists to populate those fields.
- The project is pre-release. Schema changes required by this feature are folded into the existing single initial migration rather than stacking a new migration, per the team's pre-release convention. No data backfill is required in any production environment.
- Existing verify / invite / reset flows already issue short-lived URL-safe tokens. This change does not alter token generation, validity windows, or the underlying validation logic — only the email content surrounding them.
- "Professional yet friendly" tone direction: warm, first-person ("we" / "the Sorcha team"), concise, one clear action per message, no security-theatre language, no "Dear Sir/Madam", no dark patterns.
- Logo hosting URL, final Sorcha tagline copy, and final marketing copy for "What's next" links are confirmed during implementation by the owning team; reasonable placeholder defaults are used for any that are not yet confirmed.
- Sorcha-default logo and colour are chosen to align with existing product UI branding (already consistent across the web application).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of verification, invitation, and password-reset emails sent after deploy contain a clickable call-to-action button. Zero emails contain a raw token in the plaintext body.
- **SC-002**: 100% of transactional emails sent by the Tenant Service are multipart (HTML + plaintext), so recipients with HTML disabled still receive a working message.
- **SC-003**: Every user who verifies their email address (or completes first social/passkey sign-in) receives exactly one welcome email. Duplicate welcome sends to the same user across the lifetime of that user's account: zero.
- **SC-004**: Every welcome email sent to a public user includes the recovery-phrase advance-warning section. Every welcome email sent to a standard-org user omits that section and instead identifies the joining organisation.
- **SC-005**: 100% of invitation emails sent from organisations with branding configured visually reflect that branding (logo in the header, brand colour on the action button, organisation name in the subject and body).
- **SC-006**: The six email templates render correctly (layout intact, call-to-action button visible and clickable, plaintext fallback present) in Gmail web, Gmail mobile, Outlook web, Outlook desktop, and Apple Mail.
- **SC-007**: A random sample of 10 transactional emails reviewed side-by-side is visually and tonally consistent: every one uses the same header, footer, and palette, and reads in the same voice.
- **SC-008**: After the change, copy changes to any transactional email (for example, a tagline update, a new "what's next" link, or a seasonal sign-off) can be made by editing a single template file per message type, with no code change required, and the change is reviewable as a plain diff.
- **SC-009**: After deploying this change, manual inspection of the first five real sign-ups on a staging environment confirms: verification arrives, is branded, is clickable; welcome arrives exactly once; invitation (when sent) arrives with correct org branding.
- **SC-010**: Support questions from new users that read as "I got an email with a long code but don't know what to do with it" drop to zero.
