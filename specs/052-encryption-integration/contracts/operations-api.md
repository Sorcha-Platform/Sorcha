# Operations API Contract

**Service**: Blueprint Service
**Base Path**: `/api/operations`

## Existing Endpoint (No Changes)

### GET /api/operations/{operationId}

Get the status of a single encryption operation.

**Parameters**:
- `operationId` (path, required): The operation identifier

**Authorization**: JWT with `wallet_address` claim matching the operation's submitting wallet. Service tokens bypass wallet check.

**Response 200**:
```json
{
  "operationId": "abc123def456",
  "status": "Encrypting",
  "blueprintId": "bp-001",
  "actionId": "1",
  "instanceId": "inst-001",
  "submittingWalletAddress": "did:sorcha:w:abc123",
  "totalRecipients": 5,
  "totalGroups": 3,
  "currentStep": 2,
  "totalSteps": 4,
  "stepName": "Encrypting payloads",
  "percentComplete": 30,
  "transactionHash": null,
  "error": null,
  "failedRecipient": null,
  "createdAt": "2026-03-06T12:00:00Z",
  "completedAt": null
}
```

**Response 403**: Wallet address doesn't match.
**Response 404**: Operation not found.

## New Endpoint

### GET /api/operations

List encryption operations for a wallet address, sourced from ActivityEvent records for completed operations and in-memory store for active operations.

**Query Parameters**:
- `wallet` (required): Wallet address to filter by
- `page` (optional, default 1): Page number (1-based)
- `pageSize` (optional, default 20, max 50): Items per page

**Authorization**: JWT with `wallet_address` claim matching the requested wallet.

**Response 200**:
```json
{
  "items": [
    {
      "operationId": "abc123def456",
      "status": "complete",
      "blueprintId": "bp-001",
      "actionTitle": "Submit Application",
      "instanceId": "inst-001",
      "walletAddress": "did:sorcha:w:abc123",
      "recipientCount": 5,
      "transactionHash": "a1b2c3d4e5f6...",
      "errorMessage": null,
      "createdAt": "2026-03-06T12:00:00Z",
      "completedAt": "2026-03-06T12:00:35Z"
    },
    {
      "operationId": "xyz789",
      "status": "failed",
      "blueprintId": "bp-002",
      "actionTitle": "Review Document",
      "instanceId": "inst-002",
      "walletAddress": "did:sorcha:w:abc123",
      "recipientCount": 3,
      "transactionHash": null,
      "errorMessage": "Failed to resolve public key for recipient did:sorcha:w:def456",
      "createdAt": "2026-03-06T11:30:00Z",
      "completedAt": "2026-03-06T11:30:12Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 2,
  "hasMore": false
}
```

**Response 403**: Wallet address doesn't match JWT claim.

---

# Action Execution Contract

**Service**: Blueprint Service
**Existing Endpoint**: `POST /api/instances/{instanceId}/actions/{actionId}/execute`

No changes to the endpoint. The response already includes `operationId` and `isAsync` fields.

**Async Response (HTTP 202)**:
```json
{
  "transactionId": "",
  "instanceId": "inst-001",
  "operationId": "abc123def456",
  "isAsync": true,
  "isComplete": false,
  "nextActions": [],
  "warnings": null
}
```

**Sync Response (HTTP 200)**:
```json
{
  "transactionId": "a1b2c3d4e5f6...",
  "instanceId": "inst-001",
  "operationId": null,
  "isAsync": false,
  "isComplete": false,
  "nextActions": [
    { "actionId": 2, "actionTitle": "Review", "participantId": "reviewer-01" }
  ],
  "warnings": null
}
```

---

# SignalR Events Contract

**Hub**: ActionsHub (`/actionshub`)
**Group**: `wallet:{walletAddress}`

These events are already implemented in the backend. UI clients need to subscribe to them.

### EncryptionProgress (server → client)
```json
{
  "operationId": "abc123def456",
  "step": 2,
  "stepName": "Encrypting payloads",
  "totalSteps": 4,
  "percentComplete": 30,
  "timestamp": "2026-03-06T12:00:15Z"
}
```

### EncryptionComplete (server → client)
```json
{
  "operationId": "abc123def456",
  "transactionHash": "a1b2c3d4e5f6...",
  "timestamp": "2026-03-06T12:00:35Z"
}
```

### EncryptionFailed (server → client)
```json
{
  "operationId": "abc123def456",
  "error": "Failed to resolve public key for recipient",
  "failedRecipient": "did:sorcha:w:def456",
  "step": 1,
  "timestamp": "2026-03-06T12:00:08Z"
}
```

---

# EventsHub Notification Contract (New Event Type)

**Hub**: EventsHub (`/hubs/events`)
**Group**: `user:{userId}`

### EncryptionOperationCompleted (server → client, new)

Sent to the user's personal event channel when an encryption operation completes (success or failure). Enables cross-page toast notifications.

```json
{
  "eventType": "encryption_operation_completed",
  "operationId": "abc123def456",
  "status": "complete",
  "transactionHash": "a1b2c3d4e5f6...",
  "errorMessage": null,
  "blueprintId": "bp-001",
  "actionTitle": "Submit Application",
  "timestamp": "2026-03-06T12:00:35Z"
}
```
