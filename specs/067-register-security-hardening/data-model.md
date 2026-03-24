# Data Model: Register TenantId Removal & Security Hardening

**Feature**: 067-register-security-hardening | **Date**: 2026-03-24

## Entity Changes

### 1. RegisterPurpose (New Enum)

```
RegisterPurpose
├── General = 0    (default — user-created registers)
└── System = 1     (platform-internal registers)
```

- Stored as string in MongoDB (via `JsonStringEnumConverter`)
- Stored as integer in any EF Core context
- Extensible for future values without migration

### 2. Register (Modified)

| Field | Change | Notes |
|-------|--------|-------|
| `TenantId` | **REMOVE** | Was `[Required] string`, replace with subscription-based access |
| `Purpose` | **ADD** | `RegisterPurpose`, default `General` |

**Final Properties**:
- `Id` (string, 32-char hex, required)
- `Name` (string, 1-38 chars, required)
- `Description` (string?, 0-2048 chars)
- `Height` (uint)
- `Status` (RegisterStatus)
- `Advertise` (bool)
- `IsFullReplica` (bool, default true)
- `Purpose` (RegisterPurpose, default General) — **NEW**
- `CreatedAt` (DateTime)
- `UpdatedAt` (DateTime)
- `Votes` (string?)
- `DevMode` (bool)

### 3. RegisterControlRecord (Modified)

| Field | Change | Notes |
|-------|--------|-------|
| `TenantId` | **REMOVE** | Org ownership via attestations only |

Attestations remain the authoritative source for ownership. The creating org's identity is captured through the Owner attestation's Subject (DID).

### 4. InitiateRegisterCreationRequest (Modified)

| Field | Change | Notes |
|-------|--------|-------|
| `TenantId` | **REMOVE** | Org derived from JWT `org_id` claim |
| `Purpose` | **ADD** | `RegisterPurpose`, default `General` |

### 5. Domain Events (Modified)

`RegisterCreatedEvent`, `RegisterDeletedEvent`, `RegisterStatusChangedEvent`:

| Field | Change | Notes |
|-------|--------|-------|
| `TenantId` | **REMOVE** | Route by RegisterId instead |
| `Purpose` | **ADD** (CreatedEvent only) | For SignalR routing decisions |

### 6. RegisterHub (Modified)

| Method | Change | Notes |
|--------|--------|-------|
| `SubscribeToTenant(string tenantId)` | **REMOVE** | Was: add to `tenant:{tenantId}` group |
| `UnsubscribeFromTenant(string tenantId)` | **REMOVE** | Was: remove from `tenant:{tenantId}` group |
| `SubscribeToRegister(string registerId)` | **ADD** | Add to `register:{registerId}` group (with access check) |
| `UnsubscribeFromRegister(string registerId)` | **ADD** | Remove from `register:{registerId}` group |

## MongoDB Index Changes

| Index | Change |
|-------|--------|
| `Ascending(r => r.TenantId)` | **REMOVE** |
| `Ascending(r => r.Purpose)` | **ADD** (for System register queries) |

## New Service Client

### ISubscriptionServiceClient (in Sorcha.ServiceClients)

```
GetActiveRegisterIdsForOrgAsync(Guid orgId) → List<string>
```

- Calls Tenant Service to resolve active subscriptions
- Returns list of RegisterIds the org can access
- Used by Register Service to filter query results
- Fail-closed: returns empty list on error

## State Transitions

No new state machines. RegisterPurpose is immutable after creation (set once at register creation, cannot be changed).

## Validation Rules

| Rule | Enforcement |
|------|-------------|
| Purpose must be valid enum value | DataAnnotations on DTO |
| Only SystemAdmin can set Purpose=System | Authorization policy + business logic |
| Purpose is immutable after creation | RegisterManager rejects updates to Purpose field |
| System registers cannot be deleted | RegisterManager.DeleteRegisterAsync checks Purpose |
