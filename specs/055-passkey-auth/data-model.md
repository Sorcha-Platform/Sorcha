# Data Model: Passkey Authentication

**Feature**: 055-passkey-auth
**Date**: 2026-03-10

## Entities

### PasskeyCredential (NEW — public schema)

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | GUID | PK | Unique credential record ID |
| CredentialId | byte[] | Required, Unique Index | FIDO2 credential identifier (from authenticator) |
| PublicKeyCose | byte[] | Required | COSE-encoded public key for signature verification |
| SignatureCounter | int | Required, Default 0 | Monotonic counter for cloned authenticator detection |
| OwnerType | string | Required (max 20) | Discriminator: "OrgUser" or "PublicIdentity" |
| OwnerId | GUID | Required, Index | FK to UserIdentity.Id or PublicIdentity.Id |
| OrganizationId | GUID? | Nullable, Index | Set for OrgUser type; null for PublicIdentity |
| DisplayName | string | Required (max 256) | User-assigned friendly name (e.g., "MacBook Pro", "YubiKey") |
| DeviceType | string? | Optional (max 100) | Authenticator type metadata |
| AttestationType | string | Required (max 50), Default "none" | Attestation conveyance type |
| AaGuid | GUID | Required | Authenticator Attestation GUID (identifies authenticator model) |
| Status | string | Required (max 20), Default "Active" | Active, Disabled, Revoked |
| CreatedAt | DateTimeOffset | Required | Registration timestamp |
| LastUsedAt | DateTimeOffset? | Nullable | Last successful authentication |
| DisabledAt | DateTimeOffset? | Nullable | When credential was disabled (cloned authenticator) |
| DisabledReason | string? | Nullable (max 500) | Reason for disabling |

**Indexes**:
- Unique on `CredentialId` (fast lookup during assertion)
- Composite on `OwnerType + OwnerId` (list user's credentials)
- Composite on `OwnerId + Status` (active credentials per user)
- On `OrganizationId` (org-scoped queries)

### SocialLoginLink (NEW — public schema)

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | GUID | PK | Unique link record ID |
| PublicIdentityId | GUID | Required, FK → PublicIdentity.Id | Owning public user |
| ProviderType | string | Required (max 50) | Provider name: "Google", "Microsoft", "GitHub", "Apple" |
| ExternalSubjectId | string | Required (max 256) | Provider's unique user identifier (sub claim) |
| LinkedEmail | string? | Optional (max 320) | Email from provider (informational) |
| DisplayName | string? | Optional (max 256) | Name from provider (informational) |
| CreatedAt | DateTimeOffset | Required | Link creation timestamp |
| LastUsedAt | DateTimeOffset? | Nullable | Last sign-in via this provider |

**Indexes**:
- Composite unique on `ProviderType + ExternalSubjectId` (prevent duplicate links)
- On `PublicIdentityId` (list user's social links)

### PublicIdentity (MODIFIED — public schema)

**Changes from existing model**:
- Remove: `PassKeyCredentialId`, `PublicKeyCose`, `SignatureCounter` (moved to PasskeyCredential)
- Keep: `Id`, `DeviceType`, `RegisteredAt`, `LastUsedAt`, `DisplayName`, `Email`, `Status`
- Add: `EmailVerified` (bool), `EmailVerifiedAt` (DateTimeOffset?)
- Add navigation: `ICollection<PasskeyCredential> PasskeyCredentials`
- Add navigation: `ICollection<SocialLoginLink> SocialLoginLinks`

## State Transitions

### PasskeyCredential.Status

```
Active ──┬──► Disabled (cloned authenticator detected)
         │      │
         │      └──► Active (user re-registers / admin re-enables)
         │
         └──► Revoked (user removes credential)
```

- **Active → Disabled**: Automatic when signature counter regression detected. Sets `DisabledAt` and `DisabledReason`.
- **Disabled → Active**: User re-registers the credential (creates new record, old stays Disabled).
- **Active → Revoked**: User explicitly removes the credential from settings.
- Revoked is terminal — credential record retained for audit, never reactivated.

### PublicIdentity.Status

```
Active ──► Suspended ──► Active
  │
  └──► Deleted
```

No changes to existing state machine.

## Relationships

```
PublicIdentity (1) ──── (N) PasskeyCredential   [OwnerType = "PublicIdentity"]
PublicIdentity (1) ──── (N) SocialLoginLink

UserIdentity   (1) ──── (N) PasskeyCredential   [OwnerType = "OrgUser"]
UserIdentity   (1) ──── (0..1) TotpConfiguration [existing]
```

PasskeyCredential uses a polymorphic owner pattern (OwnerType + OwnerId) rather than two separate FK columns, keeping the table unified for credential ID lookups during authentication.
