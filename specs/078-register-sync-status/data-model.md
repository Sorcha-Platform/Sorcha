# Data Model: 078 — Register Sync Status Lifecycle & UI Improvements

## Entity Changes

### Register (existing — Sorcha.Register.Models)

No new fields. Existing fields used:

| Field | Type | Purpose |
|-------|------|---------|
| Status | RegisterStatus | Online/Offline/Checking/Recovery — now driven by sync state |
| SyncState | string? | "Subscribing"/"Syncing"/"Synced"/"Error" — peer sync state mirror |
| DevMode | bool | When true, encryption not enforced. One-way disable (true→false, never back) |

### RegisterSubscription (existing — Sorcha.Peer.Service)

No new fields. Existing fields:

| Field | Type | Purpose |
|-------|------|---------|
| SyncState | RegisterSyncState | Subscribing/Syncing/FullyReplicated/Active/Error |
| Mode | ReplicationMode | ForwardOnly/FullReplica |
| ConsecutiveFailures | int | Failure count for retry logic |

### New: Offline Debounce State (in-memory, Peer Service)

| Field | Type | Purpose |
|-------|------|---------|
| RegisterId | string | Register being monitored |
| DisconnectedAt | DateTimeOffset? | When disconnect detected, null if connected |
| DebounceTimer | CancellationTokenSource? | Cancelled if reconnected within 30s |

## State Machine: Register Sync Lifecycle

```
Subscribe ──→ Checking ──→ Recovery ──→ Online
                                          │
                                          ▼
                                       Offline
                                          │
                                          ▼
                                       Checking ──→ Recovery ──→ Online
```

### Transitions

| From | To | Trigger | Debounce |
|------|-----|---------|----------|
| (none) | Checking | Subscription created | No |
| Checking | Recovery | Source peer found, docket pull begins | No |
| Recovery | Online | Docket chain fully synced + live stream active | No |
| Online | Offline | All source peers unreachable | 30 seconds |
| Offline | Checking | Source peer heartbeat resumes | No |
| Checking | Recovery | Docket pull resumes | No |
| Any | Offline | Max retry attempts exhausted | No |

## SignalR Events (existing hub, existing events)

| Event | Payload | Trigger |
|-------|---------|---------|
| RegisterStatusChanged | registerId, status | Status enum change |
| RegisterSyncStateChanged | registerId, syncState | Peer sync state change |
| TransactionConfirmed | registerId, transactionId | Transaction sealed in docket |
| DocketSealed | registerId, docketId, hash | Docket sealed |
| RegisterHeightUpdated | registerId, newHeight | New docket changes height |
