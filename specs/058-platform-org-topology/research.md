# Research: Platform Organisation Topology

**Feature**: 058-platform-org-topology
**Date**: 2026-03-16
**Status**: Complete

---

## Research Tasks & Findings

### R1: Identity Model Migration (PublicIdentity → PlatformUser)

**Decision**: Replace `PublicIdentity` + `SocialLoginLink` with `PlatformUser` + `PlatformSocialLogin` + `PlatformUserOrgMembership`.

**Rationale**: PublicIdentity was designed as a passkey-only public user. The new model needs cross-org identity anchoring, multiple social providers, email/password support, and org membership tracking. A clean replacement is simpler than evolving the existing entity — no production data to migrate.

**Alternatives Considered**:
- *Extend PublicIdentity*: Would require adding password fields, org tracking, and renaming — more confusing than a clean entity.
- *Use UserIdentity as platform anchor*: UserIdentity lives in per-org schemas, can't serve as cross-org anchor without cross-schema queries.

**Current State**:
- `PublicIdentity` (public schema): Id, DisplayName, Email, Status, EmailVerified, EmailVerifiedAt, DeviceType, RegisteredAt, LastUsedAt. Navigations: PasskeyCredentials, SocialLoginLinks.
- `SocialLoginLink` (public schema): Id, PublicIdentityId, ProviderType, ExternalSubjectId, LinkedEmail, DisplayName, CreatedAt, LastUsedAt. Unique index on (ProviderType, ExternalSubjectId).
- `PasskeyCredential` (public schema): Uses polymorphic OwnerType/OwnerId FK pattern (OwnerType = "OrgUser" or "PublicIdentity").
- `UserIdentity` (per-org schema): Contains authentication fields (PasswordHash, FailedLoginCount, LockedUntil, etc.) that should move to PlatformUser.

**Migration Path**:
1. Remove `PublicIdentity`, `SocialLoginLink`, `IPublicUserService`, `PublicUserService`, `PublicAuthEndpoints`
2. Create `PlatformUser`, `PlatformSocialLogin`, `PlatformUserOrgMembership` entities
3. Reparent `PasskeyCredential`: replace `OwnerType`/`OwnerId` with direct `PlatformUserId` FK, remove `OrganizationId`
4. Move auth fields from `UserIdentity` to `PlatformUser`, add `PlatformUserId` FK to `UserIdentity`
5. Reset initial EF migration (no production instances)

---

### R2: OAuth/OIDC Social Login Integration

**Decision**: Extend existing OIDC infrastructure with social login preset flows for public org.

**Rationale**: `OidcEndpoints.cs`, `OidcExchangeService`, and `OidcProvisioningService` already implement full OAuth2 authorization code + PKCE flow. Social login for public org follows the same pattern — just targets PlatformUser instead of UserIdentity.

**Alternatives Considered**:
- *Separate social login library (e.g., AspNet.Security.OAuth.Providers)*: Adds dependency; existing OIDC flow already handles Google, Microsoft, GitHub, Apple.
- *Build from scratch*: Wasteful — existing code is battle-tested.

**Current OIDC Flow**:
1. `POST /api/auth/oidc/initiate` — generates authorization URL with PKCE
2. `GET /api/auth/callback/{orgSubdomain}` — exchanges code for tokens, provisions/matches user
3. `OidcProvisioningService` handles user creation/matching

**Changes Needed**:
1. Social login endpoints route through PlatformUser resolution (find/create PlatformUser, then find/create UserIdentity in target org)
2. `IdentityProviderConfiguration` changes from one-to-one to one-to-many on Organization
3. Add `GitHub` to `IdentityProviderType` enum
4. Public org social login callback creates PlatformUser + PlatformSocialLogin + UserIdentity in public org

---

### R3: JWT Token Structure for Multi-Org

**Decision**: Extend existing JWT claims with `platform_user_id` claim. Keep `org_id` as the scoping claim.

**Rationale**: `TokenService.cs` already sets `org_id` in JWT claims (line 84). Adding `platform_user_id` enables org switching without re-authentication. The existing `org_id` claim continues to scope all authorization decisions.

**Current Claims** (from TokenService.GenerateUserTokenAsync):
- `sub` — User ID (UserIdentity.Id)
- `email` — User email
- `jti` — Token ID
- `name` — Display name
- `org_id` — Organization ID
- `org_name` — Organization name
- `token_type` — "user"
- Role claims from user.Roles[]

**Changes**:
- Add `platform_user_id` claim for org switching
- New endpoint: `POST /api/auth/switch-org` — takes target org_id, validates PlatformUserOrgMembership, issues new JWT scoped to target org
- Merge `GeneratePublicUserTokenAsync` into `GenerateUserTokenAsync` (all users are PlatformUsers now)

---

### R4: Authorization Policies for Platform Operations

**Decision**: Add new authorization policies in `AuthorizationPolicyExtensions.cs` for platform-level operations.

**Rationale**: Existing policies (`RequireAdministrator`, `RequireSystemAdmin`, `RequireAuthenticated`) handle org-scoped auth. Platform operations need `org_id == SystemAdminOrgId` check.

**Current Policies** (from ServiceDefaults/AuthorizationPolicyExtensions.cs):
- RequireAuthenticated, RequireService, RequireOrganizationMember
- RequireDelegatedAuthority, RequireAdministrator
- CanWriteDockets, RequireAuditor, RequireDesigner
- RequirePublicUser, CanCreateBlockchain, CanPublishBlueprint
- RequireSystemAdmin

**Changes**:
- `RequireSystemAdmin` already exists — verify it checks `org_id == SystemAdminOrgId`
- Add `RequirePlatformAuditor` — SystemAdmin org member with Auditor+ role
- Add YARP routes for `/api/platform/*` with appropriate policies

---

### R5: Blueprint Seeding for "Create Organisation"

**Decision**: Seed "Create Organisation" blueprint into public org during bootstrap.

**Rationale**: `SystemRegisterBootstrapper` already seeds blueprints. The Create Organisation blueprint follows existing JSON template patterns. Seeded during bootstrap after public org is created.

**Current Blueprint Seeding**: `SystemRegisterBootstrapper.SeedBlueprintsIfMissingAsync()` in Register Service seeds the "register-creation-v1" template.

**Changes**:
- Create `create-organisation-v1.json` blueprint template
- Seed into public org's register during bootstrap
- Participants: requestor (public org user), system (automated)
- Actions: Submit Request → Validate → Provision → Confirm

---

### R6: Per-Org Schema Management (EF Core)

**Decision**: Reuse existing per-org schema creation pattern in Tenant Service.

**Rationale**: The codebase already creates per-org PostgreSQL schemas via EF Core migrations. New org provisioning follows the same pattern.

**Key Concern**: Atomic provisioning — if schema creation succeeds but UserIdentity creation fails, the schema must be rolled back. Use database transactions with `TransactionScope` or manual cleanup.

---

### R7: IdentityProviderConfiguration One-to-Many

**Decision**: Change `Organization.IdentityProvider` (one-to-one) to `Organization.IdentityProviders` (one-to-many `ICollection<IdentityProviderConfiguration>`).

**Rationale**: Public org needs multiple social providers simultaneously (Google + GitHub + Microsoft + Apple). Private orgs may also benefit from multiple IDP support.

**Current Schema**: `IdentityProviderConfiguration` has `OrganizationId` (unique per organization, enforcing one-to-one). Navigation: `Organization.IdentityProvider`.

**Changes**:
- Remove unique constraint on `OrganizationId` in `IdentityProviderConfiguration`
- Change navigation to `Organization.IdentityProviders` (`ICollection<IdentityProviderConfiguration>`)
- Add composite unique index on `(OrganizationId, ProviderPreset)` — one config per provider type per org
- Update all code referencing `Organization.IdentityProvider` to handle collection

---

### R8: YARP Route Additions

**Decision**: Add 6 new YARP routes for platform management API.

**New Routes**:
| Route ID | Path | Policy | Cluster |
|----------|------|--------|---------|
| platform-settings-route | /api/platform/settings | RequireSystemAdmin | tenant-cluster |
| platform-public-org-route | /api/platform/settings/public-org | RequireSystemAdmin | tenant-cluster |
| platform-orgs-route | /api/platform/organizations | RequireSystemAdmin | tenant-cluster |
| platform-org-status-route | /api/platform/organizations/{id}/status | RequireSystemAdmin | tenant-cluster |
| platform-org-users-route | /api/platform/organizations/{id}/users | RequirePlatformAuditor | tenant-cluster |
| auth-switch-org-route | /api/auth/switch-org | RequireAuthenticated | tenant-cluster |

---

### R9: Email Verification & Delivery

**Decision**: Reuse existing email verification pattern from UserIdentity, applying it at PlatformUser level.

**Rationale**: UserIdentity already has `VerificationToken`, `VerificationTokenExpiresAt`, `EmailVerified`, `EmailVerifiedAt` fields. These move to PlatformUser. The verification flow remains the same — only the entity changes.

**Assumption**: Email delivery infrastructure exists (from spec assumptions). This feature does not implement the email provider — it generates tokens and endpoints.

---

### R10: `AdminCreated` ProvisioningMethod

**Decision**: Add `AdminCreated` value to existing `ProvisioningMethod` enum.

**Current Values**: Local, Oidc, Invitation, SocialLogin

**Change**: Add `AdminCreated = 4` — used when system admin creates a user directly via platform management API.

**Note**: `SocialLogin` already exists in the enum (value 3). Only `AdminCreated` needs to be added.
