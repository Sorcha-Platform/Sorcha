# API Contract Changes: Register Service

**Feature**: 067-register-security-hardening | **Date**: 2026-03-24

## Modified Endpoints

### POST /api/registers/initiate

**Change**: Remove `AllowAnonymous`, require `CanManageRegisters` (admin role + org_id).

**Request Body Changes**:
```diff
{
  "name": "string",
  "description": "string?",
- "tenantId": "string",
+ "purpose": "General|System",       // NEW, optional, defaults to "General"
  "owners": [...],
  "additionalAdmins": [...],
  "metadata": {},
  "advertise": false,
  "registerId": "string?",
  "devMode": false
}
```

**Authorization**: JWT required. `org_id` claim must be present. User must have Administrator or SystemAdmin role. Setting `purpose: "System"` requires SystemAdmin role in SystemAdmin org.

**Response**: Unchanged (200 with InitiateResponse).

---

### POST /api/registers/finalize

**Change**: Remove `AllowAnonymous`, require authentication.

**Request/Response**: Unchanged.

**Authorization**: JWT required. Same user/org context as initiate.

---

### GET /api/registers

**Change**: Remove `?tenantId=` query parameter. Add subscription-based filtering.

```diff
- GET /api/registers?tenantId={tenantId}
+ GET /api/registers
```

**Behaviour**: Returns only registers the caller's org (from JWT `org_id`) is subscribed to, plus all System-purpose registers. No query parameter needed — scope is derived from JWT.

**Response Changes**:
```diff
{
  "id": "string",
  "name": "string",
  "description": "string?",
  "status": "Online|Offline|...",
  "advertise": true,
  "isFullReplica": true,
- "tenantId": "string",
+ "purpose": "General|System",
  "height": 0,
  "createdAt": "datetime",
  "updatedAt": "datetime"
}
```

---

### DELETE /api/registers/{id}

**Change**: Remove `?tenantId=` query parameter. Authorization via control record attestations.

```diff
- DELETE /api/registers/{id}?tenantId={tenantId}
+ DELETE /api/registers/{id}
```

**Authorization**: JWT required. Caller's `wallet_address` claim must match an Owner or Admin attestation in the register's control record. System-purpose registers cannot be deleted (returns 403).

---

## New Authorization Policies

| Policy | Claims Required | Purpose |
|--------|----------------|---------|
| `CanManageRegisters` (modified) | `org_id` + Administrator/SystemAdmin role | Create/manage registers |
| `CanCreateSystemRegisters` (new) | SystemAdmin org + SystemAdmin role | Set purpose to System |

## SignalR Hub Changes

### RegisterHub

```diff
- SubscribeToTenant(string tenantId)
- UnsubscribeFromTenant(string tenantId)
+ SubscribeToRegister(string registerId)
+ UnsubscribeFromRegister(string registerId)
```

`SubscribeToRegister` verifies the caller's org has an active subscription to the register before adding to the `register:{registerId}` group.

## Event Routing Changes

```diff
- hubContext.Clients.Group($"tenant:{TenantId}").RegisterCreated(...)
+ hubContext.Clients.Group($"register:{RegisterId}").RegisterCreated(...)
```

## Service Client Contract

### ISubscriptionServiceClient (new, in Sorcha.ServiceClients)

```csharp
Task<List<string>> GetActiveRegisterIdsForOrgAsync(
    Guid orgId,
    CancellationToken cancellationToken = default);
```

Calls: `GET /api/organizations/{orgId}/register-subscriptions?status=Active`
Returns: List of RegisterId strings from active subscriptions.
Failure: Returns empty list (fail-closed), logs error.
