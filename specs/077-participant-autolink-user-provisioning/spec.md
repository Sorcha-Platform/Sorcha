# Feature Specification: Auto-Register Participant & Auto-Link Wallet + PlatformUser Admin Provisioning

**Feature Branch**: `077-participant-autolink-user-provisioning`
**Created**: 2026-03-30
**Status**: Draft
**Input**: Close GAP-018 (wallet creation auto-links participant) and GAP-019 (admin can provision platform users in private orgs). Both are P1 blockers for the multi-org ConstructionPermit walkthrough.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Wallet Creation Automatically Registers Participant and Links Wallet (Priority: P1)

A user creates a new wallet through the wallet creation wizard. After the wallet is created, the system automatically ensures the user has a participant identity record in their current organisation and links the newly created wallet to it — without the user needing to navigate to separate participant registration or wallet-linking pages. When the user's session token is refreshed (which already happens after wallet creation), it now includes the `wallet_address` claim. The user can immediately subscribe to wallet notifications and participate in workflows.

**Why this priority**: Without this, every new wallet is "dead on arrival" — the user creates a wallet but can't receive action notifications (ActionsHub rejects the subscription) and can't be resolved as a blueprint participant. The existing challenge/verify wallet-link flow is appropriate for linking *someone else's* wallet, but for a wallet the user just created with their own mnemonic, ownership is already proven.

**Independent Test**: Can be tested by creating a new wallet as a user with no existing participant record, then immediately attempting to subscribe to the wallet in ActionsHub — the subscription should succeed without any manual participant registration or wallet linking steps.

**Acceptance Scenarios**:

1. **Given** a user with no participant record in their organisation, **When** they create a wallet through the wizard, **Then** a participant identity record is automatically created for them in their current organisation using their display name and email.
2. **Given** a user who already has a participant record, **When** they create a wallet, **Then** the existing participant record is reused (no duplicate created) and the new wallet is linked to it.
3. **Given** a newly created wallet, **When** the wallet creation completes, **Then** the wallet is automatically linked to the user's participant record without requiring a challenge/verify signature flow.
4. **Given** the auto-link completes, **When** the user's session token is refreshed (which already happens in the wallet creation flow), **Then** the new token includes the `wallet_address` claim for the linked wallet.
5. **Given** the auto-link completes, **When** the user attempts to subscribe to wallet notifications via ActionsHub, **Then** the subscription succeeds because the participant service confirms the wallet is linked.
6. **Given** a wallet creation where the participant auto-registration or auto-link fails (e.g., participant service unavailable), **When** the failure occurs, **Then** the wallet is still created successfully — the auto-link failure is logged as a warning but does not block wallet creation.

---

### User Story 2 - System Admin Creates Platform Users in Private Organisations (Priority: P1)

A system administrator needs to provision users in a private organisation for testing, onboarding, or multi-org workflow setup. They call a single endpoint that creates the cross-organisation identity (platform user), the organisation-scoped user record, and the organisation membership — all in one operation. The admin can optionally set a password and skip email verification so the user can log in immediately.

**Why this priority**: Without this, users in private organisations can't log in. The existing `AddUserToOrganization` endpoint only creates a partial record (no platform user, no password hash). This blocks all multi-org testing including the ConstructionPermit walkthrough.

**Independent Test**: Can be tested by calling the admin provisioning endpoint to create a user in a private org with a password and `skipEmailVerification: true`, then immediately logging in as that user with the provided credentials.

**Acceptance Scenarios**:

1. **Given** a system administrator, **When** they create a user with an email, display name, organisation ID, role, and password, **Then** a platform user record is created (or an existing one is reused if the email already exists), a user identity is created in the specified organisation, and an organisation membership record is created.
2. **Given** a user creation request with `skipEmailVerification: true`, **When** the user is created, **Then** the user's email is marked as verified immediately and they can log in without waiting for a verification email.
3. **Given** a user creation request without a password, **When** the user is created, **Then** the user record is created but they must use a social login or password reset flow to set credentials before logging in.
4. **Given** an email address that already has a platform user record (from another organisation), **When** the admin creates the user in a new organisation, **Then** the existing platform user is reused and only a new user identity and organisation membership are created in the target organisation.
5. **Given** a non-system-administrator, **When** they attempt to call the user provisioning endpoint, **Then** the request is rejected with an authorisation error.

---

### User Story 3 - System Admin Resets User Passwords (Priority: P2)

A system administrator can reset a user's password without knowing the old password. This supports scenarios where users are locked out, onboarding requires a known initial password, or testing requires predictable credentials.

**Why this priority**: Supports operational scenarios but is not strictly required for the multi-org walkthrough (users can be created with passwords via US2). Useful for day-to-day administration.

**Independent Test**: Can be tested by creating a user with one password, then using the admin reset endpoint to set a new password, and verifying the user can only log in with the new password.

**Acceptance Scenarios**:

1. **Given** a system administrator and an existing platform user, **When** the admin sets a new password, **Then** the user's password hash is updated and they can log in with the new password.
2. **Given** a password reset, **When** the user next attempts to log in with the old password, **Then** the login is rejected.
3. **Given** a non-system-administrator, **When** they attempt to reset a user's password, **Then** the request is rejected with an authorisation error.
4. **Given** a non-existent user ID, **When** the admin attempts to reset the password, **Then** a clear error is returned indicating the user was not found.

---

### Edge Cases

- What happens if auto-participant-registration fails because the user doesn't belong to any organisation? The auto-registration is skipped with a warning log. The wallet is still created. The user can manually register later.
- What happens if the wallet address is already linked to a different participant (platform-wide uniqueness)? The auto-link is skipped with a warning. This should not happen in normal usage since a user just created the wallet, but protects against race conditions.
- What happens if the user creates multiple wallets? Each wallet is auto-linked to the same participant record. The `wallet_address` JWT claim reflects the first active link (existing behaviour).
- What happens if the admin creates a user with an invalid email format? The request is rejected with a validation error before any records are created.
- What happens if the admin creates a user in an organisation that doesn't exist? The request is rejected with a clear "organisation not found" error.
- What happens if the password provided does not meet the NIST password policy? The request is rejected with specific policy violation details (minimum length, breach check, etc.).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST automatically create a participant identity record for the user during wallet creation if no participant record exists for that user in their current organisation.
- **FR-002**: The system MUST automatically link the newly created wallet to the user's participant record without requiring a challenge/verify signature flow.
- **FR-003**: The auto-link MUST respect platform-wide wallet uniqueness — if the wallet address is already linked to another participant, the auto-link is skipped with a warning.
- **FR-004**: Auto-participant-registration and auto-link failures MUST NOT block wallet creation — failures are logged as warnings and the wallet is still created successfully.
- **FR-005**: After auto-linking, the user's refreshed session token MUST include the `wallet_address` claim for the linked wallet (leveraging the existing token refresh that already occurs after wallet creation).
- **FR-006**: The system MUST provide a single admin endpoint to create a platform user with user identity and organisation membership in one operation.
- **FR-007**: The admin user creation endpoint MUST accept an optional password that is hashed server-side using the existing password policy (NIST compliance, breach check).
- **FR-008**: The admin user creation endpoint MUST accept a `skipEmailVerification` flag that marks the user's email as verified immediately.
- **FR-009**: The admin user creation endpoint MUST reuse an existing platform user record if one exists with the same email address, creating only the new user identity and organisation membership.
- **FR-010**: The admin user creation endpoint MUST be restricted to system administrators only.
- **FR-011**: The system MUST provide an admin endpoint to reset a user's password without knowing the old password, restricted to system administrators.
- **FR-012**: The password reset endpoint MUST validate the new password against the same NIST policy used for registration.
- **FR-013**: The admin user creation endpoint MUST validate the target organisation exists and return a clear error if not found.

### Key Entities

- **ParticipantIdentity**: Organisation-scoped participant record linking a user to their role in the platform. Created automatically during wallet creation (US1) or manually via existing self-registration.
- **LinkedWalletAddress**: Links a wallet address to a participant. Created automatically during wallet creation (US1, bypassing challenge/verify) or manually via existing wallet-link flow.
- **PlatformUser**: Cross-organisation identity anchor with email uniqueness, password hash, email verification status, and social login links. Created by admin provisioning (US2) or self-registration.
- **UserIdentity**: Organisation-scoped user record with roles and permissions. Created alongside platform user during provisioning.
- **PlatformUserOrgMembership**: Denormalised lookup linking a platform user to an organisation with their role. Created during provisioning to enable organisation switching and login.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can create a wallet and immediately subscribe to wallet notifications in ActionsHub without any manual participant registration or wallet linking steps — verified by end-to-end test.
- **SC-002**: Wallet creation completes in under 5 seconds including automatic participant registration and wallet linking — verified by timing the full wizard flow.
- **SC-003**: Admin can create a user in a private organisation and that user can log in within 30 seconds — verified by provisioning + login test.
- **SC-004**: Admin-provisioned users with `skipEmailVerification: true` can log in immediately without email confirmation — verified by login test.
- **SC-005**: Existing wallet creation flows (users who already have participant records) continue to work with zero regressions — verified by existing test suite.
- **SC-006**: All existing tests (1,200+) continue to pass — verified by full test suite run.
- **SC-007**: Auto-link failure does not prevent wallet creation — verified by simulating participant service unavailability during wallet creation.

## Assumptions

- The existing token refresh call in the wallet creation wizard (`AuthService.RefreshTokenAsync()`) is reliable and will pick up the new `wallet_address` claim after auto-linking.
- The auto-link bypasses the challenge/verify flow by directly creating a `LinkedWalletAddress` record — this is safe because the user just proved ownership by generating the mnemonic and creating the wallet.
- The existing NIST password policy and breach check infrastructure in `RegistrationService` can be reused for admin-provisioned passwords.
- System administrator authorisation policies already exist and can be applied to the new endpoints.
- The `ParticipantService.SelfRegisterAsync()` logic can be called internally (service-to-service) without going through the HTTP endpoint.
