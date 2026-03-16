# Relay-Aware Peer Communication

**Date:** 2026-03-16
**Status:** Approved
**Scope:** Peer Service relay fallback for NAT'd peers

---

## Problem

Peers behind NAT can register with seed nodes and heartbeat, but cannot communicate with each other. Direct connections fail because `peer.Address` is empty and NAT blocks incoming connections. The PeerRouter relay (`RouterCommunicationService.SendMessage`) is enabled on n0.sorcha.dev but the Peer Service never uses it.

Three communication paths break for NAT'd peers:

| Path | Current Transport | Failure Mode |
|------|-------------------|-------------|
| Messaging (`PeerCommunication.SendMessage`) | Direct gRPC to `peer.Address:Port` | Address empty, connection refused |
| Tx Distribution (`TransactionDistribution.NotifyTransaction`) | Direct gRPC to `peer.Address:Port` | Address empty, connection refused |
| Register Replication (`RegisterSync.PullDocketChain`, `SubscribeToRegister`) | Server streaming via `PeerConnectionPool` channels | Channel null, peer skipped |

## Solution

Add a relay fallback layer to the Peer Service that routes messages through the first available seed node when direct peer connections fail. All communication piggybacks on `PeerCommunication.SendMessage` via new `MessageType` values — zero PeerRouter changes.

### Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Scope | Unary relay + register sync via message types | Streaming relay deferred; unary sufficient for test network |
| NAT detection | Address-based (`peer.Address` is empty) | Ground truth signal, no new proto fields |
| Router coupling | None — relay targets seed node's `PeerCommunication.SendMessage` | PeerRouter is a shim; full node serves same RPC |
| Register sync transport | New `MessageType` values on `PeerMessage` | Zero router changes, router stays dumb relay |
| Sync freshness | Notification-triggered + periodic poll backstop | Near-real-time with safety net |
| Seed node selection | First connected healthy seed | One seed today, simple selection |

### Trade-offs Accepted

- Register sync via unary relay is chattier than native streaming — acceptable for small test network
- Poll backstop adds background traffic — mitigated by 60s interval
- All relayed traffic funnels through one seed — bottleneck risk at scale, fine for now
- PeerRouter is a temporary shim — design ensures relay works identically when n0 becomes a full peer node

---

## 1. Proto Changes

Add four new `MessageType` enum values to `peer_communication.proto`. No new RPCs, no new services.

```protobuf
enum MessageType {
  UNKNOWN = 0;
  TRANSACTION_NOTIFICATION = 1;
  TRANSACTION_REQUEST = 2;
  TRANSACTION_RESPONSE = 3;
  PEER_STATUS_UPDATE = 4;
  HEARTBEAT = 5;
  BLS_KEY_SHARE_DISTRIBUTION = 6;
  BLS_PARTIAL_SIGNATURE = 7;
  REGISTER_SYNC_REQUEST = 8;
  REGISTER_SYNC_RESPONSE = 9;
  TRANSACTION_DATA_REQUEST = 10;
  TRANSACTION_DATA_RESPONSE = 11;
}
```

### Payload POCOs

Serialized as JSON into `PeerMessage.payload` bytes field. All payloads include a `CorrelationId` (GUID string) for request/response matching.

```csharp
public class RegisterSyncRequest
{
    public required string CorrelationId { get; init; }
    public required string RegisterId { get; init; }
    public long FromDocketVersion { get; init; }
    public int MaxDockets { get; init; } = 50;
}

public class RegisterSyncResponse
{
    public required string CorrelationId { get; init; }
    public required string RegisterId { get; init; }
    public required List<DocketEntry> Dockets { get; init; }
    public bool HasMore { get; init; }
}

public class DocketEntry
{
    public long Version { get; init; }
    public required byte[] Data { get; init; }
    public required string DocketHash { get; init; }
    public required string PreviousHash { get; init; }
    public required List<string> TransactionIds { get; init; }
    public long CreatedAt { get; init; }
}

public class TransactionDataRequest
{
    public required string CorrelationId { get; init; }
    public required string RegisterId { get; init; }
    public required List<string> TransactionIds { get; init; }
}

public class TransactionDataResponse
{
    public required string CorrelationId { get; init; }
    public required string RegisterId { get; init; }
    public required List<TransactionEntry> Transactions { get; init; }
}

public class TransactionEntry
{
    public required string TransactionId { get; init; }
    public required byte[] Data { get; init; }
    public required string Checksum { get; init; }
    public long CreatedAt { get; init; }
}
```

---

## 2. RelayCommunicationService

Core relay primitive. Sends `PeerMessage` through the first healthy seed node's `PeerCommunication.SendMessage` channel.

**File:** `src/Services/Sorcha.Peer.Service/Communication/RelayCommunicationService.cs`

### Interface

```csharp
public class RelayCommunicationService
{
    // Fire-and-forget relay — sends a PeerMessage through seed node
    Task<bool> SendViaRelayAsync(
        string recipientPeerId,
        MessageType messageType,
        object payload,
        CancellationToken ct);

    // Request/response relay — sends request, waits for correlated response
    Task<TResponse?> SendAndWaitAsync<TResponse>(
        string recipientPeerId,
        MessageType requestType,
        object request,
        MessageType responseType,
        TimeSpan timeout,
        CancellationToken ct) where TResponse : class;

    // Called by RelayMessageHandler when a response message arrives
    void CompleteCorrelation(string correlationId, PeerMessage response);
}
```

### Dependencies

- `PeerConnectionPool` — get seed node channel
- `PeerListManager` — identify seed nodes
- `PeerServiceConfiguration` — local node ID

### Correlation Mechanism

- Maintains `ConcurrentDictionary<string, TaskCompletionSource<PeerMessage>>` keyed by correlation ID (GUID)
- `SendAndWaitAsync` generates a correlation ID, adds a TCS, sends the request, awaits the TCS with timeout
- On timeout, TCS is removed and `null` returned
- `CompleteCorrelation` is called by `RelayMessageHandler` when a response arrives — matches correlation ID and completes the TCS

### SenderPeerId Population

`RelayCommunicationService` MUST populate `PeerMessage.SenderPeerId` with the local node ID (`PeerServiceConfiguration.NodeId ?? Environment.MachineName`) when constructing relay messages. The PeerRouter's `RouterCommunicationService` validates that `SenderPeerId` is not empty and rejects messages without it. Note: the existing `CommunicationProtocolManager` sets `SenderPeerId` to empty string — this works for direct sends but would fail through relay.

### Seed Node Selection

- Queries `PeerConnectionPool.GetAllActiveChannels()`
- Filters to channels where `PeerListManager.GetPeer(peerId)?.IsSeedNode == true`
- Uses first available
- Returns `false` / `null` if no seed node connected

---

## 3. Relay Fallback in Existing Services

Same pattern in each: if `peer.Address` is empty, use `RelayCommunicationService` instead of direct connection.

### 3a. CommunicationProtocolManager

**Change in `SendMessageAsync`:** Add relay check before the protocol fallback chain.

```
if string.IsNullOrEmpty(peer.Address):
    return await _relayCommunication.SendViaRelayAsync(
        peer.PeerId, MessageType.TransactionNotification, message, ct)
else:
    existing GrpcStream → Grpc → REST chain
```

New constructor dependency: `RelayCommunicationService`.

### 3b. TransactionDistributionService

**Change in `SendToPeerAsync`:** Same address check.

```
if string.IsNullOrEmpty(peer.Address):
    return await _relayCommunication.SendViaRelayAsync(
        peer.PeerId, MessageType.TransactionNotification, txNotification, ct)
else:
    existing direct gRPC send
```

New constructor dependency: `RelayCommunicationService`.

### 3c. RegisterReplicationService

**Change in `PullFullReplicaAsync`:** When `GetChannel` returns null AND `peer.Address` is empty, use relay-based batch sync.

```
channel = _connectionPool.GetChannel(sourcePeer.PeerId)
if channel != null:
    existing streaming sync (unchanged)
else if string.IsNullOrEmpty(sourcePeer.Address):
    relay batch sync loop:
        response = SendAndWaitAsync<RegisterSyncResponse>(
            peerId, REGISTER_SYNC_REQUEST,
            { CorrelationId, RegisterId, FromVersion, MaxDockets=50 },
            REGISTER_SYNC_RESPONSE, timeout=30s)

        process dockets into RegisterCache (same logic as streaming path)

        for each docket with transactions:
            txResponse = SendAndWaitAsync<TransactionDataResponse>(
                peerId, TRANSACTION_DATA_REQUEST,
                { CorrelationId, RegisterId, TransactionIds },
                TRANSACTION_DATA_RESPONSE, timeout=30s)

            process transactions into RegisterCache

        if !response.HasMore: break
else:
    skip peer (no channel, no relay path)
```

New constructor dependency: `RelayCommunicationService`.

---

## 4. PeerCommunicationServiceImpl & RelayMessageHandler

### PeerCommunicationServiceImpl (NEW — does not exist yet)

The Peer Service currently has no server-side implementation of `PeerCommunication.SendMessage`. Only the PeerRouter implements this as `RouterCommunicationService`. For relay to work, peers must be able to *receive* relayed messages, so we need a new gRPC service implementation.

**File:** `src/Services/Sorcha.Peer.Service/GrpcServices/PeerCommunicationServiceImpl.cs` (NEW)

This service implements `PeerCommunication.PeerCommunicationBase` and handles incoming `SendMessage` calls — both direct and relayed. It dispatches to `RelayMessageHandler` for the new message types.

Must be registered in `Program.cs` with `app.MapGrpcService<PeerCommunicationServiceImpl>()`.

```csharp
public override async Task<MessageAck> SendMessage(PeerMessage request, ServerCallContext context)
{
    switch (request.MessageType)
    {
        case MessageType.RegisterSyncRequest:
        case MessageType.RegisterSyncResponse:
        case MessageType.TransactionDataRequest:
        case MessageType.TransactionDataResponse:
            await _relayMessageHandler.HandleAsync(request, context.CancellationToken);
            return new MessageAck { Received = true, ... };

        case MessageType.TransactionNotification:
            // Process notification + trigger relay sync if subscribed
            await _relayMessageHandler.HandleTransactionNotificationAsync(request, context.CancellationToken);
            return new MessageAck { Received = true, ... };

        default:
            return new MessageAck { Received = true, ... };
    }
}
```

**Note on dual delivery paths:** `TRANSACTION_NOTIFICATION` can arrive via two routes — directly through the `TransactionDistribution.NotifyTransaction` RPC (existing path) or relayed as a `PeerMessage` (new path). `RelayMessageHandler` must unwrap the relayed payload and feed it into the same processing logic that the `TransactionDistribution` handler uses, to avoid duplicate code paths.

### RelayMessageHandler

Handles the actual message processing — dispatched to by `PeerCommunicationServiceImpl`.

**File:** `src/Services/Sorcha.Peer.Service/Communication/RelayMessageHandler.cs`

### Responsibilities

**Request handling (this peer serves data):**
- `REGISTER_SYNC_REQUEST` — read dockets from local `RegisterCache` / store, build `RegisterSyncResponse`, send back via relay. Handler must enforce response size limits (cap dockets returned if serialized size would exceed 3MB, leaving headroom under the 4MB protobuf limit).
- `TRANSACTION_DATA_REQUEST` — read transactions from local store, build `TransactionDataResponse`, send back via relay

**Response handling (completing pending correlations):**
- `REGISTER_SYNC_RESPONSE` — extract correlation ID, call `RelayCommunicationService.CompleteCorrelation`
- `TRANSACTION_DATA_RESPONSE` — extract correlation ID, call `RelayCommunicationService.CompleteCorrelation`

**Notification trigger:**
- `TRANSACTION_NOTIFICATION` — if we have an active subscription for the register (checked via `RegisterSyncBackgroundService`), kick off a `REGISTER_SYNC_REQUEST` to pull latest data

### Dependencies

- `RelayCommunicationService` — send responses back via relay, complete correlations
- `RegisterCache` — read local register data
- `RegisterSyncBackgroundService` — check active subscriptions for notification trigger (this service manages subscription state via its `GetSubscription` method)

---

## 5. Periodic Sync Backstop

Catches missed notifications and handles startup catch-up.

**Integration point:** Extend `RegisterSyncBackgroundService` (existing background service in `Replication/RegisterSyncBackgroundService.cs` that already manages register sync loops and subscription state) with a new relay poll timer.

### Configuration

Add to `RegisterSyncConfiguration`:

```csharp
public int RelayPollIntervalSeconds { get; init; } = 60;
```

### Logic (runs every `RelayPollIntervalSeconds`)

```
for each active RegisterSubscription:
    peers = PeerListManager.GetPeersForRegister(registerId)
    for each peer where string.IsNullOrEmpty(peer.Address):
        response = SendAndWaitAsync<RegisterSyncResponse>(
            peer, REGISTER_SYNC_REQUEST,
            { RegisterId, FromVersion=subscription.LastSyncedDocketVersion, MaxDockets=50 },
            REGISTER_SYNC_RESPONSE, timeout=30s)

        if response != null:
            process dockets + pull transactions
            update subscription state
            break  // success, don't query more peers for this register

        // else try next peer
```

### Guards

- Skips if sync already in progress for that register — per-register `SemaphoreSlim` stored in a `ConcurrentDictionary<string, SemaphoreSlim>` on `RegisterSyncBackgroundService`, shared between the periodic poll and the notification-triggered sync path
- Only targets NAT'd peers (empty address) — direct peers use existing streaming
- Stops after first successful peer per register

---

## 6. Error Handling & Resilience

### Relay unavailable (no seed node connected)

- `SendViaRelayAsync` returns `false`
- `SendAndWaitAsync` returns `null`
- Callers treat as "peer unavailable", try next peer or wait for next poll cycle
- Existing `PeerConnectionPool.ReconnectDisconnectedSeedNodesAsync` handles reconnection

### Correlation timeout

- `SendAndWaitAsync` uses `CancellationTokenSource` with specified timeout
- On timeout, TCS removed from pending dictionary, `null` returned
- Caller moves to next source peer

### Payload size limits

- Protobuf default 4MB message size limit
- `RegisterSyncResponse` limits `MaxDockets` per batch (default 50) to stay under
- If response exceeds max size, relay call fails; caller retries with halved `MaxDockets`

### Circuit breaker interaction

- Relay calls go through seed node's channel — relay failures do NOT trip the seed node's circuit breaker
- Relay failures are tracked via `PeerConnectionPool.RecordFailureAsync(targetPeerId)` against the target peer (not the seed node). Note: `PeerListManager` itself does not track failures — `PeerConnectionPool` and its internal `CircuitBreaker` handle failure counts

### Duplicate/out-of-order responses

- Correlation ID ensures responses match requests
- Stale responses for expired TCS entries silently discarded
- Register sync is idempotent — `AddOrUpdateDocket` handles duplicates

---

## 7. Files Changed

### New Files

| File | Purpose |
|------|---------|
| `Communication/RelayCommunicationService.cs` | Core relay primitive + request/response correlation |
| `Communication/RelayMessageHandler.cs` | Incoming relay message dispatch + local data serving |
| `Communication/Models/RelayMessages.cs` | Request/response POCOs for register sync payloads |
| `GrpcServices/PeerCommunicationServiceImpl.cs` | NEW gRPC service — handles incoming `SendMessage` calls (does not exist yet) |

### Modified Files

| File | Change |
|------|--------|
| `Protos/peer_communication.proto` | Add 4 `MessageType` enum values (8-11) |
| `Communication/CommunicationProtocolManager.cs` | Relay fallback when `peer.Address` empty (address check BEFORE protocol chain — reorders existing fallback logic) |
| `Distribution/TransactionDistributionService.cs` | Relay fallback in `SendToPeerAsync` |
| `Replication/RegisterReplicationService.cs` | Relay-based batch sync when channel null + address empty |
| `Core/PeerServiceConfiguration.cs` | Add `RelayPollIntervalSeconds` to `RegisterSyncConfiguration` |
| `Replication/RegisterSyncBackgroundService.cs` | Add periodic relay sync poll timer + per-register sync semaphores |
| `Extensions/ServiceCollectionExtensions.cs` | Register new services in DI |
| `Program.cs` | Add `app.MapGrpcService<PeerCommunicationServiceImpl>()` |

### Zero Changes

- **PeerRouter** — no changes, router stays a dumb relay
- **Proto services** — no new RPCs, no new `.proto` files
- **PeerConnectionPool** — unchanged, seed node channels used as-is

---

## 8. Testing Strategy

### Unit Tests

- `RelayCommunicationServiceTests` — mock `PeerConnectionPool` seed channel, verify messages forwarded, verify correlation timeout, verify `CompleteCorrelation` unblocks `SendAndWaitAsync`
- `RelayMessageHandlerTests` — verify dispatch for each message type, verify responses sent back via relay, verify correlation ID matching
- `CommunicationProtocolManagerTests` — verify relay fallback triggers when `peer.Address` empty, verify direct path used when address present
- `TransactionDistributionServiceTests` — verify relay fallback for NAT'd peers
- `RegisterReplicationServiceTests` — verify relay batch sync loop, verify batch processing, verify fallback to next peer on failure

### Integration Test

- Two peers + PeerRouter (or mock seed node)
- Peer A writes a transaction to a register
- Peer B (NAT'd, no address) syncs the register via relay
- Verify Peer B's `RegisterCache` contains the transaction

---

## 9. Future Work (Out of Scope)

- **Streaming relay** — native streaming through seed nodes for higher throughput replication
- **Multiple seed node relay** — load balance relay across seed nodes
- **Relay authentication** — verify sender identity on relayed messages
- **Relay metrics** — track relay usage, latency, success rates on both peer and router
- **Full node replacement** — when n0 becomes a full peer, the relay path works unchanged since it targets `PeerCommunication.SendMessage` not PeerRouter-specific APIs
