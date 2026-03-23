# Data Model: Register Subscriptions & Private Register Invitations

## Entities

### Organization (Extended)

**Table:** `Organizations` (existing)
**New fields:**

| Column | Type | Nullable | Default | Notes |
|--------|------|----------|---------|-------|
| `WalletAddress` | `varchar(128)` | YES | NULL | HD wallet address, provisioned async |
| `PublicKey` | `varchar(512)` | YES | NULL | ED25519 public signing key (base64) |
| `EncryptionPublicKey` | `varchar(512)` | YES | NULL | X25519 derived encryption key (base64) |
| `SigningAlgorithm` | `varchar(32)` | YES | NULL | Default: "ED25519" |

### OrganizationRegisterSubscription (New)

**Table:** `OrganizationRegisterSubscriptions`

| Column | Type | Nullable | Default | Notes |
|--------|------|----------|---------|-------|
| `Id` | `uuid` | NO | `gen_random_uuid()` | PK |
| `OrganizationId` | `uuid` | NO | — | FK → Organizations.Id |
| `RegisterId` | `varchar(32)` | NO | — | Validated: `^[a-f0-9]{32}$` |
| `RegisterName` | `varchar(38)` | YES | NULL | Cached display name |
| `SubscriptionType` | `varchar(16)` | NO | — | `Owner`, `Public`, `Invited` |
| `Status` | `varchar(16)` | NO | `Pending` | `Pending`, `Active`, `Suspended`, `Revoked` |
| `InvitationId` | `varchar(64)` | YES | NULL | Phase 2: links to invitation |
| `SubscribedAt` | `timestamptz` | NO | `now()` | — |
| `SubscribedByUserId` | `uuid` | NO | — | Admin who created |
| `RevokedAt` | `timestamptz` | YES | NULL | When revoked |
| `RevokedByUserId` | `uuid` | YES | NULL | Admin who revoked |

**Indexes:**
- Unique: `(OrganizationId, RegisterId)`
- Index: `OrganizationId` (for list queries)
- Index: `RegisterId` (for register-scoped queries)

**Constraints:**
- FK: `OrganizationId` → `Organizations.Id` (CASCADE DELETE)
- CHECK: `SubscriptionType IN ('Owner', 'Public', 'Invited')`
- CHECK: `Status IN ('Pending', 'Active', 'Suspended', 'Revoked')`

### InvitationNonce (Phase 2 — New)

**Table:** `InvitationNonces`

| Column | Type | Nullable | Default | Notes |
|--------|------|----------|---------|-------|
| `Id` | `uuid` | NO | `gen_random_uuid()` | PK |
| `Nonce` | `varchar(36)` | NO | — | UUID v4 from invitation token |
| `InvitationId` | `varchar(64)` | NO | — | Invitation ID |
| `SourceOrgId` | `uuid` | NO | — | Org that created invitation |
| `TargetOrgId` | `uuid` | NO | — | Org that accepted |
| `RegisterId` | `varchar(32)` | NO | — | Target register |
| `ConsumedAt` | `timestamptz` | NO | `now()` | When consumed |

**Indexes:**
- Unique: `Nonce` (fast duplicate check)

## Enums

### SubscriptionType
```csharp
public enum SubscriptionType
{
    Owner,    // Org created the register — cannot unsubscribe
    Public,   // Subscribed to a public register
    Invited   // Accepted a private invitation (Phase 2)
}
```

### SubscriptionStatus
```csharp
public enum SubscriptionStatus
{
    Pending,    // Tenant record created, peer subscription in progress
    Active,     // Peer subscription confirmed
    Suspended,  // Admin action or register offline
    Revoked     // Admin unsubscribed
}
```

## State Transitions

### Subscription Status
```
                  ┌──────────┐
                  │ Pending  │
                  └────┬─────┘
                       │ peer subscription confirmed
                       ▼
                  ┌──────────┐
              ┌───│  Active  │───┐
              │   └──────────┘   │
   admin action│                 │admin unsubscribe
              ▼                  ▼
        ┌───────────┐     ┌──────────┐
        │ Suspended │     │ Revoked  │
        └───────────┘     └──────────┘

  Pending → Revoked (peer subscription failed after max retries)
  Suspended → Active (admin reactivates)
```

## Relationships

```
Organization 1 ──── * OrganizationRegisterSubscription
                         │
                         │ (Phase 2: InvitationId)
                         │
                    InvitationNonce (nonce consumed on acceptance)
```
