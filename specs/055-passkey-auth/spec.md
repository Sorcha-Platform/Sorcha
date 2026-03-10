# Feature Specification: Passkey (WebAuthn/FIDO2) Authentication

**Feature Branch**: `055-passkey-auth`
**Created**: 2026-03-10
**Status**: Draft
**Input**: User description: "Passkey (WebAuthn/FIDO2) support for Sorcha using Fido2NetLib. Two use cases: (1) Org users register passkeys as 2FA alongside TOTP. (2) Public users use passkeys as primary auth alongside social login (Google, Microsoft, GitHub, Apple). Method-first registration flow. Prefer discoverable credentials."

## User Scenarios & Testing

### User Story 1 — Org User Registers a Passkey as Second Factor (Priority: P1)

An organizational user who already authenticates with email and password wants to add a passkey as an alternative second factor alongside their existing TOTP setup. They navigate to their security settings, click "Add Passkey", and the browser prompts them to create a credential using their device's biometric, PIN, or security key. After successful registration, the passkey appears in their list of registered credentials with a friendly name. On their next login, after entering their password, they are offered the choice to verify with TOTP or passkey.

**Why this priority**: Org users are the primary audience today. Adding passkey 2FA provides phishing-resistant authentication for the most security-sensitive accounts — administrators managing registers, blueprints, and governance.

**Independent Test**: Can be fully tested by logging in as an org user, registering a passkey in settings, logging out, and completing login using the passkey as a second factor.

**Acceptance Scenarios**:

1. **Given** an authenticated org user on the security settings page, **When** they click "Add Passkey" and complete the browser ceremony, **Then** the passkey is stored and appears in their registered credentials list with a display name and registration date.
2. **Given** an org user with both TOTP and a passkey registered, **When** they log in with email and password, **Then** they are presented with a choice of second factor methods (TOTP code or passkey).
3. **Given** an org user choosing passkey at the 2FA prompt, **When** the browser presents the credential and the user approves (biometric/PIN), **Then** they receive a valid session and are redirected to the application.
4. **Given** an org user with only a passkey registered (no TOTP), **When** they log in with email and password, **Then** they are prompted for passkey verification directly without a method selection step.
5. **Given** an org user on the security settings page, **When** they click "Remove" on a registered passkey, **Then** the credential is deleted and can no longer be used for authentication.

---

### User Story 2 — Public User Signs Up with a Passkey (Priority: P2)

A public user (not affiliated with any organization) visits the Sorcha sign-up page and chooses "Passkey" as their registration method. They are prompted to enter a display name and email address, then the browser prompts them to create a passkey credential. After completing the ceremony, their account is created and they receive an authenticated session. On subsequent visits, they can sign in by clicking "Sign in with Passkey" and selecting their credential.

**Why this priority**: Public user passkey registration establishes the passwordless identity model that enables external participants to interact with registers and blueprints without needing organizational membership.

**Independent Test**: Can be fully tested by visiting the sign-up page, selecting passkey, completing registration, logging out, and signing back in with the passkey.

**Acceptance Scenarios**:

1. **Given** a visitor on the sign-up page, **When** they select "Passkey" as their sign-up method and complete the form and browser ceremony, **Then** a public identity is created and they receive an authenticated session.
2. **Given** a registered public user, **When** they visit the sign-in page and click "Sign in with Passkey", **Then** the browser presents their stored credential and they are authenticated after approval.
3. **Given** a public user with a discoverable credential, **When** they click "Sign in with Passkey" without entering an email, **Then** the browser shows available credentials and the user is authenticated after selecting one.
4. **Given** a public user with a non-discoverable credential, **When** they click "Sign in with Passkey", **Then** they are prompted for their email first, and the browser then presents the matching credential.
5. **Given** a public user who already has an account, **When** they try to register a new passkey with the same email, **Then** they are informed the account already exists and directed to sign in instead.

---

### User Story 3 — Public User Signs Up with Social Login (Priority: P2)

A public user visits the sign-up page and chooses a social provider (Google, Microsoft, GitHub, or Apple). They are redirected to the provider's consent screen, authorize access, and return to Sorcha with their account created using the name and email from the provider. On subsequent visits, they click the same social provider button to sign in.

**Why this priority**: Social login provides the lowest-friction registration path for users who prefer not to manage a separate passkey. It leverages existing OIDC infrastructure already built in the Tenant Service.

**Independent Test**: Can be fully tested by visiting the sign-up page, selecting a social provider, completing the OAuth flow, verifying the account was created with the provider's name/email, and signing in again via the same provider.

**Acceptance Scenarios**:

1. **Given** a visitor on the sign-up page, **When** they select a social provider (Google, Microsoft, GitHub, or Apple), **Then** they are redirected to the provider's authorization page.
2. **Given** a user returning from a successful social authorization, **When** the callback is processed, **Then** a public identity is created with display name and email from the provider, and they receive an authenticated session.
3. **Given** a registered public user who signed up via social login, **When** they visit the sign-in page and click the same social provider, **Then** they are authenticated and receive a valid session.
4. **Given** a user returning from a social provider with an email that already exists as a public identity, **When** the callback is processed, **Then** the existing identity is linked to the social provider and the user is authenticated.

---

### User Story 4 — Org User Authenticates with Passkey at 2FA Step (Priority: P3)

An org user who has registered one or more passkeys wants to use a passkey instead of a TOTP code during the second-factor step of login. The login flow detects available second-factor methods and presents the appropriate options.

**Why this priority**: This is the consumption side of Story 1 — once passkeys are registered, the login flow must support them. Lower priority because Story 1 covers the full end-to-end test including login.

**Independent Test**: Can be tested by logging in with a user who has multiple 2FA methods and verifying each method works independently.

**Acceptance Scenarios**:

1. **Given** an org user with a registered passkey but no TOTP, **When** they enter correct email and password, **Then** the system issues a passkey challenge and the browser prompts for the credential.
2. **Given** an org user with both TOTP and passkey, **When** they enter correct credentials and select "Use passkey", **Then** the browser prompts for the passkey credential and authentication completes on approval.
3. **Given** an org user attempting passkey 2FA, **When** the ceremony fails (user cancels, device error), **Then** they can retry or switch to TOTP.

---

### User Story 5 — Public User Manages Authentication Methods (Priority: P3)

A public user who signed up via passkey wants to add a social login as an alternative, or vice versa. They also want to manage their registered passkeys — view them, add new ones, or remove old ones.

**Why this priority**: Account management is important but not required for initial launch. Users need at least one working auth method before they can manage alternatives.

**Independent Test**: Can be tested by signing in as a public user, navigating to account settings, and adding/removing authentication methods.

**Acceptance Scenarios**:

1. **Given** a public user signed up with a passkey, **When** they navigate to account settings and link a social provider, **Then** they can sign in with either method.
2. **Given** a public user with multiple passkeys registered, **When** they view their security settings, **Then** they see a list of registered passkeys with display names and registration dates.
3. **Given** a public user with multiple auth methods, **When** they remove a passkey, **Then** it is deleted and can no longer be used for sign-in.
4. **Given** a public user with only one auth method remaining, **When** they attempt to remove it, **Then** the system prevents removal and explains that at least one method must remain.

---

### Edge Cases

- What happens when a user's passkey device is lost or broken? They must be able to fall back to TOTP (org users) or social login (public users), or go through an account recovery flow.
- What happens when the browser does not support WebAuthn? The passkey option should be hidden or gracefully degraded with a message explaining browser requirements.
- What happens when a cloned authenticator is detected (signature counter goes backwards)? The credential is disabled immediately and the user is alerted. They must re-register the passkey to resume using it. The active session is not revoked (to avoid false-positive lockouts).
- What happens when a user registers multiple passkeys across devices? All should work independently — the system stores multiple credentials per user.
- What happens during passkey registration if the browser ceremony is cancelled mid-flow? The registration is abandoned cleanly with no partial state persisted.
- What happens when the Relying Party ID (domain) changes? Existing passkey credentials become invalid — this must be documented as a deployment consideration.
- What happens when a social provider is unavailable during sign-in? Provider buttons are shown normally; if the redirect or callback fails, the user sees a clear error message suggesting they retry or use an alternative method.

## Requirements

### Functional Requirements

- **FR-001**: System MUST allow org users to register one or more passkey credentials from their security settings page.
- **FR-002**: System MUST support passkeys as a second-factor method for org users, alongside existing TOTP.
- **FR-003**: System MUST present org users with a choice of available second-factor methods when multiple are registered.
- **FR-004**: System MUST allow org users to remove individual passkey credentials from their account.
- **FR-005**: System MUST allow public users to create an account using a passkey as their primary authentication method.
- **FR-006**: System MUST allow public users to create an account using a social login provider (Google, Microsoft, GitHub, Apple).
- **FR-007**: System MUST present the sign-up flow as method-first: user chooses their auth method before providing personal details.
- **FR-008**: System MUST auto-populate display name and email from social provider profile data when available.
- **FR-009**: System MUST request discoverable (resident key) credentials by default but accept non-discoverable credentials.
- **FR-010**: System MUST support discoverable credential sign-in (user clicks "Sign in with Passkey" without entering an email).
- **FR-011**: System MUST support non-discoverable credential sign-in (user enters email, then completes passkey ceremony).
- **FR-012**: System MUST validate the signature counter on each passkey authentication and immediately disable the credential if a counter regression is detected, requiring re-registration.
- **FR-013**: System MUST allow public users to link additional authentication methods (social providers, additional passkeys) to their account.
- **FR-014**: System MUST prevent users from removing their last remaining authentication method.
- **FR-015**: System MUST persist passkey credential data (credential ID, public key, signature counter, device type) per user.
- **FR-016**: System MUST issue standard JWT tokens upon successful passkey or social authentication, consistent with existing token format.
- **FR-017**: System MUST gracefully handle browsers that do not support WebAuthn by hiding passkey options or displaying a compatibility message.
- **FR-018**: System MUST support multiple passkey registrations per user (minimum 10 per account).

### Key Entities

- **PasskeyCredential**: A WebAuthn credential registered by a user. Stored in the public schema with an owner type discriminator (org user or public identity). Includes credential ID, COSE-encoded public key, signature counter, device display name, attestation type, owner type, owner ID, registration timestamp, and last-used timestamp.
- **PublicIdentity**: A user account not affiliated with an organization. Authenticates via passkeys and/or social login. Has display name, email, status, and linked authentication methods.
- **SocialLoginLink**: A connection between a public identity and an external social provider. Includes provider type, external subject ID, and linked email.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Org users can register a passkey and use it as a second factor within 30 seconds of starting the process.
- **SC-002**: Public users can create an account via passkey or social login within 60 seconds of arriving at the sign-up page.
- **SC-003**: Passkey sign-in completes in under 5 seconds from clicking "Sign in with Passkey" to receiving an authenticated session.
- **SC-004**: All four social providers (Google, Microsoft, GitHub, Apple) complete the sign-up/sign-in flow successfully.
- **SC-005**: Users with multiple second-factor methods can switch between them without re-entering their password.
- **SC-006**: The system correctly detects and flags cloned authenticators via signature counter validation.
- **SC-007**: 100% of passkey operations work across Chrome, Edge, Firefox, and Safari on their current stable releases.

## Clarifications

### Session 2026-03-10

- Q: When a cloned authenticator is detected (signature counter regression), what should happen to the credential? → A: Disable the credential immediately, require re-registration. Do not revoke the active session (avoids false-positive lockouts).
- Q: Should PasskeyCredential be stored in per-org schema or public schema? → A: Unified single table in public schema with owner type discriminator (org user vs public identity). Simplifies credential lookup by credential ID during authentication.
- Q: When a social provider is unavailable during sign-in, what should the user experience be? → A: Show provider buttons normally; display a clear error message after redirect/callback failure, suggesting retry or alternative method. No pre-check of provider availability.

## Assumptions

- The Relying Party ID will be configured per deployment environment (e.g., `sorcha.dev` for production, `localhost` for development).
- Social login for public users will reuse the existing OIDC infrastructure (OidcExchangeService, OidcProvisioningService, OidcDiscoveryService) with a new "public user" flow path.
- The existing PublicIdentity model fields (PassKeyCredentialId, PublicKeyCose, SignatureCounter) will be extended to support multiple credentials per user via a separate PasskeyCredential entity.
- Attestation verification will use "none" attestation conveyance (most compatible, recommended for consumer-facing applications) unless the deployment requires hardware attestation.
- Account recovery for lost passkeys is handled by existing alternative auth methods (TOTP for org users, social login for public users). A dedicated recovery flow is out of scope for this feature.
- Email verification for public users who sign up with passkeys will follow the same flow as existing org user email verification.
