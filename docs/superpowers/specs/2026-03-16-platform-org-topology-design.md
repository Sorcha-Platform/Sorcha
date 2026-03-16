# Platform Organisation Topology

**Date:** 2026-03-16
**Status:** Draft
**Scope:** Multi-tier organisation model with public org, social login, and blueprint-driven org creation

---

## Overview

Sorcha installations adopt a three-tier organisation topology:

1. **System Admin Org** — Infrastructure control plane, houses platform administrators
2. **Public Org** — Community front door, social login + email/password signup, self-service org creation
3. **Private Orgs** — Autonomous workspaces created via blueprint or system admin

This replaces the current single-org bootstrap model and removes the `PublicIdentity` concept entirely. All users authenticate via `PlatformUser` (public schema) and are authorised via `UserIdentity` (per-org schema).

---

## Org Topology

### System Admin Org (exactly one)

- Well-known ID: `00000000-0000-0000-0000-000000000001` (existing `DefaultOrganizationId`)
- Created at bootstrap, always exists
- `OrgType.Standard`, `SelfRegistrationEnabled = false`, `IsPlatformOrg = true`
- Not visible to public users
- Houses system administrators and auditors
- Member roles double as platform permissions (see Permission Model)

### Public Org (exactly one)

- Well-known ID: `00000000-0000-0000-0000-000000000002` (new)
- Created at bootstrap, **disabled by default** (`Status = Suspended`, `SelfRegistrationEnabled = false`)
- System admin enables via UI toggle in Platform Settings (sets `Status = Active` and `SelfRegistrationEnabled = true` atomically)
- `OrgType.Public`, `IsPlatformOrg = true`
- Supports social login (Google, GitHub, Microsoft, Apple) + email/password signup
- Houses the "Create Organisation" blueprint
- System admin org members have full management powers over this org

### Private Orgs (zero to many)

- Created via blueprint workflow from public org, or by system admin with email invite
- `OrgType.Standard`, `SelfRegistrationEnabled = false`
- Fully autonomous — own admins, users, blueprints, registers
- System admin org members have read-only audit access (metadata + user list only)
- Private org creation limit per user is configurable via `PlatformSettings.MaxOrgsPerUser` (default 1)

---

## Identity Model

### Two-Layer Identity

Authentication and authorisation are separated into two layers:

1. **PlatformUser** (public schema) — Platform-wide identity anchor. Holds authentication credentials (social login, passkey, email/password). One per person across the entire installation.
2. **UserIdentity** (per-org schema) — Org-scoped authorisation record. Holds role, status, org-specific profile. One per person per org. References `PlatformUserId`.

```
┌─────────────────────────────────────────┐
│          PlatformUser (public)          │
│  - Authentication credentials           │
│  - SocialLogins (navigation, 1:many)    │
│  - Email (platform-wide unique)         │
│  - Password hash                        │
│  - PasskeyCredentials (navigation)      │
│  - OrgMemberships (navigation)          │
│  - Created orgs count                   │
├─────────────────────────────────────────┤
│         ┌──────────┼──────────┐         │
│         ▼          ▼          ▼         │
│   UserIdentity  UserIdentity  UserIdentity
│   (public org)  (private A)   (private B)
│   org_00..002   org_{guid}    org_{guid}
│   Role: Member  Role: Admin   Role: Member
└─────────────────────────────────────────┘
```

This enables:
- **Org switcher:** Query `PlatformUserOrgMembership` lookup table by `PlatformUserId` (public schema, avoids cross-schema queries)
- **MaxOrgsPerUser:** Count against `PlatformUser.CreatedOrgsCount`
- **Social login lookup:** Platform-wide by provider + subject on `PlatformSocialLogin`
- **Multiple social providers:** User can link Google AND GitHub via separate `PlatformSocialLogin` records
- **Cross-org JWT re-issuance:** Authenticate once via `PlatformUser`, then scope JWT to any org

### Remove Entirely

- `PublicIdentity` entity + table (public schema)
- `SocialLoginLink` entity + table (public schema)
- `PublicAuthEndpoints.cs`
- `IPublicUserService` / `PublicUserService`
- `IsPublicIdentity` property from `OrganizationContext`

### New Entity: PlatformUser (public schema)

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | Guid | Primary key, referenced by UserIdentity.PlatformUserId |
| `Email` | string | Platform-wide unique, used for login + recovery |
| `DisplayName` | string | Default display name (orgs can override) |
| `PasswordHash` | string? | BCrypt hash; null for social-only users |
| `EmailVerified` | bool | Whether email has been verified |
| `EmailVerifiedAt` | DateTimeOffset? | When email was verified |
| `VerificationToken` | string? | Email verification token |
| `VerificationTokenExpiresAt` | DateTimeOffset? | Token expiry (24h) |
| `PasswordResetTokenHash` | string? | Password reset token (BCrypt) |
| `PasswordResetTokenExpiresAt` | DateTimeOffset? | Reset token expiry (1h) |
| `FailedLoginCount` | int | Progressive lockout counter |
| `LockedUntil` | DateTimeOffset? | Lockout expiry |
| `LockedPermanently` | bool | Permanent lockout (>25 failures) |
| `CreatedOrgsCount` | int (default 0) | Tracks private orgs created (for MaxOrgsPerUser) |
| `Status` | PlatformUserStatus | Active, Suspended, Deleted |
| `CreatedAt` | DateTimeOffset | Registration timestamp |
| `LastLoginAt` | DateTimeOffset? | Last successful authentication |

**Navigations:**
- `SocialLogins` → `ICollection<PlatformSocialLogin>` (multiple providers per user)
- `PasskeyCredentials` → `ICollection<PasskeyCredential>` (multiple passkeys per user)
- `OrgMemberships` → `ICollection<PlatformUserOrgMembership>` (denormalized org lookup)

**Unique indexes:**
- `Email` (platform-wide unique)

### New Entity: PlatformSocialLogin (public schema)

Supports linking multiple social providers to one PlatformUser (e.g., Google AND GitHub).

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | Guid | Primary key |
| `PlatformUserId` | Guid | FK to PlatformUser |
| `Provider` | string | "google", "github", "microsoft", "apple" |
| `Subject` | string | Provider's unique user ID |
| `Email` | string? | Email from provider (may differ from PlatformUser.Email) |
| `DisplayName` | string? | Name from provider profile |
| `LinkedAt` | DateTimeOffset | When the link was established |

**Unique indexes:**
- `(Provider, Subject)` — platform-wide unique per provider

### New Entity: PlatformUserOrgMembership (public schema)

Denormalized lookup table to avoid cross-schema queries for the org switcher. Maintained in sync when `UserIdentity` records are created/deleted.

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | Guid | Primary key |
| `PlatformUserId` | Guid | FK to PlatformUser |
| `OrganizationId` | Guid | FK to Organization |
| `Role` | string | Denormalized highest role (for display in switcher) |
| `JoinedAt` | DateTimeOffset | When user joined this org |

**Unique indexes:**
- `(PlatformUserId, OrganizationId)` — one membership record per user per org

### Retain: PasskeyCredential (public schema, reparented)

Retain existing `PasskeyCredential` entity with all current fields. Only change: replace polymorphic `OwnerType`/`OwnerId` FK with direct `PlatformUserId` FK to `PlatformUser`.

| Change | From | To |
|--------|------|----|
| FK | `OwnerType` + `OwnerId` (polymorphic) | `PlatformUserId` (Guid, FK to PlatformUser) |
| Remove | `OrganizationId` (no longer org-scoped) | — |

All other fields retained as-is: `PublicKeyCose`, `SignatureCounter`, `AaGuid`, `AttestationType`, `DisplayName`, `DeviceType`, `Status` (CredentialStatus enum), `LastUsedAt`, `DisabledAt`, `DisabledReason`, `CreatedAt`.

Supports multiple passkeys per user from day one.

### Modify: UserIdentity (per-org schema)

Add field:

| Field | Type | Purpose |
|-------|------|---------|
| `PlatformUserId` | Guid | FK to PlatformUser — cross-org identity anchor |

Remove fields (moved to PlatformUser):
- `PasswordHash`
- `ExternalIdpSubject`
- `EmailVerified`, `EmailVerifiedAt`
- `VerificationToken`, `VerificationTokenExpiresAt`
- `PasswordResetTokenHash`, `PasswordResetTokenExpiresAt`
- `FailedLoginCount`, `LockedUntil`, `LockedPermanently`

Retain org-scoped fields:
- `Email` (denormalized for org-scoped queries, copied from PlatformUser)
- `DisplayName` (can differ per org)
- `Roles` (org-scoped)
- `Status` (org-scoped — can be active in one org, suspended in another)
- `ProvisionedVia` (how they joined this org)

### Add ProvisioningMethod Value

Update the existing `ProvisioningMethod` enum:
- `Local` — Email/password registration
- `Oidc` — Auto-provisioned via enterprise IDP
- `Invitation` — Org invitation accepted
- `SocialLogin` — Google, Microsoft, GitHub, Apple (new)
- `AdminCreated` — Created by org admin (new)

### Add to Organization

| Field | Type | Purpose |
|-------|------|---------|
| `IsPlatformOrg` | bool (default false) | Marks system admin + public orgs as undeletable |

### Modify: PlatformSettings (public schema, singleton)

Uses existing `SystemConfiguration` for simple key-value flags. Social login provider configuration uses existing `IdentityProviderConfiguration` on the public org.

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | Guid | Primary key |
| `PublicOrgEnabled` | bool (default false) | Controls public org availability |
| `MaxOrgsPerUser` | int (default 1) | Limit on private org creation per user |
| `UpdatedAt` | DateTimeOffset | Last modification time |
| `UpdatedBy` | Guid | System admin who last updated |

Social login provider config (client ID, secret, enabled) is stored via existing `IdentityProviderConfiguration` entities on the public org, not duplicated here. The current one-to-one relationship (`Organization.IdentityProvider`) must be changed to one-to-many (`Organization.IdentityProviders` → `ICollection<IdentityProviderConfiguration>`) to support multiple social providers. Add `GitHub` to the `IdentityProviderType` enum.

### Role Assignment Constraint

`UserRole.SystemAdmin` can only be assigned to users in the system admin org (ID `...0001`). The `UserService` must validate this on role assignment and reject attempts to assign `SystemAdmin` in any other org.

### Well-Known IDs (DatabaseInitializer)

```csharp
public static readonly Guid SystemAdminOrgId = new("00000000-0000-0000-0000-000000000001"); // existing
public static readonly Guid PublicOrgId = new("00000000-0000-0000-0000-000000000002");       // new
```

---

## Authentication Flows

All authentication resolves against `PlatformUser` first, then scopes to an org.

### Public Org Signup (New User)

```
User visits platform → Clicks "Sign up" →
  Path A: Social login → OAuth dance →
    PlatformUser created + PlatformSocialLogin record (Provider, Subject)
    UserIdentity created in public org (ProvisionedVia = SocialLogin)
  Path B: Email/password → Registration form →
    PlatformUser created (PasswordHash via BCrypt)
    UserIdentity created in public org (ProvisionedVia = Local)
    Email verification sent
→ JWT issued with org_id = PublicOrgId, platform_user_id = PlatformUser.Id
```

### Returning User Login

```
User visits platform → Clicks "Sign in" →
  Social: match PlatformSocialLogin by provider + subject → resolve PlatformUser
  Email: match PlatformUser by email, verify password
→ Resolve default org (last used, or public org)
→ JWT issued scoped to that org
```

### Private Org Direct Login

Users can bookmark or navigate directly to a private org's subdomain:

```
User visits private-org.sorcha.io →
  Authenticate via PlatformUser (social or email/password) →
  Verify UserIdentity exists in requested org →
  JWT issued scoped to private org
```

If the user has no UserIdentity in the requested org, they are redirected to the public org login with a message.

### Org Switching

- Users can belong to public org + private org(s)
- UI shows org switcher in global nav
- Switching orgs issues a new JWT scoped to the selected org
- Query: `SELECT * FROM PlatformUserOrgMembership WHERE PlatformUserId = @id` (public schema, no cross-schema query)
- Same `PlatformUser`, different `UserIdentity` per org, different JWT claims

---

## Permission Model

No new enums or JWT claims. System admin org membership + existing `UserRole` = platform permissions.

### Authorization Logic

```
Is the user's org_id the well-known SystemAdminOrgId?
  YES → Check their role:
    SystemAdmin →
      - Enable/disable/configure public org (via Platform Settings UI)
      - Create private orgs + send admin invites
      - Disable/suspend any private org
      - Full management of public org (users, settings, blueprints)
    Administrator →
      - CRUD users in the public org
      - View public org activity
    Auditor →
      - View private org metadata (name, status, created, user count)
      - View private org user list (names, roles, status)
      - Cannot see registers, blueprints, transactions, or content
  NO → Standard org-scoped permissions (existing behaviour, unchanged)
```

### Implementation

A single middleware check — `IsSystemAdminOrgMember()` — compares JWT `org_id` against the well-known system admin org ID. Existing `[RequireRole]` policies handle role checks.

### System Admin Boundaries in Private Orgs

System admins **cannot**:
- Read register entries or transactions
- View or modify blueprints
- Modify users or roles
- Act as a participant in workflows
- Access wallet operations

They **can**:
- View org metadata (name, status, creation date, user count)
- View user list (names, roles, status)
- Disable/suspend the org

---

## Platform Settings API

New endpoints, system admin only. Require YARP API Gateway route additions with `SystemAdmin` authorization policy.

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/platform/settings` | Get public org status, config |
| `PUT` | `/api/platform/settings/public-org` | Enable/disable public org, configure branding |
| `POST` | `/api/platform/organizations` | Admin-initiated org creation with admin email invite |
| `PUT` | `/api/platform/organizations/{id}/status` | Disable/suspend a private org |
| `GET` | `/api/platform/organizations` | List all orgs (metadata only) |
| `GET` | `/api/platform/organizations/{id}/users` | Audit: view org user list (read-only for private orgs) |

---

## "Create Organisation" Blueprint

Built-in blueprint template seeded into the public org at bootstrap.

### Participants

- `requestor` — the public org user
- `system` — automated platform actions

### Actions

1. **Submit Request** (requestor)
   - Collects: orgName, subdomain, description (optional)
   - Schema validates: name 3-100 chars, subdomain format

2. **Validate Request** (system)
   - Subdomain available? (unique check against Organization table)
   - User already at MaxOrgsPerUser? (check PlatformUser.CreatedOrgsCount)
   - User email verified? (check PlatformUser.EmailVerified)
   - Fails fast with reason if any check fails

3. **Provision Organisation** (system)
   - Calls Tenant Service API to create org:
     - Creates Organization record (`OrgType.Standard`, `Status = Active`)
     - Creates PostgreSQL schema (`org_{newOrgId}`)
     - Runs EF migrations for per-org tables
     - Creates `UserIdentity` for requestor in new org (`Role = Administrator`, `PlatformUserId` linked)
     - Increments `PlatformUser.CreatedOrgsCount`
   - Records transaction on public org's register (audit trail)
   - Operation is idempotent — retries safe via unique constraints
   - On partial failure: rollback schema creation, no org record persisted (DB transaction)

4. **Confirm** (system)
   - Sends welcome email with org URL
   - Returns new org details to requestor

### Extensibility

- **Payment:** Insert action between Validate and Provision
- **Manual approval:** Insert approval action with system admin participant
- **KYC/identity verification:** Same pattern — add action steps
- Blueprint is versioned; upgrading the template upgrades the process

### Admin-Initiated Path

`POST /api/platform/organizations` bypasses the blueprint — system admins are already trusted. Creates org + sends email invite for admin role. The invited user authenticates (creating or reusing their `PlatformUser`), accepts the invite, and gets a `UserIdentity` in the new org with `Role = Administrator`.

---

## Bootstrap Changes

The bootstrap flow (`POST /api/tenants/bootstrap`) creates:

1. **System admin org** (existing, ID `...0001`)
   - `OrgType.Standard`, `IsPlatformOrg = true`
   - Status: Active

2. **Public org** (new, ID `...0002`)
   - `OrgType.Public`, `IsPlatformOrg = true`
   - `SelfRegistrationEnabled = false` (until enabled via UI)
   - Status: **Suspended** (disabled by default)

3. **Admin PlatformUser** (new — platform-level identity for the bootstrap admin)

4. **Admin UserIdentity** in system admin org (references PlatformUser)

5. **PlatformSettings** row with `PublicOrgEnabled = false`, `MaxOrgsPerUser = 1`

6. **Service principals** (existing behaviour)

7. **"Create Organisation" blueprint** seeded into public org

---

## UI Changes

### System Admin Org — Admin Panel

1. **Platform Settings page** (new)
   - Public org enable/disable toggle
   - Social login provider configuration (toggle + client ID/secret per provider, stored via IdentityProviderConfiguration)
   - Public org branding (logo, colors, tagline)
   - Max orgs per user setting

2. **Platform Organisations page** (new)
   - List all orgs (name, subdomain, status, user count, created date)
   - Actions: Disable/Enable org, View users (read-only for private orgs)
   - "Create Organisation" button → form with org name, subdomain, admin invitee email

3. **Public Org Users page** (new or extend existing)
   - Full user management for the public org

### Public Org — User-Facing UI

4. **Signup/Login page**
   - Social login buttons (only shows enabled providers from IdentityProviderConfiguration)
   - Email/password registration form
   - Hidden/404 when public org is disabled

5. **Org Switcher** (new, global nav)
   - Dropdown showing orgs the user belongs to
   - Queries by PlatformUserId across org schemas
   - Switching triggers new JWT scoped to selected org

6. **"Create Organisation" page** (new, public org)
   - Triggers the Create Organisation blueprint
   - Form: org name, subdomain, optional description
   - Shows workflow progress/status

---

## Migration Strategy

Since there are no production instances, this is a clean-slate change:

- Remove `PublicIdentity`, `SocialLoginLink` entities and tables
- Reparent `PasskeyCredential` from `PublicIdentity` to `PlatformUser` (replace polymorphic FK with direct `PlatformUserId` FK, remove `OrganizationId`, retain all other fields)
- Add `PlatformUser` entity and table (public schema)
- Add `PlatformSocialLogin` entity and table (public schema) — multiple providers per user
- Add `PlatformUserOrgMembership` entity and table (public schema) — denormalized org lookup for switcher
- Add `PlatformUserId` to `UserIdentity`, remove auth fields moved to PlatformUser
- Add `IsPlatformOrg` to `Organization`
- Change `Organization.IdentityProvider` (one-to-one) to `Organization.IdentityProviders` (one-to-many `ICollection<IdentityProviderConfiguration>`)
- Add `GitHub` to `IdentityProviderType` enum
- Add `PlatformSettings` entity and table
- Add `SocialLogin` and `AdminCreated` to `ProvisioningMethod` enum
- Update `DatabaseInitializer` to create both orgs + PlatformUser for bootstrap admin
- Reset initial EF migration (no production data to preserve)
- Remove `PublicAuthEndpoints`, `IPublicUserService`, `PublicUserService`
- Social login + passkey logic moves into `AuthEndpoints` resolving against `PlatformUser` / `PlatformSocialLogin`
- Add YARP API Gateway routes for `/api/platform/*` with SystemAdmin authorization policy

---

## Out of Scope (Future)

- Payment/billing integration for org creation
- Manual approval workflows for org creation
- KYC/identity verification steps
- Federation between Sorcha installations
- Custom domain per private org (exists in model but not wired up)
