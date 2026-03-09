# Feature Specification: Organization Admin & Identity Management

**Feature Branch**: `054-org-identity-admin`
**Created**: 2026-03-08
**Status**: Draft
**Input**: Organization Admin & Identity Management - OIDC integration, multi-tenant URL resolution, role consolidation, admin console with user management, IDP discovery-first configuration, auto-provisioning with domain restrictions, email verification, social login for public orgs

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Enterprise Org Admin Configures External Identity Provider (Priority: P1)

An organization administrator sets up their company's identity provider so employees can sign in to Sorcha using their existing corporate credentials. The admin navigates to the org settings page, selects their identity provider from a list of well-known providers (Microsoft Entra ID, Google, Okta, Apple, Amazon Cognito) or enters a custom OIDC issuer URL. The system automatically discovers the provider's endpoints via the `.well-known/openid-configuration` document. The admin enters their Client ID and Client Secret (obtained from their identity provider's developer console), tests the connection, and activates it. From that point, any employee can sign in to Sorcha via their corporate identity.

**Why this priority**: Without external identity provider support, every organization must manually create and manage local user accounts. This is the foundational capability that unlocks all other identity management features.

**Independent Test**: Can be fully tested by configuring a test OIDC provider and verifying the discovery, connection test, and activation flow. Delivers value by enabling an organization to connect their identity system to Sorcha.

**Acceptance Scenarios**:

1. **Given** an org admin is on the identity settings page, **When** they select "Microsoft Entra ID" from the provider dropdown, **Then** the issuer URL field is pre-filled with Microsoft's standard issuer URL template and the system fetches the discovery document.
2. **Given** an org admin has entered valid Client ID and Secret, **When** they click "Test Connection", **Then** the system validates the credentials against the provider and displays a success or failure message.
3. **Given** a valid IDP configuration exists but is not yet enabled, **When** the admin activates it, **Then** the provider appears as a sign-in option on the organization's login page.
4. **Given** an org admin enters a custom OIDC issuer URL, **When** the system fetches the discovery document, **Then** the authorization, token, userinfo, and JWKS endpoints are automatically populated.
5. **Given** an org admin enters an invalid or unreachable issuer URL, **When** the system attempts discovery, **Then** an appropriate error message is displayed and the configuration cannot be saved.

---

### User Story 2 - Employee Signs In via Corporate Identity Provider (Priority: P1)

An employee of an organization that has configured an external identity provider visits their organization's Sorcha login page. They click "Sign in with [Provider Name]" and are redirected to their corporate identity provider. After authenticating there, they are redirected back to Sorcha. On their first login, the system automatically creates their account with the default "Member" role. On subsequent logins, the system recognizes them and signs them in directly. If the identity provider does not return an email address, the user is prompted to provide one, and it must be verified before they can proceed.

**Why this priority**: This is the core user-facing login flow. Without it, configuring an IDP (Story 1) has no user value.

**Independent Test**: Can be tested with a configured test IDP by completing the full authorization code flow and verifying account creation and JWT issuance. Delivers value by allowing users to sign in without a separate Sorcha password.

**Acceptance Scenarios**:

1. **Given** an organization has a configured and active IDP, **When** a new user authenticates via the IDP for the first time, **Then** a user account is auto-provisioned with the "Member" role and a Sorcha JWT is issued.
2. **Given** a returning user with an existing account, **When** they authenticate via the IDP, **Then** they are signed in and their last login timestamp is updated.
3. **Given** the IDP returns an email address marked as verified, **When** the account is created, **Then** the email is stored and marked as verified without requiring additional verification.
4. **Given** the IDP does not return an email address (or returns an unverified one), **When** the account is created, **Then** the user is redirected to a "Complete your profile" page where they must provide and verify an email address before accessing the platform.
5. **Given** an organization has configured domain restrictions (e.g., only `@contoso.com`), **When** a user with an email outside the allowed domains authenticates, **Then** they are denied access with a clear message explaining the restriction.
6. **Given** a user has completed OIDC authentication, **When** 2FA is required by the organization, **Then** the user must complete a TOTP challenge before receiving their Sorcha JWT.

---

### User Story 3 - Public User Self-Registers via Social Login (Priority: P1)

A member of the general public visits a public-facing organization's signup page. They can sign up using their existing Microsoft, Google, or Apple account (social login) or create a local account with email and password. Social login is the primary and encouraged path due to lower friction. After signing up, they receive the "Member" role and can immediately participate in workflows they are invited to.

**Why this priority**: Public-facing organizations are a key use case for Sorcha. Self-registration with social login removes the biggest barrier to participation — requiring users to create yet another password.

**Independent Test**: Can be tested by visiting a public org's signup page, completing social login with a test account, and verifying account creation. Delivers value by enabling frictionless public onboarding.

**Acceptance Scenarios**:

1. **Given** a public organization exists, **When** a visitor navigates to its signup page, **Then** they see sign-in buttons for Microsoft, Google, and Apple, plus an option to create a local email/password account.
2. **Given** a visitor clicks "Sign in with Google", **When** they complete authentication at Google, **Then** they are redirected back and an account is auto-provisioned with the "Member" role.
3. **Given** a visitor chooses to create a local account, **When** they submit a valid email and password, **Then** a verification email is sent and the account is created in an unverified state.
4. **Given** a local account has been created but email is not yet verified, **When** the user clicks the verification link within 24 hours, **Then** the email is marked as verified and the account is fully activated.
5. **Given** a local account exists with an unverified email, **When** the 24-hour verification window expires, **Then** the verification token is invalidated and the user must request a new verification email.

---

### User Story 4 - Organization Admin Manages Users (Priority: P2)

An organization administrator needs to view, search, and manage the users in their organization. They access the admin console which shows a dashboard overview (active user count, recent logins, pending invitations, IDP connection status) and a user management table. From here they can search users, filter by role or status, change a user's role, suspend or reactivate accounts, and send invitations to specific email addresses.

**Why this priority**: Day-to-day user management is essential for ongoing operations but depends on having users in the system first (Stories 1-3).

**Independent Test**: Can be tested by creating an org with several users and exercising all admin actions (search, filter, role change, suspend, invite). Delivers value by giving admins control over their organization's users.

**Acceptance Scenarios**:

1. **Given** an org admin navigates to the admin console, **When** the dashboard loads, **Then** it displays the count of active users, users by role, recent login activity, pending invitations, and IDP connection status.
2. **Given** an org has 50+ users, **When** the admin searches by name or email, **Then** matching users are displayed with relevant details (name, email, role, status, last login).
3. **Given** a user currently has the "Member" role, **When** the admin changes their role to "Designer", **Then** the role is updated immediately and the change is recorded in the audit log.
4. **Given** an active user account, **When** the admin suspends the account, **Then** the user can no longer sign in and their existing sessions are invalidated.
5. **Given** an admin creates an invitation for an email address, **When** the invitation is sent, **Then** a time-limited invitation link is generated (valid for 7 days) that allows the recipient to join the organization with a specified role.
6. **Given** a pending invitation exists, **When** the admin revokes it, **Then** the invitation link becomes invalid.

---

### User Story 5 - Organization Creator Sets Up a New Organization (Priority: P2)

A new user creates an organization on Sorcha via self-service signup. They provide an organization name and subdomain (e.g., "acmestores"). The system creates the organization and automatically assigns the creator as Administrator. The organization is immediately accessible via path-based URL (`app.sorcha.io/org/acmestores`) and subdomain URL (`acmestores.sorcha.io`). The admin can optionally configure a custom domain later (e.g., `login.acmestores.com`) by adding a CNAME record.

**Why this priority**: Organization creation is the entry point for all admin functionality, but the basic org CRUD already exists. This story focuses on the multi-tenant URL resolution and custom domain setup which are new capabilities.

**Independent Test**: Can be tested by creating an org and verifying access via all three URL tiers (path, subdomain, custom domain). Delivers value by giving organizations a branded entry point.

**Acceptance Scenarios**:

1. **Given** a user is creating a new organization, **When** they provide a name and subdomain, **Then** the organization is created, the user is assigned the Administrator role, and the org is accessible via `app.sorcha.io/org/{subdomain}`.
2. **Given** an organization exists with subdomain "acmestores", **When** a user navigates to `acmestores.sorcha.io`, **Then** they are served the organization's login/signup page.
3. **Given** an org admin navigates to domain settings, **When** they enter a custom domain (e.g., `login.acmestores.com`), **Then** the system provides CNAME instructions pointing to `{subdomain}.sorcha.io`.
4. **Given** an admin has configured a custom domain and the CNAME is in place, **When** they click "Verify Domain", **Then** the system confirms the CNAME resolution and provisions an SSL certificate.
5. **Given** a verified custom domain, **When** a user navigates to `login.acmestores.com`, **Then** they are served the organization's login/signup page with full SSL.
6. **Given** a subdomain is already taken, **When** a new user tries to create an org with the same subdomain, **Then** the system rejects it with a clear error suggesting alternatives.

---

### User Story 6 - Admin Configures Domain Restrictions (Priority: P3)

An organization administrator wants to restrict which email addresses can auto-provision accounts. They navigate to the domain settings and add one or more allowed email domains (e.g., `@contoso.com`, `@fabrikam.com`). Once configured, only users with matching email addresses can auto-provision on first OIDC login. Users with non-matching emails are rejected unless they have been explicitly invited. The admin can remove restrictions at any time to return to open provisioning.

**Why this priority**: Domain restrictions are an important security control but not required for initial operation. Organizations start open and tighten up later.

**Independent Test**: Can be tested by configuring domain restrictions, then attempting OIDC login with matching and non-matching email addresses. Delivers value by giving enterprise admins control over who can join their organization.

**Acceptance Scenarios**:

1. **Given** an organization has no domain restrictions configured, **When** any user authenticates via OIDC, **Then** they are auto-provisioned regardless of email domain.
2. **Given** an admin adds `contoso.com` to the allowed domains list, **When** a user with `user@contoso.com` authenticates, **Then** they are auto-provisioned normally.
3. **Given** domain restrictions include `contoso.com` only, **When** a user with `user@external.com` authenticates via OIDC, **Then** they are denied with a message explaining that their email domain is not permitted and they should contact the org administrator.
4. **Given** domain restrictions are active, **When** an admin sends an explicit invitation to `partner@external.com`, **Then** that user can join regardless of domain restrictions.
5. **Given** an admin removes all domain restrictions, **When** users with any email domain authenticate, **Then** auto-provisioning resumes for all.

---

### User Story 7 - Org Admin Reviews Audit Log (Priority: P3)

An organization administrator needs to review security-relevant events within their organization. They access the audit log which shows login history (successful and failed), role changes, IDP configuration changes, invitation activity, and account status changes. The log is searchable and filterable by date range, event type, and user.

**Why this priority**: Audit logging is essential for compliance and security monitoring but is a read-only view that depends on all other features generating audit events.

**Independent Test**: Can be tested by performing various admin actions and verifying they appear in the audit log with correct details. Delivers value by providing organizational accountability and security oversight.

**Acceptance Scenarios**:

1. **Given** a user signs in successfully, **When** the admin views the audit log, **Then** the login event appears with timestamp, user identity, authentication method, and source IP.
2. **Given** a failed login attempt (e.g., rejected by domain restriction), **When** the admin views the audit log, **Then** the failed attempt is recorded with the reason for rejection.
3. **Given** multiple audit events exist, **When** the admin filters by event type "role changes", **Then** only role change events are displayed.
4. **Given** audit events span several weeks, **When** the admin filters by date range, **Then** only events within the specified range are displayed.

---

### Edge Cases

- What happens when an organization's external IDP goes down? Users with local password fallback can still sign in. Users who only have OIDC accounts see an error with instructions to contact their admin. The system should detect IDP unavailability (timeout/error) and display a meaningful message rather than a generic error.
- What happens when an IDP returns different email addresses for the same user across logins (e.g., alias changes)? The system matches users by the IDP's `sub` (subject) claim, not by email. Email changes are noted but do not create duplicate accounts.
- What happens when a user is a member of multiple organizations? Each organization has its own user record. The user signs in to a specific org via its URL and gets a JWT scoped to that org. A user can have different roles in different organizations.
- What happens when a custom domain's CNAME is removed after verification? Periodic verification checks (daily) detect broken CNAMEs. The custom domain status is changed to "Failed" and access falls back to subdomain/path URLs. The admin is notified.
- What happens when an invitation link is used by someone other than the intended recipient? Invitations are accepted by whoever clicks the link and authenticates. The system records the actual email of the user who accepted. If the accepting user's email doesn't match the invitation email, the admin is notified via audit log.
- What happens when someone brute-forces a login? Progressive lockout kicks in: 5 failures = 5 min cooldown, 10 = 30 min, 15 = 24 hour lockout, 25 = admin unlock required. Failed attempt counters reset after a successful login. Lockout events are recorded in the audit log.
- What happens when Apple ID returns email/name only on first authorization and the initial token exchange fails? The system retries the exchange. If the failure is permanent, the user is informed that they may need to revoke Sorcha's access in their Apple ID settings and try again so that Apple re-sends the required claims.

## Requirements *(mandatory)*

### Functional Requirements

**OIDC Integration**

- **FR-001**: System MUST support OpenID Connect authorization code flow for external identity provider authentication.
- **FR-002**: System MUST auto-discover IDP endpoints by fetching the `.well-known/openid-configuration` document from the issuer URL.
- **FR-003**: System MUST provide pre-configured issuer URL shortcuts for the top 5 providers: Microsoft Entra ID, Google, Okta, Apple, and Amazon Cognito.
- **FR-004**: System MUST support any OIDC-compliant provider via manual issuer URL entry (generic OIDC).
- **FR-005**: System MUST exchange authorization codes for tokens server-side (never expose tokens to the browser).
- **FR-006**: System MUST validate external ID tokens (signature via JWKS, issuer, audience, expiry, nonce).
- **FR-007**: System MUST issue a Sorcha-native JWT after successful external authentication (full token exchange — downstream services never see external tokens).
- **FR-008**: System MUST store IDP client secrets encrypted at rest.

**Social Login**

- **FR-009**: System MUST support social login via Microsoft, Google, and Apple for public-facing organizations.
- **FR-010**: System MUST support local email/password account creation as an alternative to social login.
- **FR-011**: System MUST support TOTP-based two-factor authentication, configurable per organization.
- **FR-045**: System MUST enforce progressive account lockout on failed login attempts: 5 failures = 5 minute cooldown, 10 failures = 30 minute cooldown, 15 failures = 24 hour lockout, 25 failures = account locked until admin unlock.
- **FR-046**: System MUST enforce a minimum password length of 12 characters for local accounts with no complexity rules (no mandatory uppercase, lowercase, number, or special character requirements).
- **FR-047**: System MUST check new passwords against a breached password list and reject passwords that appear in known breaches.

**User Provisioning**

- **FR-012**: System MUST auto-provision a user account on first successful OIDC login with the "Member" role.
- **FR-013**: System MUST match returning users by the IDP's `sub` (subject) claim, not by email address.
- **FR-014**: System MUST extract email and display name from IDP claims (trying `email`, `preferred_username`, `upn` for email; `name`, `given_name`+`family_name` for display name).
- **FR-015**: System MUST redirect users to a "Complete your profile" page when required claims (email, display name) are missing from the IDP response.
- **FR-016**: System MUST NOT allow platform access until the user has a verified email address.
- **FR-017**: System MUST send a verification email with a time-limited token (24 hours) when the email is not pre-verified by the IDP.
- **FR-018**: System MUST trust the IDP's `email_verified` claim when present and `true`, skipping additional verification.

**Domain Restrictions**

- **FR-019**: System MUST allow organizations to operate with no domain restrictions by default (open provisioning).
- **FR-020**: System MUST allow org admins to configure a list of allowed email domains for auto-provisioning.
- **FR-021**: System MUST reject auto-provisioning for users whose email domain is not in the allowed list when restrictions are active.
- **FR-022**: System MUST allow explicitly invited users to join regardless of domain restrictions.

**Role Management**

- **FR-023**: System MUST support exactly five roles: SystemAdmin, Administrator, Designer, Auditor, and Member.
- **FR-024**: System MUST assign the "Member" role to all auto-provisioned users.
- **FR-025**: System MUST assign the "Administrator" role to the user who creates an organization.
- **FR-026**: System MUST allow Administrators to change any non-SystemAdmin user's role within their organization.
- **FR-027**: System MUST deprecate and migrate the existing User, Consumer, and Developer roles to Member.

**Multi-Tenant URL Resolution**

- **FR-028**: System MUST resolve organizations via path-based URLs (e.g., `app.sorcha.io/org/{subdomain}`).
- **FR-029**: System MUST resolve organizations via subdomain-based URLs (e.g., `{subdomain}.sorcha.io`).
- **FR-030**: System MUST resolve organizations via custom domains (e.g., `login.acmestores.com`) mapped via CNAME.
- **FR-031**: System MUST provide CNAME setup instructions when an admin configures a custom domain.
- **FR-032**: System MUST verify custom domain CNAME resolution before activating the custom domain.
- **FR-033**: System MUST provision and manage SSL certificates for verified custom domains.
- **FR-034**: System MUST periodically verify custom domain CNAME records and deactivate domains whose records are removed.
- **FR-035**: System MUST use a standardised OIDC callback URL pattern (`/auth/callback/{orgSubdomain}`) regardless of the entry URL tier.

**Organization Admin Console**

- **FR-036**: System MUST provide a dashboard showing active user count, users by role, recent login activity, pending invitations, and IDP connection status.
- **FR-037**: System MUST provide a user management table with search, filter (by role, status), inline role change, and suspend/activate actions.
- **FR-038**: System MUST support invitation-based onboarding with time-limited links (7-day default expiry) and specified roles.
- **FR-039**: System MUST allow admins to revoke pending invitations.
- **FR-040**: System MUST record security-relevant events in an audit log (logins, failed logins, role changes, IDP config changes, invitation activity, account status changes).
- **FR-041**: System MUST allow admins to search and filter the audit log by date range, event type, and user.
- **FR-048**: System MUST retain audit events for 12 months by default, with the retention period configurable by org admins.
- **FR-049**: System MUST automatically purge audit events older than the configured retention period.

**Organization Setup**

- **FR-042**: System MUST support hybrid org creation — self-service for any user plus platform-provisioned by SystemAdmins.
- **FR-043**: System MUST distinguish between Standard organizations (enterprise, IDP-configured) and Public organizations (self-registration enabled, social login).
- **FR-044**: System MUST enable self-registration by default for Public organizations and disable it by default for Standard organizations.

### Key Entities

- **Organization**: A tenant on the Sorcha platform. Has a unique subdomain, optional custom domain, org type (Standard/Public), self-registration setting, allowed email domains, and optional branding. Contains users, an optional IDP configuration, and invitations.
- **IdentityProviderConfiguration**: The external OIDC provider configured for an organization. Stores issuer URL, client credentials (encrypted), discovered endpoints, provider preset type, and activation status. One per organization.
- **UserIdentity**: A user within an organization. Has email (verified), display name, role (one of five), status (Active/Suspended/Deleted), authentication method (Local/OIDC/Invite), optional external IDP subject identifier, optional password hash, and 2FA configuration.
- **OrgInvitation**: A time-limited invitation to join an organization. Contains target email, assigned role, expiry, status (Pending/Accepted/Expired/Revoked), and the inviting admin.
- **CustomDomainMapping**: Maps a custom domain to an organization for URL resolution. Tracks verification status and the fallback subdomain.
- **AuditEvent**: A security-relevant event within an organization. Contains timestamp, event type, actor, target, details, and source information. Subject to configurable retention policy (default 12 months).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An organization administrator can configure an external identity provider and have their first employee sign in within 10 minutes of starting setup.
- **SC-002**: A public user can complete self-registration via social login (Microsoft, Google, or Apple) in under 60 seconds from landing on the signup page.
- **SC-003**: 95% of first-time OIDC logins successfully auto-provision a user account without requiring manual intervention from an administrator.
- **SC-004**: All three URL resolution tiers (path, subdomain, custom domain) resolve to the correct organization within 500ms.
- **SC-005**: Organization administrators can find and modify any user's role within 3 clicks from the admin dashboard.
- **SC-006**: The system supports at least 5 different OIDC providers simultaneously across different organizations without configuration conflicts.
- **SC-007**: Every security-relevant action (login, role change, IDP config change, invitation) is recorded in the audit log within 5 seconds of occurrence.
- **SC-008**: Custom domain setup (from CNAME configuration to verified SSL) completes within 15 minutes when DNS propagation is immediate.
- **SC-009**: Email verification flow completes successfully for 99% of users who click the verification link within the validity window.
- **SC-010**: Role consolidation (8 roles to 5) migrates all existing users without any loss of access or data.

## Clarifications

### Session 2026-03-08

- Q: What brute force protection should apply to failed login attempts? → A: Progressive lockout — 5 failures = 5 min cooldown, 10 failures = 30 min cooldown, 15 failures = 24 hour lockout, 25 failures = admin unlock required.
- Q: What password policy should apply to local email/password accounts? → A: NIST SP 800-63B modern approach — minimum 12 characters, no complexity rules, checked against a breached password list.
- Q: How long should audit events be retained? → A: 12 months by default, configurable by org admin (can increase or decrease).

## Assumptions

- The platform domain will be `sorcha.io` (or a configured equivalent) with wildcard DNS and SSL support for subdomain-based resolution.
- Apple's OIDC quirk (email/name only on first authorization) is handled by capturing and persisting claims on first login; the system will not repeatedly prompt for claims that Apple provides only once.
- Custom domain SSL certificates will be managed by the hosting platform (e.g., Azure managed certificates) rather than being manually provisioned.
- The existing TOTP 2FA implementation in the Tenant Service is functional and will be reused for post-OIDC 2FA challenges.
- The existing `Organization.Subdomain` field serves as the universal org identifier across all three URL tiers.
- IDP discovery documents are cached and refreshed periodically (e.g., every 24 hours) rather than fetched on every authentication request.
- The email verification flow uses a simple token-in-link approach (not a code entry approach) for maximum compatibility across email clients.
