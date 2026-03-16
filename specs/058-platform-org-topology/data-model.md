# Data Model: Platform Organisation Topology

**Feature**: 058-platform-org-topology
**Date**: 2026-03-16

---

## New Entities

### PlatformUser (public schema)

Platform-wide identity anchor. One per person across the entire installation.

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `Id` | Guid | PK | Primary key, referenced by UserIdentity.PlatformUserId |
| `Email` | string | Required, MaxLength(320), Unique | Platform-wide unique email |
| `DisplayName` | string | Required, MaxLength(256) | Default display name |
| `PasswordHash` | string? | MaxLength(500) | BCrypt hash; null for social-only users |
| `EmailVerified` | bool | Default: false | Whether email has been verified |
| `EmailVerifiedAt` | DateTimeOffset? | | When email was verified |
| `VerificationToken` | string? | MaxLength(100) | Email verification token |
| `VerificationTokenExpiresAt` | DateTimeOffset? | | Token expiry (24h) |
| `PasswordResetTokenHash` | string? | MaxLength(500) | SHA-256 hashed reset token |
| `PasswordResetTokenExpiresAt` | DateTimeOffset? | | Reset token expiry (1h) |
| `FailedLoginCount` | int | Default: 0 | Progressive lockout counter |
| `LockedUntil` | DateTimeOffset? | | Temporary lockout expiry |
| `LockedPermanently` | bool | Default: false | Permanent lockout (>25 failures) |
| `CreatedOrgsCount` | int | Default: 0 | Tracks private orgs created |
| `Status` | PlatformUserStatus | Required | Active, Suspended, Deleted |
| `CreatedAt` | DateTimeOffset | Required | Registration timestamp |
| `LastLoginAt` | DateTimeOffset? | | Last successful authentication |

**Navigations**:
- `SocialLogins` → `ICollection<PlatformSocialLogin>`
- `PasskeyCredentials` → `ICollection<PasskeyCredential>`
- `OrgMemberships` → `ICollection<PlatformUserOrgMembership>`

**Indexes**:
- Unique on `Email`
- On `Status` (for admin queries)

**Enum: PlatformUserStatus**:
- `Active` (0)
- `Suspended` (1)
- `Deleted` (2)

---

### PlatformSocialLogin (public schema)

Links a social provider to a PlatformUser. Multiple providers per user.

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `Id` | Guid | PK | Primary key |
| `PlatformUserId` | Guid | FK → PlatformUser, Required | Owner |
| `Provider` | string | Required, MaxLength(50) | "google", "github", "microsoft", "apple" |
| `Subject` | string | Required, MaxLength(256) | Provider's unique user ID |
| `Email` | string? | MaxLength(320) | Email from provider |
| `DisplayName` | string? | MaxLength(256) | Name from provider profile |
| `LinkedAt` | DateTimeOffset | Required | When the link was established |
| `LastUsedAt` | DateTimeOffset? | | Last sign-in using this link |

**Indexes**:
- Unique on `(Provider, Subject)` — platform-wide
- On `PlatformUserId`

---

### PlatformUserOrgMembership (public schema)

Denormalized lookup for org switcher. Avoids cross-schema queries.

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `Id` | Guid | PK | Primary key |
| `PlatformUserId` | Guid | FK → PlatformUser, Required | User |
| `OrganizationId` | Guid | FK → Organization, Required | Org |
| `Role` | string | Required, MaxLength(50) | Highest role (denormalized) |
| `JoinedAt` | DateTimeOffset | Required | When user joined |

**Indexes**:
- Unique on `(PlatformUserId, OrganizationId)`
- On `PlatformUserId` (for switcher queries)

---

### PlatformSettings (public schema, singleton)

Platform-level configuration flags.

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `Id` | Guid | PK | Primary key |
| `PublicOrgEnabled` | bool | Default: false | Controls public org availability |
| `MaxOrgsPerUser` | int | Default: 1, Range(1, 100) | Limit on private orgs per user |
| `UpdatedAt` | DateTimeOffset | Required | Last modification time |
| `UpdatedBy` | Guid | Required | System admin who last updated |

---

## Modified Entities

### Organization (public schema) — Add Field

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `IsPlatformOrg` | bool | Default: false | Marks system admin + public orgs as undeletable |

**Navigation Change**:
- `IdentityProvider` (single) → `IdentityProviders` (`ICollection<IdentityProviderConfiguration>`)

---

### IdentityProviderConfiguration (public schema) — Change Relationship

**Remove**: Unique constraint on `OrganizationId`
**Add**: Composite unique index on `(OrganizationId, ProviderPreset)` — one config per provider type per org

---

### UserIdentity (per-org schema) — Add/Remove Fields

**Add**:

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `PlatformUserId` | Guid | Required | FK to PlatformUser — cross-org identity anchor |

**Remove** (moved to PlatformUser):
- `PasswordHash`
- `ExternalIdpSubject`
- `EmailVerified`, `EmailVerifiedAt`
- `VerificationToken`, `VerificationTokenExpiresAt`
- `PasswordResetTokenHash`, `PasswordResetTokenExpiresAt`
- `FailedLoginCount`, `LockedUntil`, `LockedPermanently`

**Retain** (org-scoped):
- `Email` (denormalized copy from PlatformUser)
- `DisplayName` (can differ per org)
- `Roles`, `Status`, `ProvisionedVia`, `InvitedByUserId`
- `ProfileCompleted`, `CreatedAt`, `LastLoginAt`
- `OrganizationId`

---

### PasskeyCredential (public schema) — Reparent FK

**Remove**:
- `OwnerType` (polymorphic discriminator)
- `OwnerId` (polymorphic FK)
- `OrganizationId`

**Add**:

| Field | Type | Constraints | Purpose |
|-------|------|-------------|---------|
| `PlatformUserId` | Guid | FK → PlatformUser, Required | Direct FK replacing polymorphic |

**Retain all other fields**: `CredentialId`, `PublicKeyCose`, `SignatureCounter`, `DisplayName`, `DeviceType`, `AttestationType`, `AaGuid`, `Status`, `CreatedAt`, `LastUsedAt`, `DisabledAt`, `DisabledReason`

**Index changes**:
- Remove composite index on `(OwnerType, OwnerId)`
- Remove composite index on `(OwnerId, Status)`
- Remove index on `OrganizationId`
- Add index on `PlatformUserId`
- Add composite index on `(PlatformUserId, Status)`

---

## Enum Changes

### IdentityProviderType — Add Value
- Existing: MicrosoftEntra, Google, Okta, Apple, AmazonCognito, GenericOidc
- **Add**: `GitHub`

### ProvisioningMethod — Add Value
- Existing: Local, Oidc, Invitation, SocialLogin
- **Add**: `AdminCreated`

---

## Removed Entities

- `PublicIdentity` — replaced by PlatformUser
- `SocialLoginLink` — replaced by PlatformSocialLogin
- `OwnerTypes` constants class — no longer needed (polymorphic FK removed)

## Removed Services

- `IPublicUserService` / `PublicUserService` — replaced by PlatformUserService
- `PublicAuthEndpoints` — merged into AuthEndpoints with platform-aware flow

---

## Entity Relationship Diagram

```
PlatformUser (public)
├── 1:N → PlatformSocialLogin (multiple social providers)
├── 1:N → PasskeyCredential (multiple passkeys)
├── 1:N → PlatformUserOrgMembership (denormalized org lookup)
│          └── N:1 → Organization
└── 1:N → UserIdentity (per-org, via PlatformUserId)

Organization (public)
├── 0:1 → IsPlatformOrg flag
├── 1:N → IdentityProviderConfiguration (multiple IDPs)
└── 1:N → UserIdentity (per-org schema)

PlatformSettings (public, singleton)
└── standalone, no FK relationships

SystemConfiguration (public)
└── Key-value store, "BootstrapCompleted" flag
```

---

## Well-Known IDs

| Constant | Value | Purpose |
|----------|-------|---------|
| `SystemAdminOrgId` | `00000000-0000-0000-0000-000000000001` | System admin org (existing DefaultOrganizationId) |
| `PublicOrgId` | `00000000-0000-0000-0000-000000000002` | Public org (new) |
| `DefaultAdminUserId` | `00000000-0000-0001-0000-000000000001` | Bootstrap admin (existing) |

---

## State Transitions

### Organization.Status
```
Created → Active → Suspended → Active (re-enabled)
                 → Deleted (soft delete, 30-day retention)
```

### PlatformUser.Status
```
Created → Active → Suspended → Active (re-enabled)
                 → Deleted (soft delete)
```

Platform orgs (`IsPlatformOrg = true`) cannot transition to Suspended or Deleted.
