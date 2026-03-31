# API Contracts: 078 — Register Sync Status Lifecycle & UI Improvements

## Modified Endpoints

### Register Service — Internal Subscription Handler

**POST** `/api/internal/register-subscriptions`

Existing endpoint. Add status transition logic:
- On `action: "subscribe"` → set register status to `Checking`
- On sync state update → map to RegisterStatus and call `UpdateRegisterStatusAsync`

### Register Service — Disable Dev Mode (New)

**POST** `/api/registers/{registerId}/disable-dev-mode`

**Auth**: RequireAdministrator
**Description**: Irreversibly disables dev mode, enabling mandatory field-level encryption.

**Response**: 200 OK
```json
{
  "registerId": "string",
  "devMode": false,
  "message": "Dev mode disabled. Field-level encryption is now required for new transactions."
}
```

**Error**: 409 Conflict if dev mode already disabled.

### Peer Service — Subscribe (Modified)

**POST** `/api/registers/{registerId}/subscribe`

Existing endpoint. After creating subscription, immediately trigger sync (bypass timer wait).

### Peer Service — Report Status (New Internal)

**POST** `/api/internal/register-sync-status`

Called by Peer Service to Register Service when sync state changes.

**Request**:
```json
{
  "registerId": "string",
  "syncState": "Subscribing|Syncing|FullyReplicated|Active|Error",
  "peerConnectionActive": true
}
```

**Response**: 200 OK

## SignalR Events (No Changes)

All events already exist. UI handling changes only:
- `TransactionConfirmed` → prepend to table (currently shows notification box)
- `DocketSealed` → prepend to table (currently shows notification box)
- `RegisterStatusChanged` → update status badge (already works)
