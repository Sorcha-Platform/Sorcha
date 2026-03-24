# Data Model: Blueprint Service Persistence & Validator Crash Recovery

**Feature**: 068-blueprint-persistence | **Date**: 2026-03-24

## Entity Changes

### 1. BlueprintDraftEntity (New — PostgreSQL)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string (GUID) | PK | Auto-generated |
| OwnerId | string | Required, indexed | JWT `sub` claim of creator |
| Name | string | Required, max 200 | Display name |
| Description | string? | Max 2000 | Optional description |
| Content | string (JSONB) | Required | Full blueprint JSON |
| OrganizationId | string? | Indexed | Org context for future scoping |
| Status | DraftStatus enum | Required, default Draft | Draft, Archived |
| CreatedAt | DateTimeOffset | Required | UTC |
| UpdatedAt | DateTimeOffset | Required | UTC |

**Future Extensibility**:

### 2. BlueprintDraftAccessEntity (New — PostgreSQL, schema only)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | Guid | PK | |
| DraftId | string | FK → BlueprintDraftEntity, indexed | |
| UserId | string | Required, indexed | Delegated user |
| AccessLevel | string | Required | "Read", "Write" |
| GrantedAt | DateTimeOffset | Required | |
| GrantedBy | string | Required | User who granted access |

**Note**: Table created in schema but no business logic implemented. Exists to prevent future schema migration for collaboration feature.

### 3. BlueprintTemplateEntity (New — PostgreSQL)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string | PK | Template identifier |
| Name | string | Required, max 200 | Display name |
| Description | string? | Max 2000 | |
| Category | string? | Indexed | Template category |
| Content | string (JSONB) | Required | Full template JSON |
| Version | int | Required, default 1 | For seed version comparison |
| Source | TemplateSource enum | Required | Seed, UserCreated |
| Published | bool | Required, default true | Visibility flag |
| UsageCount | int | Default 0 | Popularity tracking |
| CreatedAt | DateTimeOffset | Required | |
| UpdatedAt | DateTimeOffset | Required | |

### 4. ActionEntity (New — PostgreSQL)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| TransactionHash | string | PK | Transaction identifier |
| WalletAddress | string | Required, indexed | Sender wallet |
| RegisterAddress | string | Required, indexed | Target register |
| Content | string (JSONB) | Required | Full action details JSON |
| IdempotencyKey | string? | Unique index | Replay protection |
| IdempotencyExpiry | DateTimeOffset? | | TTL for key |
| CreatedAt | DateTimeOffset | Required | |

**File storage** (separate table):

### 5. FileMetadataEntity (New — PostgreSQL)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string | PK | File identifier |
| TransactionHash | string | FK → ActionEntity, indexed | |
| FileName | string | Required | |
| ContentType | string | Required | MIME type |
| Size | long | Required | Bytes |
| Content | byte[] | Required | File content |

### 6. InstanceEntity (New — PostgreSQL)

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | string | PK | Instance identifier |
| BlueprintId | string | Required, indexed | |
| BlueprintVersion | int | Required | Pinned at creation |
| RegisterId | string | Required, indexed | |
| State | InstanceState enum | Required, indexed | Active/Completed/etc. |
| CurrentActionIds | string (JSONB) | | List of pending action IDs |
| ParticipantWallets | string (JSONB) | | Dict mapping participant→wallet |
| FirstTransactionId | string? | | Chain root |
| LastTransactionId | string? | | Chain tip |
| CompletedActionCount | int | Default 0 | |
| AccumulatedData | string (JSONB) | | Cached execution state |
| ActiveBranches | string (JSONB) | | Parallel branch state |
| Metadata | string (JSONB) | | Key-value pairs |
| Version | int | Required | Optimistic concurrency |
| CreatedAt | DateTimeOffset | Required | |
| UpdatedAt | DateTimeOffset | Required | |
| CompletedAt | DateTimeOffset? | | |

## Redis Cache Structures

### Published Blueprint Cache

| Key Pattern | Value | TTL | Notes |
|-------------|-------|-----|-------|
| `bp:pub:{blueprintId}:v:{version}` | Serialized blueprint JSON | 15 min | Version-specific |
| `bp:pub:{blueprintId}:latest` | Version number | 5 min | Quick latest-version lookup |
| `bp:pub:register:{registerId}` | Set of blueprint IDs | 15 min | Register-scoped listing |

### Instance State Cache

| Key Pattern | Value | TTL | Notes |
|-------------|-------|-----|-------|
| `bp:inst:{instanceId}:state` | Serialized AccumulatedData JSON | 30 min | Hot execution state |
| `bp:inst:{instanceId}:meta` | Serialized instance metadata | 30 min | Fast metadata lookup |

## Indexes

### PostgreSQL Indexes

| Table | Index | Columns | Notes |
|-------|-------|---------|-------|
| BlueprintDrafts | IX_Drafts_OwnerId | OwnerId | Owner-scoped queries |
| BlueprintDrafts | IX_Drafts_OrgId | OrganizationId | Future org-scoped queries |
| BlueprintTemplates | IX_Templates_Category | Category | Category filtering |
| Actions | IX_Actions_Wallet_Register | WalletAddress, RegisterAddress | Action listing |
| Actions | UX_Actions_IdempotencyKey | IdempotencyKey | Unique, replay protection |
| Instances | IX_Instances_BlueprintId | BlueprintId | Blueprint-scoped queries |
| Instances | IX_Instances_RegisterId | RegisterId | Register-scoped queries |
| Instances | IX_Instances_State | State | State filtering |

## Enums

### DraftStatus
- `Draft` = 0 (default)
- `Archived` = 1

### TemplateSource
- `Seed` = 0 (loaded from JSON files)
- `UserCreated` = 1 (created at runtime)
