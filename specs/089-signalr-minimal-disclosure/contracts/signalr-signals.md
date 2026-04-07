# SignalR Signal Contracts

## Hub: ActionsHub (/actionshub)

### Server → Client Methods

#### ActionAvailable
**Trigger:** Action routed to participant after previous action completion
**Target Group:** `wallet:{address}`
**Payload:** `SignalNotification`
```json
{
  "signalType": "action-available",
  "instanceId": "abc-123",
  "correlationId": "def-456",
  "timestamp": "2026-04-07T12:00:00Z"
}
```

#### ActionRejected
**Trigger:** Action rejected and routed back to previous participant
**Target Group:** `wallet:{address}`
**Payload:** `SignalNotification`
```json
{
  "signalType": "action-rejected",
  "instanceId": "abc-123",
  "correlationId": "def-456",
  "timestamp": "2026-04-07T12:00:00Z"
}
```

#### WorkflowCompleted
**Trigger:** No next actions after routing (terminal state)
**Target Group:** `wallet:{address}` (all participants in instance)
**Payload:** `SignalNotification`
```json
{
  "signalType": "workflow-completed",
  "instanceId": "abc-123",
  "correlationId": null,
  "timestamp": "2026-04-07T12:00:00Z"
}
```

#### EncryptionProgress
**Trigger:** Encryption pipeline step advancement
**Target Group:** `wallet:{senderAddress}`
**Payload:** `EncryptionSignal`
```json
{
  "operationId": "op-789",
  "percentComplete": 30,
  "status": "encrypting",
  "timestamp": "2026-04-07T12:00:00Z"
}
```

#### EncryptionComplete
**Trigger:** Encryption pipeline completes successfully
**Target Group:** `wallet:{senderAddress}`
**Payload:** `EncryptionSignal`
```json
{
  "operationId": "op-789",
  "percentComplete": 100,
  "status": "complete",
  "timestamp": "2026-04-07T12:00:00Z"
}
```

#### EncryptionFailed
**Trigger:** Encryption pipeline fails
**Target Group:** `wallet:{senderAddress}`
**Payload:** `EncryptionSignal`
```json
{
  "operationId": "op-789",
  "percentComplete": 30,
  "status": "failed",
  "timestamp": "2026-04-07T12:00:00Z"
}
```

### Client → Server Methods

#### SubscribeToWallet(walletAddress: string)
**Authorization:** User tokens: wallet ownership validated via Participant Service. Service tokens: `org_id` claim required (reject without).
**Effect:** Adds connection to `wallet:{walletAddress}` group.

#### UnsubscribeFromWallet(walletAddress: string)
**Effect:** Removes connection from `wallet:{walletAddress}` group.

---

## Hub: EventsHub (/hubs/events)

### Server → Client Methods

#### InboundActionReceived
**Trigger:** Wallet Service detects inbound action transaction via Redis pub/sub
**Target Group:** `user:{userId}`
**Payload:** `SignalNotification`
```json
{
  "signalType": "inbound-action",
  "instanceId": "abc-123",
  "correlationId": "def-456",
  "timestamp": "2026-04-07T12:00:00Z"
}
```
**Pull-back:** `GET /api/activity` (Tenant Service)

#### EncryptionOperationCompleted
**Trigger:** Encryption completes or fails
**Target Group:** `user:{userId}`
**Payload:** `EncryptionSignal`
```json
{
  "operationId": "op-789",
  "percentComplete": 100,
  "status": "complete",
  "timestamp": "2026-04-07T12:00:00Z"
}
```
**Pull-back:** `GET /api/operations/{operationId}`

---

## Removed Signals

| Signal | Was Sent To | Reason Removed |
|--------|-------------|----------------|
| Any signal to `instance:{id}` group | `instance:{instanceId}` | No subscribers, no authz, SIGINT risk |
| `RecipientEncryptionProgress` | `wallet:{senderAddress}` | Detail available via pull-back |
| `DigestNotificationReceived` | `user:{userId}` | Unchanged (raw JSON, out of scope) |

## Pull-Back Endpoints

| Signal | Client Calls | Authentication |
|--------|-------------|----------------|
| `action-available` | `GET /api/instances/{instanceId}` | JWT (wallet ownership or org membership) |
| `action-rejected` | `GET /api/instances/{instanceId}` | JWT (wallet ownership or org membership) |
| `workflow-completed` | `GET /api/instances/{instanceId}` | JWT (wallet ownership or org membership) |
| `inbound-action` | `GET /api/activity` | JWT (user scoped) |
| `encryption-progress` | `GET /api/operations/{operationId}` | JWT (operation owner) |
