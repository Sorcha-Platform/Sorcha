# Phase 1 Data Model: Peer NAT Traversal

All entities are **in-memory / process-local** (no persistence, no EF migration,
not on the F113 storage-audit list). They live in `Sorcha.Peer.Service`.

## ReverseStreamEntry

A single live reverse duplex stream accepted by a rendezvous from a NAT'd peer.
Ported from `Sorcha.PeerRouter/Models/ReverseStreamEntry.cs`.

| Field | Type | Notes |
|---|---|---|
| `PeerId` | string (key) | The NAT'd peer that opened the stream |
| `ResponseStream` | `IServerStreamWriter<PeerMessage>` | Used to push brokered requests to the peer |
| `StreamCts` | `CancellationTokenSource` | Cancels the read loop when superseded/closed |
| `ConnectedAt` | `DateTimeOffset` | Establishment time |
| `LastActivityAt` | `DateTimeOffset` (mutable) | Liveness; updated on each send/recv |
| `IsActive` | bool (mutable) | Cleared when superseded by a reconnect |

**Lifecycle**: established on first message of `Stream` (carries `SenderPeerId`) →
active while the duplex stream is open → superseded (a newer stream for the same
`PeerId` cancels+replaces the old) → removed on disconnect/cancel.

## ReverseStreamManager (registry)

Thread-safe `peerId → ReverseStreamEntry`. Ported from
`Sorcha.PeerRouter/Services/ReverseStreamManager.cs`. Registered as a peer-service
**singleton**.

- `RegisterStream(peerId, stream)` — add/replace (cancels prior entry for that peer).
- `TryGetStream(peerId, out entry)` — lookup for brokering.
- `RemoveStream(peerId)` — on disconnect.
- `DispatchAsync(peerId, message)` *(new helper)* — write a brokered request to the
  peer's stream; surfaces `Unavailable` if no active stream.
- `ActiveCount` *(new)* — gauge source for observability.

## CorrelatedRequest (client-side, exists)

A pending request-over-relay awaiting its response, matched by `CorrelationId`
GUID embedded in the `PeerMessage` payload. Already present in
`RelayCommunicationService` / `RelayMessageHandler` (request/response message-type
pairs in the proto: `REGISTER_SYNC_REQUEST/RESPONSE`,
`TRANSACTION_DATA_REQUEST/RESPONSE`). No change beyond multi-anchor awareness.

## Anchor (client-side, NEW — extends single-seed)

One outbound reverse stream a NAT'd node maintains to a public peer. Today the NAT'd
node holds one (to its seed); v1 holds a **set**.

| Field | Type | Notes |
|---|---|---|
| `AnchorPeerId` | string | The public peer this stream is held to |
| `ChannelAddress` | string | Dialled gRPC address |
| `State` | enum `Connecting\|Established\|Reconnecting\|Failed` | Per-anchor |
| `LastHeartbeatRttMs` | int? | Feeds latency-preferred selection |
| `EstablishedAt` / `LastActivityAt` | `DateTimeOffset` | Liveness |

**State transitions**: `Connecting → Established` (hello/heartbeat ack) →
`Reconnecting` (stream drop, backoff) → `Established` (recovered) / `Failed`
(give up after policy). Per-anchor reconnect/backoff already in
`RelayCommunicationService`; extend its single field to a keyed collection.

## AnchorAdvertisement (gossip payload extension)

Carried on the existing advert/heartbeat so subscribers learn how to reach a NAT'd
node. Additive field on the existing advertisement model.

| Field | Type | Notes |
|---|---|---|
| `NatdPeerId` | string | The NAT'd node being described |
| `Anchors` | `string[]` | Public peer ids currently anchoring it (its live reverse streams) |
| `AdvertisedAt` | timestamp | For staleness/convergence |

Ingested into the **NodeRoutingTable** (below). Must converge within one
advert/heartbeat cycle as anchors come/go (edge case in spec).

## NodeRoutingTable (subscriber-side, NEW/EXTEND)

Per-node view used to pick a path to a NAT'd target.

| Field | Type | Notes |
|---|---|---|
| `PeerId` | string (key) | Target node |
| `DirectAddress` | string? | Non-empty ⇒ reachable directly (public target) |
| `Anchors` | `AnchorRef[]` | From `AnchorAdvertisement`; each with last-known RTT |
| `IsSelfAnchor` | bool (derived) | True if *this* node holds a reverse stream to the target |

## RoutingPreference (selection result, NEW)

The output of path selection for a given target, consumed by
`CommunicationProtocolManager`.

- Ordering: **self-anchor (direct over local reverse stream)** → **lowest-RTT
  remote anchor** → next-best on `CircuitBreaker` open.
- Re-evaluated per request (cheap) so it adapts as RTT/anchor-set change.

## Validation & invariants

- A NAT'd node (`PublicAddress` empty) MUST NOT be selected via `DirectAddress`.
- A rendezvous MUST NOT dial a NAT'd peer; brokering is only via an active
  `ReverseStreamEntry`.
- Selecting a path whose anchor has no live stream at the rendezvous yields
  `Unavailable` → failover, never a hang (FR-010).
- Reconnect/supersede is idempotent: replacing a stream for a `PeerId` cancels the
  prior entry exactly once.
