# Data Model: P2P Register Sync

**Feature**: 071-p2p-register-sync | **Date**: 2026-03-28

## New Entities

### ReverseStreamEntry (PeerRouter — in-memory)

Tracks an active bidirectional stream from a NAT'd peer to the Router.

| Field | Type | Description |
|-------|------|-------------|
| PeerId | string | Peer identifier (key) |
| ResponseStream | IServerStreamWriter | gRPC stream writer for pushing messages to peer |
| ConnectedAt | DateTimeOffset | When stream was established |
| LastActivityAt | DateTimeOffset | Last message sent or received |
| IsActive | bool | Whether stream is still open |

**Lifecycle**: Created when peer calls `Stream()` RPC. Removed when stream completes or errors. No persistence — lost on Router restart, peers reconnect automatically.

---

### DocketFinalizationRecord (Peer Service — in-memory tracking)

Tracks finalization progress for replicated dockets.

| Field | Type | Description |
|-------|------|-------------|
| RegisterId | string | Target register |
| DocketNumber | long | Docket sequence number |
| DocketHash | string | Docket hash for dedup |
| Status | FinalizationStatus | Pending / Finalized / Rejected |
| AttemptedAt | DateTimeOffset | Last finalization attempt |
| FinalizedAt | DateTimeOffset? | When successfully written to Register Service |
| ErrorMessage | string? | If rejected, reason |

**FinalizationStatus**: `Pending` (docket received, awaiting verification), `Finalized` (written to Register Service), `Rejected` (invalid signature or verification failure)

---

### ValidatorKeyCache (Peer Service — in-memory)

Caches the validator public key per register, extracted from genesis docket.

| Field | Type | Description |
|-------|------|-------------|
| RegisterId | string | Register identifier (key) |
| PublicKey | byte[] | Validator's public key bytes |
| Algorithm | string | Signing algorithm (ED25519, NISTP256, RSA4096) |
| ResolvedFrom | string | Source: "genesis-docket" |
| ResolvedAt | DateTimeOffset | When key was resolved |

---

## Modified Entities

### RoutingEntry (PeerRouter — existing)

**Change**: `AdvertisedRegisters` must be updated from heartbeats, not only from initial `RegisterPeer`.

| Field | Change | Description |
|-------|--------|-------------|
| AdvertisedRegisters | Updated on heartbeat | Currently only set during RegisterPeer; must also update when heartbeat includes new advertisements |

---

### RegisterSubscription (Peer Service — existing)

**No schema changes**. Existing fields support the full flow:
- `Mode` (FullReplica/ForwardOnly)
- `SyncState` (Subscribing/Syncing/FullyReplicated/Active/Error)
- `LastSyncedDocketVersion` / `LastSyncedTransactionVersion` — version cursors
- `SourcePeerIds` — tracks which peers are data sources

---

### CachedDocket (Peer Service RegisterCache — existing)

**No schema changes**. The existing `CachedDocket` in `RegisterCache` already stores docket data including transaction references. The finalization service reads from this cache.

---

## State Transitions

### Reverse Stream Lifecycle

```
Disconnected → Connecting → Connected → Active → Disconnected
                   ↑                                    │
                   └────────── (exponential backoff) ────┘
```

### Docket Finalization

```
Docket Received → Signature Verification → [Valid] → Write to Register Service → Finalized
                                         → [Invalid] → Rejected (logged)
                                         → [Write Failed] → Retry (backoff)
```

### Register Subscription (existing — no changes)

```
Subscribing → Syncing → FullyReplicated → Active (live streaming)
     ↑                                         │
     └─────────── Error (retry) ────────────────┘
```
