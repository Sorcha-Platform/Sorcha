# Data Model: Auto-Register Participant & PlatformUser Provisioning

## Modified Entities

### LinkedWalletAddress (existing, Tenant Service)

Add field:

| Field | Type | Description |
|-------|------|-------------|
| VerificationMethod | string | How ownership was verified: "challenge-verify" (existing flow) or "self-created" (auto-link on wallet creation). Default: "challenge-verify". |

### PlatformUser (existing, Tenant Service)

No schema changes. Existing fields used:
- Email (unique, used for reuse check)
- PasswordHash (set by admin provisioning)
- EmailVerified (set to true when skipEmailVerification)
- Status (Active)

### UserIdentity (existing, Tenant Service)

No schema changes. Existing fields used:
- PlatformUserId (linked to PlatformUser)
- OrganizationId (target org)
- Roles (admin-specified role)
- ProvisionedVia (new value: "AdminProvisioned")

## New Models

### AdminProvisionUserRequest

Request body for `POST /api/platform/users`.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Email | string | Yes | User's email address (unique across platform) |
| DisplayName | string | Yes | User's display name |
| OrganizationId | guid | Yes | Target organisation to provision into |
| Role | string | Yes | Role to assign (Consumer, Member, Admin) |
| Password | string? | No | Optional password (hashed server-side, NIST policy enforced) |
| SkipEmailVerification | bool | No | If true, mark email as verified immediately. Default: false. |

### AdminProvisionUserResponse

| Field | Type | Description |
|-------|------|-------------|
| UserId | guid | Created/reused PlatformUser ID |
| UserIdentityId | guid | Created UserIdentity ID |
| Email | string | User's email |
| DisplayName | string | User's display name |
| OrganizationId | guid | Organisation provisioned into |
| OrganizationName | string | Organisation display name |
| Role | string | Assigned role |
| EmailVerified | bool | Whether email is marked verified |
| IsExistingPlatformUser | bool | Whether an existing PlatformUser was reused |

### AdminResetPasswordRequest

Request body for `PUT /api/platform/users/{id}/password`.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| NewPassword | string | Yes | New password (NIST policy enforced) |

### AutoLinkResult (internal, not API-exposed)

Returned by the auto-link service method.

| Field | Type | Description |
|-------|------|-------------|
| ParticipantCreated | bool | Whether a new participant was registered |
| WalletLinked | bool | Whether the wallet was linked |
| ParticipantId | guid? | Participant identity ID (if exists) |
| SkipReason | string? | Why auto-link was skipped (if applicable) |

## Entity Relationships

```
PlatformUser (1) ──── (N) UserIdentity (one per org)
     │                        │
     │                        └── Organization
     │
     └──── (N) PlatformUserOrgMembership
                    │
                    └── Organization

ParticipantIdentity (1) ──── (N) LinkedWalletAddress
     │
     └── UserIdentity (via UserId + OrganizationId)
```

## Validation Rules

### AdminProvisionUserRequest
- Email: valid format, max 256 chars
- DisplayName: non-empty, max 200 chars
- OrganizationId: must reference an existing org
- Role: must be a valid UserRole enum value
- Password (if provided): NIST policy (min 8 chars, breach check, no common patterns)

### AdminResetPasswordRequest
- NewPassword: NIST policy (same as above)

### Auto-Link
- Platform-wide wallet uniqueness: wallet address not already linked to another participant
- User must belong to at least one organisation
