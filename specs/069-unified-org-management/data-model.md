# Data Model: Unified Organisation Management UI

**Branch**: `069-unified-org-management` | **Date**: 2026-03-25

## Entity Changes

### UserResponse DTO (Enhanced)

Extends the existing `UserResponse` record returned by user list/detail endpoints.

| Field | Type | Source | New? | Notes |
|-------|------|--------|------|-------|
| Id | Guid | UserIdentity | No | |
| OrganizationId | Guid | UserIdentity | No | |
| Email | string | UserIdentity | No | |
| DisplayName | string | UserIdentity | No | |
| Roles | UserRole[] | UserIdentity | No | |
| Status | IdentityStatus | UserIdentity | No | Active/Suspended/Deleted |
| CreatedAt | DateTimeOffset | UserIdentity | No | |
| LastLoginAt | DateTimeOffset? | UserIdentity | No | |
| **EmailVerified** | bool | PlatformUser (join) | **Yes** | From PlatformUser.EmailVerified |
| **EmailVerifiedAt** | DateTimeOffset? | PlatformUser (join) | **Yes** | From PlatformUser.EmailVerifiedAt |
| **ProvisionedVia** | string | UserIdentity | **Yes** | Already on entity, not yet in DTO |
| **InvitedByUserId** | Guid? | UserIdentity | **Yes** | Already on entity, not yet in DTO |
| **ProfileCompleted** | bool | UserIdentity | **Yes** | Already on entity, not yet in DTO |
| **InvitationStatus** | string? | OrgInvitation (join) | **Yes** | Pending/Accepted/Expired/null |

### UserListRequest (Query Parameters)

New query parameters for `GET /api/organizations/{orgId}/users`:

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| includeInactive | bool | false | Existing parameter |
| **emailVerified** | bool? | null | null = no filter, true/false = filter by PlatformUser.EmailVerified |
| **provisionedVia** | string? | null | Filter by ProvisioningMethod (Local, Invitation, SocialLogin, etc.) |
| **includePendingInvitations** | bool | false | Include OrgInvitation records with Status=Pending (pre-UserIdentity) |

### PendingInvitationResponse (New)

Returned when `includePendingInvitations=true` for users who have been invited but not yet accepted (no UserIdentity exists).

| Field | Type | Source | Notes |
|-------|------|--------|-------|
| Email | string | OrgInvitation | Invitation recipient |
| AssignedRole | UserRole | OrgInvitation | Role upon acceptance |
| InvitationStatus | string | OrgInvitation | Pending/Expired |
| InvitedByUserId | Guid | OrgInvitation | Admin who sent invitation |
| ExpiresAt | DateTimeOffset | OrgInvitation | Invitation expiry |
| CreatedAt | DateTimeOffset | OrgInvitation | When invited |

### UserListResponse (Enhanced)

| Field | Type | New? | Notes |
|-------|------|------|-------|
| Users | IReadOnlyList\<UserResponse\> | No | Existing users in org |
| TotalCount | int | No | Total matching users |
| **PendingInvitations** | IReadOnlyList\<PendingInvitationResponse\> | **Yes** | Only populated when includePendingInvitations=true |
| **PendingInvitationCount** | int | **Yes** | Count of pending invitations |

### AuditEventType (New Value)

| Value | Notes |
|-------|-------|
| **EmailVerifiedByAdmin** | New audit event for admin email verification override |

## UI View Models

### ParticipantListItemViewModel (Enhanced)

| Field | Type | New? | Notes |
|-------|------|------|-------|
| Id | Guid | No | |
| DisplayName | string | No | |
| Email | string | No | |
| Status | string | No | Active/Suspended/Inactive |
| HasLinkedWallet | bool | No | |
| CreatedAt | DateTimeOffset | No | |
| **PublishStatus** | string? | **Yes** | None/Published/Revoked |
| **PublishedRegisterName** | string? | **Yes** | Register name if published |
| **PublishedAt** | DateTimeOffset? | **Yes** | When published |
| **PublishedVersion** | int? | **Yes** | Version number on register |

### OrganizationDashboardViewModel (Enhanced)

| Field | Type | New? | Notes |
|-------|------|------|-------|
| UserCount | int | No | |
| ParticipantCount | int | No | |
| PublishedParticipantCount | int | No | |
| ActiveUserCount | int | No | |
| ActiveParticipantCount | int | No | |
| **InvitedUserCount** | int | **Yes** | Pending invitations |
| **UnverifiedUserCount** | int | **Yes** | Active users with EmailVerified=false |

## State Transitions

### User Composite Status (Derived)

The UI presents a simplified composite status derived from multiple fields:

```
"Invited"     ← OrgInvitation.Status == Pending (no UserIdentity yet)
"Unverified"  ← UserIdentity.Status == Active AND PlatformUser.EmailVerified == false
"Active"      ← UserIdentity.Status == Active AND PlatformUser.EmailVerified == true
"Suspended"   ← UserIdentity.Status == Suspended
"Deleted"     ← UserIdentity.Status == Deleted
```

### Admin Override Transitions

```
Unverified → Active     (via POST .../verify-email)
Invited → [resend]      (via POST .../invitations/{id}/resend — existing)
Active ↔ Suspended      (via POST .../suspend and .../reactivate — existing)
```

## Relationships

```
Organisation (1) ──── (*) UserIdentity
    │                        │
    │                        │ PlatformUserId
    │                        ▼
    │                   PlatformUser (has EmailVerified)
    │
    ├──── (*) OrgInvitation (pending users not yet in UserIdentity)
    │
    └──── (*) Participant
                │
                │ published to
                ▼
          Register (published status: Draft/Published/Revoked)
```
