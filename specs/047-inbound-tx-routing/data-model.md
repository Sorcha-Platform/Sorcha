# Data Model: Inbound Transaction Routing & User Notification

**Feature**: 047-inbound-tx-routing
**Date**: 2026-03-02

## Entities

### 1. LocalAddressIndex

**Owner**: Register Service
**Storage**: Redis (bit-array for bloom filter)
**Purpose**: Probabilistic index of all wallet addresses belonging to the local node. Supports fast membership testing with no false negatives.

| Field | Type | Description |
|-------|------|-------------|
| RegisterId | string | Register this index belongs to |
| BitArray | byte[] | Redis bit-array key (`register:bloom:{registerId}`) |
| HashFunctionCount | int | Number of hash functions (k), stored in `register:bloom:params:{registerId}` |
| BitArraySize | int | Size of bit array in bits (m) |
| AddressCount | int | Number of addresses inserted (n) |
| LastRebuiltAt | DateTimeOffset | Timestamp of last full rebuild |

**Operations**:
- `Add(address)` → SETBIT on k positions
- `MayContain(address)` → GETBIT on k positions, true only if ALL bits set
- `Rebuild()` → Clear bit-array, re-insert all addresses from Wallet Service
- `Remove(address)` → Requires full rebuild (bloom filters don't support deletion)

**Sizing Formula** (for <0.1% false positive rate):
- m = -(n * ln(p)) / (ln(2)^2) where n=address count, p=0.001
- k = (m/n) * ln(2)
- For 100,000 addresses: m ≈ 1,437,759 bits (175 KB), k ≈ 10

### 2. InboundActionEvent

**Owner**: Wallet Service (transient — processed immediately or queued for digest)
**Storage**: In-memory (real-time) or Redis sorted set (digest queue)
**Purpose**: Represents a detected action-type transaction destined for a local address.

| Field | Type | Description |
|-------|------|-------------|
| Id | Guid | Unique event identifier |
| WalletAddress | string | Recipient address that matched |
| WalletId | Guid | Wallet containing the matched address |
| UserId | string | Owner's user sub (from Wallet.Owner) |
| TenantId | string | Tenant identifier |
| BlueprintId | string | Blueprint ID from TransactionMetaData |
| InstanceId | string | Blueprint instance ID from TransactionMetaData |
| ActionId | uint | Action ID from TransactionMetaData |
| NextActionId | uint | Next action ID from TransactionMetaData |
| SenderAddress | string | Transaction sender address (if available) |
| TransactionId | string | 64-char hex SHA-256 hash (TxId) |
| RegisterId | string | Register the transaction belongs to |
| DocketNumber | long | Docket number containing this transaction |
| Timestamp | DateTimeOffset | When the event was detected |
| IsRecoveryEvent | bool | True if detected during recovery mode |

**Relationships**:
- References Wallet (via WalletId) — owned by Wallet Service
- References Transaction (via TransactionId) — stored in Register Service MongoDB
- References Blueprint metadata — from TransactionMetaData fields

### 3. NotificationPreference

**Owner**: Tenant Service (extends existing UserPreferences)
**Storage**: PostgreSQL (via EF Core, existing TenantDbContext)
**Purpose**: Per-user configuration for notification delivery method and frequency.

| Field | Type | Description |
|-------|------|-------------|
| NotificationMethod | NotificationMethod (enum) | Delivery channel |
| NotificationFrequency | NotificationFrequency (enum) | Delivery timing |

**Enum: NotificationMethod**:
| Value | Int | Description |
|-------|-----|-------------|
| InApp | 0 | In-app notifications only (default) |
| InAppPlusEmail | 1 | In-app + email summary |
| InAppPlusPush | 2 | In-app + browser push notification |

**Enum: NotificationFrequency**:
| Value | Int | Description |
|-------|-----|-------------|
| RealTime | 0 | Immediate per-transaction (default) |
| HourlyDigest | 1 | Batched hourly summary |
| DailyDigest | 2 | Batched daily summary |

**Integration**: Added as two new columns on existing `UserPreferences` table. New EF Core migration required. Defaults: `InApp` + `RealTime` (matching spec FR-011).

### 4. DigestQueue

**Owner**: Wallet Service
**Storage**: Redis sorted set (`wallet:digest:{userId}`)
**Purpose**: Accumulated InboundActionEvents awaiting digest delivery, grouped by user.

| Field | Type | Description |
|-------|------|-------------|
| UserId | string | User whose digest this belongs to |
| Events | List&lt;InboundActionEvent&gt; | Queued events (serialized as JSON in Redis) |
| Score | double | Unix timestamp of event (for sorted set ordering) |

**Operations**:
- `Enqueue(userId, event)` → ZADD to user's sorted set with timestamp score
- `DequeueAll(userId)` → ZRANGEBYSCORE + ZREMRANGEBYSCORE (atomic via Lua script)
- `Count(userId)` → ZCARD
- `Clear(userId)` → DEL key (when switching from digest to real-time)

**Key Pattern**: `wallet:digest:{userId}` — sorted set of serialized InboundActionEvent JSON

### 5. RecoveryState

**Owner**: Register Service
**Storage**: Redis hash (`register:recovery:{registerId}`)
**Purpose**: Per-register sync tracking during recovery mode.

| Field | Type | Description |
|-------|------|-------------|
| RegisterId | string | Register being recovered |
| Status | RecoveryStatus (enum) | Current recovery state |
| LocalLatestDocket | long | Last docket number stored locally |
| NetworkHeadDocket | long | Latest docket number on the network |
| StartedAt | DateTimeOffset | When recovery started |
| LastProgressAt | DateTimeOffset | When last docket was processed |
| DocketsProcessed | long | Count of dockets processed so far |
| ErrorCount | int | Number of errors during recovery |
| LastError | string? | Most recent error message |

**Enum: RecoveryStatus**:
| Value | Int | Description |
|-------|-----|-------------|
| Synced | 0 | Up to date with network |
| Recovering | 1 | Actively catching up |
| Stalled | 2 | Recovery paused due to errors |

**Operations**:
- `GetStatus(registerId)` → HGETALL
- `UpdateProgress(registerId, docketNumber)` → HMSET (atomic)
- `MarkSynced(registerId)` → HSET status=Synced
- `MarkRecovering(registerId, local, head)` → HMSET (initialize recovery)

## Entity Relationships

```
                    ┌─────────────────────┐
                    │   Wallet (existing)  │
                    │  Owner: string (sub) │
                    │  Tenant: string      │
                    └──────────┬──────────┘
                               │ 1:N
                    ┌──────────▼──────────┐
                    │ WalletAddress (exist)│
                    │  Address: string     │◄──── LocalAddressIndex
                    │  PublicKey: string   │      (bloom filter check)
                    └──────────┬──────────┘
                               │ match
                    ┌──────────▼──────────┐
                    │  InboundActionEvent  │──── Real-time → SignalR push
                    │  BlueprintId         │──── Digest → DigestQueue
                    │  ActionId            │
                    │  UserId              │
                    └─────────────────────┘
                               │
              ┌────────────────┼────────────────┐
              ▼                ▼                 ▼
    ┌─────────────────┐ ┌──────────────┐ ┌──────────────┐
    │NotificationPref │ │  DigestQueue  │ │RecoveryState │
    │(UserPreferences)│ │(Redis sorted) │ │ (Redis hash) │
    │ Method + Freq   │ │ Per-user      │ │ Per-register │
    └─────────────────┘ └──────────────┘ └──────────────┘
```

## State Transitions

### Recovery State Machine

```
    Node Start
        │
        ▼
  ┌─ Compare local docket vs network head ─┐
  │                                         │
  ▼ (gap detected)                    ▼ (no gap)
Recovering ──────────────────────▶ Synced
  │          (all dockets caught up)    ▲
  │                                     │
  ▼ (errors > threshold)               │
Stalled ─────────────────────────────┘
           (retry succeeds)
```

### Notification Flow

```
Transaction Stored (Register Service)
        │
        ▼
  Bloom Filter Check
        │
  ┌─────┴─────┐
  │ No match   │ Match found
  │ (discard)  │
  │            ▼
  │     gRPC → Wallet Service
  │            │
  │     Resolve address → wallet → user
  │            │
  │     Check NotificationPreference
  │            │
  │     ┌──────┴──────┐
  │     │ RealTime    │ Digest
  │     ▼             ▼
  │  SignalR push   Enqueue to
  │  (EventsHub)   DigestQueue
  │                    │
  │              Timer fires
  │                    │
  │              Batch deliver
  │              via SignalR
  └────────────────────┘
```
