# Data Model: Organization Admin & Identity Management

**Branch**: `054-org-identity-admin` | **Date**: 2026-03-08

## Entity Changes

### Modified Entities

#### Organization (existing — `Models/Organization.cs`)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| Id | Guid | — | PK |
| Name | string | — | Display name |
| Subdomain | string | — | Unique, 3-50 chars |
| Status | OrganizationStatus | — | Active/Suspended/Deleted |
| CreatorIdentityId | Guid? | — | User who created org |
| CreatedAt | DateTimeOffset | — | |
| Branding | BrandingConfiguration? | — | Logo, colors |
| IdentityProvider | IdentityProviderConfiguration? | — | Nav property |
| **OrgType** | **OrgType** | **ADD** | Standard / Public |
| **SelfRegistrationEnabled** | **bool** | **ADD** | Default: true for Public, false for Standard |
| **AllowedEmailDomains** | **string[]** | **ADD** | Empty = no restrictions |
| **CustomDomain** | **string?** | **ADD** | e.g., "login.acmestores.com" |
| **CustomDomainStatus** | **CustomDomainStatus** | **ADD** | None/Pending/Verified/Failed |
| **AuditRetentionMonths** | **int** | **ADD** | Default: 12 |

```csharp
public enum OrgType { Standard, Public }
public enum CustomDomainStatus { None, Pending, Verified, Failed }
```

#### IdentityProviderConfiguration (existing — `Models/IdentityProviderConfiguration.cs`)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| Id | Guid | — | PK |
| OrganizationId | Guid | — | FK → Organization |
| ProviderType | IdentityProviderType | RENAME → ProviderPreset | |
| IssuerUrl | string | — | OIDC issuer |
| ClientId | string | — | OAuth2 client ID |
| ClientSecretEncrypted | byte[] | — | AES-256-GCM |
| Scopes | string[] | — | Required: "openid" |
| AuthorizationEndpoint | string? | — | Auto-discovered |
| TokenEndpoint | string? | — | Auto-discovered |
| MetadataUrl | string? | — | Discovery URL |
| CreatedAt | DateTimeOffset | — | |
| UpdatedAt | DateTimeOffset | — | |
| **IsEnabled** | **bool** | **ADD** | Activation toggle |
| **DisplayName** | **string?** | **ADD** | UI label for provider |
| **UserInfoEndpoint** | **string?** | **ADD** | Auto-discovered |
| **JwksUri** | **string?** | **ADD** | Auto-discovered |
| **DiscoveryDocumentJson** | **string?** | **ADD** | Cached raw JSON |
| **DiscoveryFetchedAt** | **DateTimeOffset?** | **ADD** | Cache timestamp |

Update `IdentityProviderType` enum:
```csharp
public enum IdentityProviderType
{
    MicrosoftEntra,   // was AzureEntra
    Google,           // NEW
    Okta,             // NEW
    Apple,            // NEW
    AmazonCognito,    // was AwsCognito
    GenericOidc       // unchanged
}
```

#### UserIdentity (existing — `Models/UserIdentity.cs`)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| Id | Guid | — | PK |
| OrganizationId | Guid | — | FK → Organization |
| ExternalIdpUserId | string? | RENAME → ExternalIdpSubject | Clarity: this is the `sub` claim |
| PasswordHash | string? | — | BCrypt |
| Email | string | — | Unique within org |
| DisplayName | string | — | |
| Roles | UserRole[] | — | Now 5 roles only |
| Status | IdentityStatus | — | Active/Suspended/Deleted |
| CreatedAt | DateTimeOffset | — | |
| LastLoginAt | DateTimeOffset? | — | |
| **EmailVerified** | **bool** | **ADD** | Default: false |
| **EmailVerifiedAt** | **DateTimeOffset?** | **ADD** | |
| **VerificationToken** | **string?** | **ADD** | 32-byte URL-safe base64 |
| **VerificationTokenExpiresAt** | **DateTimeOffset?** | **ADD** | 24h from generation |
| **ProvisionedVia** | **ProvisioningMethod** | **ADD** | Local/Oidc/Invitation |
| **InvitedByUserId** | **Guid?** | **ADD** | If provisioned via invite |
| **ProfileCompleted** | **bool** | **ADD** | False if missing email/name |
| **FailedLoginCount** | **int** | **ADD** | For progressive lockout tracking |
| **LockedUntil** | **DateTimeOffset?** | **ADD** | Null = not locked |
| **LockedPermanently** | **bool** | **ADD** | Requires admin unlock |

```csharp
public enum ProvisioningMethod { Local, Oidc, Invitation, SocialLogin }
```

Update `UserRole` enum (consolidation):
```csharp
public enum UserRole
{
    SystemAdmin = 0,
    Administrator = 1,
    Designer = 2,
    Auditor = 3,
    Member = 4
    // Removed: Developer (was 3), User (was 4), Consumer (was 5)
}
```

### New Entities

#### OrgInvitation

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| OrganizationId | Guid | FK → Organization |
| Email | string | Target email |
| AssignedRole | UserRole | Role on acceptance |
| Token | string | 32-byte URL-safe base64, unique |
| ExpiresAt | DateTimeOffset | Default: 7 days |
| Status | InvitationStatus | Pending/Accepted/Expired/Revoked |
| InvitedByUserId | Guid | FK → UserIdentity |
| AcceptedByUserId | Guid? | FK → UserIdentity (may differ from target email) |
| AcceptedAt | DateTimeOffset? | |
| RevokedAt | DateTimeOffset? | |
| CreatedAt | DateTimeOffset | |

```csharp
public enum InvitationStatus { Pending, Accepted, Expired, Revoked }
```

**Indexes**: Unique on `Token`; composite on `OrganizationId + Email + Status` (for lookup).

#### CustomDomainMapping

| Field | Type | Notes |
|-------|------|-------|
| Id | Guid | PK |
| OrganizationId | Guid | FK → Organization, unique |
| Domain | string | e.g., "login.acmestores.com", unique |
| Status | CustomDomainStatus | Pending/Verified/Failed |
| VerifiedAt | DateTimeOffset? | |
| LastCheckedAt | DateTimeOffset? | |
| CreatedAt | DateTimeOffset | |

**Indexes**: Unique on `Domain` (for lookup by custom domain).

### Existing Entity (Already Implemented — No Changes Needed)

#### AuditLogEntry (existing — `Models/AuditLogEntry.cs`)

Already has: Timestamp, EventType, IdentityId, OrganizationId, IpAddress, UserAgent, Success, Details (JSONB).

**Additional AuditEventTypes to add** (extend existing enum):
- `InvitationSent`
- `InvitationAccepted`
- `InvitationRevoked`
- `InvitationExpired`
- `DomainRestrictionUpdated`
- `CustomDomainConfigured`
- `CustomDomainVerified`
- `CustomDomainFailed`
- `EmailVerificationSent`
- `EmailVerified`
- `AccountLockedOut`
- `AccountUnlockedByAdmin`
- `SelfRegistration`
- `OidcFirstLogin`
- `ProfileCompleted`

## Relationships

```
Organization (1) ──── (0..1) IdentityProviderConfiguration
Organization (1) ──── (0..1) CustomDomainMapping
Organization (1) ──── (N) UserIdentity          [per-org schema]
Organization (1) ──── (N) OrgInvitation          [per-org schema]
Organization (1) ──── (N) AuditLogEntry          [per-org schema]
UserIdentity (1) ──── (N) OrgInvitation          [as inviter]
UserIdentity (1) ──── (0..1) TotpConfiguration   [existing]
```

## Schema Placement

| Entity | Schema | Rationale |
|--------|--------|-----------|
| Organization (modified) | `public` | Cross-tenant lookups (subdomain, custom domain) |
| IdentityProviderConfiguration (modified) | `public` | Discovered during pre-auth (before org context) |
| CustomDomainMapping (new) | `public` | Gateway lookups before authentication |
| UserIdentity (modified) | `org_{id}` | Tenant-isolated user data |
| OrgInvitation (new) | `org_{id}` | Tenant-isolated invitations |
| AuditLogEntry (existing, extended) | `org_{id}` | Tenant-isolated audit trail |

## EF Core Migration Plan

**Migration name**: `AddOrgIdentityManagement`

1. Alter `Organizations` table: add OrgType, SelfRegistrationEnabled, AllowedEmailDomains (JSON array), CustomDomain, CustomDomainStatus, AuditRetentionMonths
2. Alter `IdentityProviderConfigurations` table: add IsEnabled, DisplayName, UserInfoEndpoint, JwksUri, DiscoveryDocumentJson, DiscoveryFetchedAt; rename ProviderType→ProviderPreset values
3. Create `CustomDomainMappings` table (public schema)
4. Per-org schema: alter `UserIdentities` table — add EmailVerified, EmailVerifiedAt, VerificationToken, VerificationTokenExpiresAt, ProvisionedVia, InvitedByUserId, ProfileCompleted, FailedLoginCount, LockedUntil, LockedPermanently; rename ExternalIdpUserId→ExternalIdpSubject
5. Per-org schema: create `OrgInvitations` table
6. Data migration: map existing `Developer`/`User`/`Consumer` roles to `Member`
7. Extend `AuditEventType` enum with new event types
