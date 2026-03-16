# Feature Specification: Platform Organisation Topology

**Feature Branch**: `058-platform-org-topology`
**Created**: 2026-03-16
**Status**: Draft
**Input**: Multi-tier organisation topology with public org, social login, and blueprint-driven org creation
**Design Spec**: `docs/superpowers/specs/2026-03-16-platform-org-topology-design.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Bootstrap and Enable Public Organisation (Priority: P1)

After a fresh Sorcha installation, a system administrator bootstraps the platform, then enables the public organisation so that external users can sign up. The bootstrap creates two organisations: the system admin org (active, for platform management) and the public org (initially disabled). The admin navigates to Platform Settings in the admin panel and toggles the public org on, configuring branding and enabling social login providers.

**Why this priority**: Nothing else works until the platform is bootstrapped with both orgs and the public org can be enabled. This is the foundation for all other stories.

**Independent Test**: Can be fully tested by running the bootstrap endpoint and verifying both orgs exist, then calling the platform settings endpoint to enable the public org and confirming its status changes.

**Acceptance Scenarios**:

1. **Given** a fresh installation with no data, **When** the bootstrap endpoint is called with admin credentials, **Then** both the system admin org and public org are created, the admin receives a valid authentication token, and the public org is in a disabled state.
2. **Given** a bootstrapped system with a disabled public org, **When** a system admin enables the public org via Platform Settings, **Then** the public org becomes active, self-registration is enabled, and the configured social login providers are available on the signup page.
3. **Given** the bootstrap has already been completed, **When** the bootstrap endpoint is called again, **Then** the request is rejected with a conflict response.
4. **Given** a bootstrapped system, **When** a non-system-admin attempts to access Platform Settings, **Then** the request is denied.

---

### User Story 2 - Public Organisation Signup via Social Login (Priority: P1)

A new user visits the Sorcha platform and signs up using a social login provider (Google, GitHub, Microsoft, or Apple). The system creates a platform-level identity and an organisation membership in the public org. The user lands in the public org with a member role and can immediately use the platform.

**Why this priority**: Social login is the primary onboarding path for the public org and the main goal of this feature.

**Independent Test**: Can be fully tested by initiating a social login flow, completing the OAuth exchange, and verifying the user has a platform identity and public org membership with a valid session.

**Acceptance Scenarios**:

1. **Given** the public org is enabled with Google configured as a social provider, **When** a new user clicks "Sign in with Google" and completes the OAuth flow, **Then** a platform identity is created, the user is added to the public org as a member, and a valid session token is issued.
2. **Given** a user previously signed up with Google, **When** they click "Sign in with Google" again, **Then** they are authenticated against their existing platform identity and receive a session scoped to the public org.
3. **Given** a user signed up with Google, **When** they link their GitHub account from profile settings, **Then** both social providers are associated with the same platform identity, and either can be used for future login.
4. **Given** the public org is disabled, **When** a user attempts to sign up via social login, **Then** the signup page is not accessible and returns an appropriate error.

---

### User Story 3 - Public Organisation Signup via Email/Password (Priority: P2)

A new user signs up to the public org using email and password. The system creates their platform identity, adds them to the public org, and sends an email verification link. The user must verify their email before accessing full platform features.

**Why this priority**: Email/password is the fallback signup path for users who prefer not to use social login. Important for accessibility but secondary to social login.

**Independent Test**: Can be fully tested by submitting a registration form with email and password, verifying the platform identity and org membership are created, and completing email verification.

**Acceptance Scenarios**:

1. **Given** the public org is enabled, **When** a new user submits a valid email and password, **Then** a platform identity is created with a hashed password, the user is added to the public org, and a verification email is sent.
2. **Given** a user registered but has not verified their email, **When** they click the verification link, **Then** their email is marked as verified and they gain full access.
3. **Given** a user's email is already registered, **When** another registration attempt is made with the same email, **Then** the request is rejected with an appropriate message (without revealing whether the email exists).
4. **Given** a user registered with email/password, **When** they later sign in with a social provider that uses the same email, **Then** the social provider is linked to their existing platform identity.

---

### User Story 4 - Self-Service Organisation Creation via Blueprint (Priority: P2)

A public org member triggers the "Create Organisation" blueprint to create their own private organisation. The workflow collects organisation details, validates the request (subdomain availability, creation limit, email verification), provisions the new org, and makes the requesting user its administrator.

**Why this priority**: This is the self-service growth path — how the platform scales from single public org to many private orgs. Core to the business model but requires signup (P1) to work first.

**Independent Test**: Can be fully tested by having a public org member trigger the org creation blueprint, completing the workflow steps, and verifying the new org exists with the user as administrator.

**Acceptance Scenarios**:

1. **Given** a verified public org member, **When** they submit a valid organisation name and subdomain through the Create Organisation blueprint, **Then** a new private org is created, they become its administrator, and the org appears in their org switcher.
2. **Given** a user who has already created their maximum number of private orgs, **When** they attempt to create another, **Then** the blueprint validation rejects the request with a clear message about the creation limit.
3. **Given** a requested subdomain that is already taken, **When** the blueprint validates the request, **Then** the user is informed the subdomain is unavailable and can choose another.
4. **Given** a user whose email is not verified, **When** they attempt to create an org, **Then** the blueprint validation rejects the request and prompts them to verify their email first.

---

### User Story 5 - Admin-Initiated Organisation Creation with Invite (Priority: P2)

A system administrator creates a private organisation on behalf of someone and invites them by email to be the org's administrator. The invited person receives an email, signs up or logs in, accepts the invitation, and becomes the new org's admin.

**Why this priority**: Provides a manual org creation path for system admins, important for enterprise onboarding and cases where the blueprint path isn't appropriate.

**Independent Test**: Can be fully tested by having a system admin create an org, verifying the invite email is sent, and confirming the invitee can accept and becomes the org admin.

**Acceptance Scenarios**:

1. **Given** a system admin, **When** they create a new organisation with an invitee email, **Then** the org is created and an invitation email is sent to the specified address.
2. **Given** an invitation email was sent to a new user, **When** they follow the link and complete signup, **Then** they are added to the new org as administrator.
3. **Given** an invitation email was sent to an existing platform user, **When** they follow the link and log in, **Then** they are added to the new org as administrator without creating a duplicate identity.

---

### User Story 6 - Organisation Switching (Priority: P3)

A user who belongs to multiple organisations (e.g., the public org and their private org) can switch between them using an org switcher in the navigation. Switching changes their active context and session permissions.

**Why this priority**: Essential UX once users can create orgs, but only matters after org creation works.

**Independent Test**: Can be fully tested by creating a user with memberships in two orgs, switching between them, and verifying the session context changes appropriately.

**Acceptance Scenarios**:

1. **Given** a user belongs to the public org and a private org, **When** they open the org switcher, **Then** they see both orgs listed with their name and their role in each.
2. **Given** a user is viewing the public org, **When** they select their private org from the switcher, **Then** their session context changes to the private org with appropriate permissions.
3. **Given** a user belongs to only one org, **When** they view the navigation, **Then** the org switcher shows their current org but has no other options to switch to.

---

### User Story 7 - Platform Organisation Management (Priority: P3)

System administrators can view all organisations, see user lists (read-only for private orgs), and disable/suspend organisations. This provides platform governance without intruding on private org autonomy.

**Why this priority**: Governance and moderation capability. Important for platform health but secondary to core signup and org creation flows.

**Independent Test**: Can be fully tested by having a system admin list all orgs, view a private org's user list, and suspend an org, verifying the actions and permission boundaries.

**Acceptance Scenarios**:

1. **Given** a system admin, **When** they view the Platform Organisations page, **Then** they see all orgs with name, subdomain, status, user count, and creation date.
2. **Given** a system admin viewing a private org, **When** they view its users, **Then** they see names, roles, and status but cannot modify anything.
3. **Given** a system admin, **When** they disable a private org, **Then** the org's status changes to suspended and its members can no longer authenticate to it.
4. **Given** a system admin viewing the platform organisations, **When** they attempt to access a private org's registers or blueprints, **Then** the request is denied — audit access is limited to metadata and user lists only.
5. **Given** a private org admin, **When** they attempt to assign the SystemAdmin role to a user in their org, **Then** the request is rejected — SystemAdmin role is restricted to the system admin org.

---

### Edge Cases

- What happens when a user signs up with social login using an email that already has an email/password account? The social provider is linked to the existing platform identity.
- What happens when the public org is disabled while users are logged in? Active sessions continue until they expire; new logins and signups are blocked.
- What happens when a private org is suspended while a blueprint is running? Active blueprint instances in the org are paused; the org admin is notified when the org is re-enabled.
- What happens when the system admin org is accidentally disabled? Platform orgs (marked `IsPlatformOrg`) cannot be disabled or deleted — the system must reject such requests.
- What happens when a user tries to access a private org they don't belong to? They are redirected to the public org login with a message that they are not a member.
- What happens during org creation if the provisioning step partially fails (e.g., schema created but user creation fails)? The entire operation is rolled back — no partial org state is persisted.
- What happens when `MaxOrgsPerUser` is changed after users have already created orgs? Existing orgs are not affected; the new limit only applies to future creation attempts.

## Requirements *(mandatory)*

### Functional Requirements

**Organisation Topology**

- **FR-001**: System MUST create two platform organisations during bootstrap: a system admin org (active) and a public org (disabled by default).
- **FR-002**: System MUST enforce exactly one system admin org and exactly one public org per installation — both are marked as platform orgs and cannot be deleted or suspended by any user.
- **FR-003**: System MUST support creation of multiple private organisations, each fully autonomous with their own users, data, and permissions.
- **FR-004**: System admin MUST be able to enable or disable the public org via a UI toggle in the admin panel, which atomically changes the org status and self-registration setting.

**Platform Identity**

- **FR-005**: System MUST maintain a platform-wide identity for each person, separate from their per-organisation memberships, used as the single point of authentication.
- **FR-006**: System MUST support linking multiple social login providers (Google, GitHub, Microsoft, Apple) to a single platform identity.
- **FR-007**: System MUST support passkey (FIDO2/WebAuthn) authentication, with multiple passkey credentials per platform identity.
- **FR-008**: System MUST maintain a denormalized lookup of each platform user's org memberships to support the org switcher without cross-tenant queries.
- **FR-009**: Platform identity email MUST be unique across the entire installation.

**Authentication**

- **FR-010**: System MUST authenticate all users against their platform identity first, then scope the session to a specific organisation.
- **FR-011**: System MUST support social login (Google, GitHub, Microsoft, Apple) as a signup and login method for the public org, creating or matching platform identities.
- **FR-012**: System MUST support email/password signup for the public org with email verification required for full access.
- **FR-013**: System MUST support direct login to private orgs via subdomain — authenticating at the platform level, then verifying org membership.
- **FR-014**: System MUST allow users to switch between their organisations, issuing a new session scoped to the selected org.

**Organisation Creation**

- **FR-015**: System MUST provide a built-in "Create Organisation" blueprint in the public org that collects org details, validates the request, provisions the org, and assigns the requester as admin.
- **FR-016**: System MUST enforce a configurable maximum number of private orgs per user (default: 1).
- **FR-017**: System MUST validate that the user's email is verified before allowing org creation.
- **FR-018**: System MUST validate subdomain uniqueness and format (3-50 chars, lowercase alphanumeric + hyphens, not reserved).
- **FR-019**: System admin MUST be able to create organisations directly and invite an administrator by email, bypassing the blueprint workflow.
- **FR-020**: Org provisioning MUST be atomic — partial failures must roll back completely with no orphaned state.

**Permission Model**

- **FR-021**: System MUST derive platform permissions from the user's role within the system admin org — no separate platform role concept needed.
- **FR-022**: SystemAdmin role MUST only be assignable to users in the system admin org — attempts to assign it in any other org MUST be rejected.
- **FR-023**: System admins MUST have full management access to the public org (users, settings, blueprints).
- **FR-024**: System admins MUST have read-only audit access to private orgs, limited to org metadata (name, status, creation date, user count) and user list (names, roles, status).
- **FR-025**: System admins MUST NOT be able to access private org content (registers, blueprints, transactions, wallet operations).
- **FR-026**: System admins MUST be able to disable or suspend any private org.

**Social Login Configuration**

- **FR-027**: System MUST support configuring multiple social login providers per organisation (one-to-many relationship), each with their own credentials and enabled/disabled state.
- **FR-028**: System MUST include GitHub as a supported social login provider type.

### Key Entities

- **PlatformUser**: Platform-wide identity anchor. Holds authentication credentials (email, password hash, lockout state), links to social logins and passkey credentials, tracks org creation count. One per person across the installation.
- **PlatformSocialLogin**: Links a social provider (Google, GitHub, Microsoft, Apple) to a PlatformUser. Multiple providers can be linked to one identity. Unique per provider-subject combination.
- **PlatformUserOrgMembership**: Denormalized lookup recording which organisations a platform user belongs to, with their highest role. Used for the org switcher. Maintained in sync with per-org user records.
- **PasskeyCredential**: FIDO2/WebAuthn credential linked to a PlatformUser. Supports multiple credentials per user. Retains all existing metadata (device type, attestation, status).
- **PlatformSettings**: Singleton configuration for platform-level flags: public org enabled/disabled, max orgs per user. Social provider configuration stored separately per organisation.
- **Organisation**: Gains a platform org flag to mark the system admin and public orgs as undeletable. Gains one-to-many relationship with identity provider configurations for multi-provider support.
- **UserIdentity**: Per-org user record. Gains a reference to PlatformUser for cross-org identity linking. Authentication fields (password, lockout, verification) move to PlatformUser.

### Assumptions

- Each Sorcha installation has exactly one system admin org and one public org — multi-installation federation is out of scope.
- Social login providers use standard OAuth2/OIDC flows — no custom provider protocols.
- The "Create Organisation" blueprint follows existing blueprint patterns and can be seeded from the template catalog.
- Email delivery infrastructure exists or can be configured — the feature depends on email for verification and invitations but does not specify the email provider.
- The bootstrap endpoint remains the entry point for initial setup — no separate installer or setup wizard.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: New users can complete social login signup to the public org in under 30 seconds (from clicking "Sign in with [Provider]" to landing in the public org).
- **SC-002**: New users can complete email/password signup in under 2 minutes (including form submission; email verification is separate).
- **SC-003**: Public org members can create a private org via the blueprint in under 2 minutes (from triggering the blueprint to the org being accessible).
- **SC-004**: System admins can enable the public org and configure at least one social provider in under 5 minutes from a freshly bootstrapped system.
- **SC-005**: Organisation switching completes in under 2 seconds (from selection to the new org context being fully loaded).
- **SC-006**: System admins can view the full org list and audit a private org's user list within 3 interactions (navigate to page, select org, view users).
- **SC-007**: 100% of org creation attempts that fail mid-provisioning leave zero orphaned state (full rollback verified).
- **SC-008**: Platform supports at least 4 simultaneous social login providers on the public org without configuration conflicts.
