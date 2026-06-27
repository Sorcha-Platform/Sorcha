# Feature Specification: Onboarding Profile Capture

**Feature Branch**: `157-onboarding-profile-capture`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "Feature 157 Onboarding Profile Capture — implement per docs/superpowers/specs/2026-06-24-onboarding-profile-capture-design.md (wallet wizard 12/24 default 24 + name default; new Complete-your-profile wizard step seeding persona; add EmailVerified to /api/auth/me)"

> **Note**: The referenced design document `docs/superpowers/specs/2026-06-24-onboarding-profile-capture-design.md` was not present in the repository at specification time. This spec is derived from the inline description and the existing onboarding/wallet-creation surfaces. If the design doc is added later, reconcile this spec against it during `/speckit-clarify` or `/speckit-plan`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Complete your profile during onboarding (Priority: P1)

A new person finishing first-run onboarding is presented with a "Complete your profile" step. They confirm or enter a small amount of personal information (such as their name and basic contact details). When they continue, that information is saved as their self-asserted profile so the platform can pre-fill it elsewhere instead of asking again, and so workflow actions that need basic identity attributes can read it.

**Why this priority**: This is the core new capability of the feature — capturing profile information once, at the moment the user is already engaged in onboarding, so downstream experiences (form pre-fill, persona-backed actions) have data to work with. It delivers standalone value even if the wallet-default and `EmailVerified` changes ship separately.

**Independent Test**: Run a fresh onboarding flow, reach the "Complete your profile" step, submit profile details, and confirm the details are persisted to the user's profile and visible on a subsequent visit (e.g. pre-filled or readable via the profile surface).

**Acceptance Scenarios**:

1. **Given** a newly onboarding user with no saved profile, **When** they reach the "Complete your profile" step and submit their name and basic details, **Then** the details are saved as their self-asserted profile and onboarding continues to the next step.
2. **Given** a user on the "Complete your profile" step, **When** the system already knows some details (e.g. display name from sign-up), **Then** those fields are pre-filled so the user only confirms or amends them.
3. **Given** a user on the "Complete your profile" step, **When** they choose to skip optional fields and continue, **Then** onboarding proceeds and only the provided fields are saved.
4. **Given** a user who completed the profile step, **When** they later view their profile, **Then** the values they entered during onboarding are present.

---

### User Story 2 - Sensible wallet creation defaults during onboarding (Priority: P2)

A new person creating their first wallet through the onboarding wizard is offered defaults that reduce friction: the wallet name is pre-filled with a sensible value, and the recovery-phrase length defaults to the strongest standard option (24 words) rather than the shortest. The user can still change either value before creating the wallet.

**Why this priority**: Improves the security posture and reduces decision fatigue for the most common first action, but the wallet flow already works without it, so it is secondary to capturing the profile itself.

**Independent Test**: Launch the wallet-creation wizard as part of onboarding and confirm the wallet name field is pre-populated and the recovery-phrase length is preset to 24 words, while both remain editable and a wallet can still be created with overridden values.

**Acceptance Scenarios**:

1. **Given** a user entering the wallet-creation wizard during onboarding, **When** the form first renders, **Then** the recovery-phrase length defaults to 24 words.
2. **Given** a user entering the wallet-creation wizard during onboarding, **When** the form first renders, **Then** the wallet name is pre-filled with a sensible default name.
3. **Given** the pre-filled defaults, **When** the user changes the name or selects a different recovery-phrase length (e.g. 12 words), **Then** the wallet is created with the user's chosen values.
4. **Given** a user creating a wallet outside the onboarding wizard, **When** no default name is supplied, **Then** the form behaves as before without forcing onboarding-specific defaults.

---

### User Story 3 - Email verification status is visible to the app (Priority: P3)

When the application asks "who is the current user?", the answer includes whether the user's email address has been verified. This lets the experience adapt — for example, prompting an unverified user to verify their email, or gating profile completion or other steps on verification — without a separate lookup.

**Why this priority**: Enabling data point that other onboarding/profile behaviour can build on, but on its own it changes nothing the user sees until consumed by another surface, so it is lowest priority.

**Independent Test**: Request the current-user information for a verified user and an unverified user and confirm the response correctly reflects each user's email-verification status.

**Acceptance Scenarios**:

1. **Given** an authenticated user whose email has been verified, **When** the current-user information is requested, **Then** the response indicates the email is verified.
2. **Given** an authenticated user whose email has not been verified, **When** the current-user information is requested, **Then** the response indicates the email is not verified.

---

### Edge Cases

- What happens when the user submits the profile step with invalid or malformed values (e.g. an unparseable contact value)? The system should reject with a clear, field-level message and not save a partial/invalid profile.
- What happens when saving the profile fails (transient backend error) mid-onboarding? Onboarding should surface the failure and allow retry without losing entered values, and must not silently advance as if the profile were saved.
- What happens when a returning user re-enters onboarding but already has a profile? The step should show their existing values and a re-submit should update (not duplicate) the profile.
- What happens when the user picks a non-default recovery-phrase length and then navigates back? The chosen value should be preserved, not reset to the default.
- What happens for a user authenticated by a method that does not carry an email (or whose email status is unknown)? The current-user information must represent verification status unambiguously rather than implying "verified".

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The onboarding flow MUST present a "Complete your profile" step that allows the user to enter or confirm basic personal profile information.
- **FR-002**: On submission of the profile step, the system MUST persist the entered values as the user's self-asserted profile, associated with the authenticated user.
- **FR-003**: The profile step MUST pre-fill fields the system already knows (e.g. display name captured at sign-up) so the user confirms rather than re-enters them.
- **FR-004**: The profile step MUST treat already-saved profile values as the starting point on re-entry and update the existing profile in place rather than creating a duplicate.
- **FR-005**: The profile step MUST validate submitted values and reject invalid input with clear, field-specific feedback, without persisting a partial or invalid profile.
- **FR-006**: The wallet-creation wizard, when entered as part of onboarding, MUST default the recovery-phrase length to 24 words.
- **FR-007**: The wallet-creation wizard MUST accept a pre-filled default wallet name supplied by the onboarding flow and display it as the initial name value.
- **FR-008**: The user MUST be able to override both the defaulted wallet name and the defaulted recovery-phrase length before creating the wallet, and the wallet MUST be created with the user's chosen values.
- **FR-009**: Wallet creation outside the onboarding wizard MUST remain functional and MUST NOT be forced into onboarding-specific defaults when no onboarding defaults are supplied.
- **FR-010**: The current-user information returned for an authenticated user MUST include whether that user's email address has been verified.
- **FR-011**: The email-verification status MUST accurately reflect the user's verified/unverified state, including an unambiguous representation when the status is not applicable or unknown.
- **FR-012**: The profile capture step MUST be reachable as part of the standard first-run onboarding journey and MUST allow the user to continue onboarding after completing (or skipping optional parts of) it.

### Key Entities *(include if feature involves data)*

- **User Profile (Persona)**: The user's self-asserted personal information (e.g. name and basic contact details), owned by and associated with a single authenticated user. Represents what the user has told the platform about themselves; used to pre-fill experiences and seed identity-bearing actions. Created or updated during the onboarding profile step.
- **Current-User Information**: The summary the application receives about the signed-in user (identity, organisation, roles, and now email-verification status). Read-only projection derived from the authenticated session/account.
- **Wallet Creation Request**: The set of choices made when creating a wallet (name, algorithm, recovery-phrase length, optional passphrase). During onboarding it carries pre-filled defaults (name, 24-word length) that the user may override.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new user can complete the "Complete your profile" onboarding step in under 1 minute, and the entered information is retrievable on their next visit.
- **SC-002**: For users who complete onboarding, profile information captured during onboarding is available to pre-fill or back subsequent experiences in 100% of cases where the user submitted it successfully.
- **SC-003**: When entering the wallet-creation wizard during onboarding, the recovery-phrase length is preset to 24 words and the wallet name is pre-filled in 100% of onboarding sessions, with both remaining user-editable.
- **SC-004**: The current-user information correctly reports email-verification status for verified and unverified users in 100% of checks.
- **SC-005**: No regression in the existing (non-onboarding) wallet-creation flow — wallets can still be created with any supported recovery-phrase length and a user-chosen name.

## Assumptions

- The "self-asserted profile" referenced in the description corresponds to the platform's existing user persona concept (personal-context, self-asserted attributes); seeding the persona means writing the user's persona during onboarding.
- "name default" for the wallet wizard means pre-filling the wallet **name** field with a sensible default value passed from the onboarding flow (the wizard already accepts a default-name input); it does not mean changing the cryptographic algorithm default.
- "12/24 default 24" means the recovery-phrase (mnemonic) word-count selector — which currently defaults to 12 — should default to 24 in the onboarding context, while still offering the existing 12/15/18/21/24 choices.
- "add EmailVerified to /api/auth/me" means extending the current-user information returned by the authenticated current-user endpoint to include the email-verification flag already tracked on the user account; no new verification mechanism is introduced.
- Email-verification state is already maintained on the user account today; this feature surfaces it rather than implementing verification.
- The onboarding journey, wallet wizard, and current-user endpoint already exist; this feature extends them rather than introducing new authentication, wallet, or persona subsystems.
- Profile capture is scoped to the personal context for the signed-in user; multi-context or organisation-on-behalf-of profile capture is out of scope for this feature.
