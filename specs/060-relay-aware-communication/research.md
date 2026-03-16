# Research: Relay-Aware Peer Communication

**Phase 0 Output** | **Date**: 2026-03-16

## R1: Seed Node Channel Access for Relay

**Decision**: Use `PeerConnectionPool.GetAllActiveChannels()` filtered by `PeerListManager.GetPeer(peerId)?.IsSeedNode == true` to find seed node channels for relay routing.

**Rationale**: The connection pool already maintains gRPC channels to seed nodes via `BootstrapFromSeedNodesAsync()`. No new connection management needed — relay piggybacks on existing seed channels.

**Alternatives considered**:
- Dedicated relay channel pool → rejected (over-engineering, seed channels already work)
- Direct channel creation per relay call → rejected (wasteful, bypasses circuit breakers)

## R2: Request/Response Correlation Pattern

**Decision**: `ConcurrentDictionary<string, TaskCompletionSource<PeerMessage>>` keyed by GUID correlation ID. Timeout via `CancellationTokenSource` that removes the entry on expiry.

**Rationale**: Standard async coordination pattern in .NET. Thread-safe, minimal allocations. TCS integrates naturally with async/await. GUID correlation IDs are embedded in the JSON payload, not the proto envelope, so zero proto changes for correlation.

**Alternatives considered**:
- Channel<T> per request → rejected (heavier weight, same result)
- Callback dictionary → rejected (TCS is idiomatic async pattern)
- Proto-level correlation field → rejected (would require PeerRouter changes)

## R3: NAT Detection Signal

**Decision**: `string.IsNullOrEmpty(peer.Address)` is the ground truth NAT detection signal. Peers behind NAT register with the seed node but have no publicly reachable address.

**Rationale**: Already the case in the existing system — NAT'd peers have empty `Address` in `PeerNode`. No new fields or detection mechanisms needed. The PeerRouter sets address from the registration, and NAT'd peers can't provide one.

**Alternatives considered**:
- Explicit `IsNatd` flag on PeerNode → rejected (redundant with empty address, adds proto field)
- STUN-based detection → rejected (adds complexity, empty address is sufficient)

## R4: Register Sync via Relay Transport

**Decision**: New `MessageType` enum values (8-11) on existing `PeerMessage` proto. Payloads serialized as JSON into `PeerMessage.payload` bytes field.

**Rationale**: Zero PeerRouter changes — the router is a dumb relay that forwards any `PeerMessage` regardless of `MessageType`. JSON payloads are flexible and debuggable. The 4MB proto limit accommodates 50-docket batches for typical transaction sizes.

**Alternatives considered**:
- New proto service/RPCs → rejected (requires PeerRouter changes)
- Binary protobuf payloads → rejected (JSON is sufficient for test network, easier to debug)
- Streaming relay → rejected (PeerRouter returns Unimplemented for streaming)

## R5: SenderPeerId Population

**Decision**: `RelayCommunicationService` must populate `PeerMessage.SenderPeerId` with `PeerServiceConfiguration.NodeId ?? Environment.MachineName`.

**Rationale**: The PeerRouter's `RouterCommunicationService` validates that `SenderPeerId` is not empty (lines 60-65) and rejects messages without it. The existing `CommunicationProtocolManager` sets `SenderPeerId` to empty string — this works for direct sends (recipient sees the gRPC connection source) but fails through relay because the router sees the sender as the relay intermediary.

**Alternatives considered**:
- Remove router validation → rejected (security concern, allows spoofing)
- Fix CommunicationProtocolManager to also set SenderPeerId → rejected (out of scope, direct sends work fine)

## R6: Dual Delivery Path for Transaction Notifications

**Decision**: `RelayMessageHandler.HandleTransactionNotificationAsync` unwraps the relayed `TRANSACTION_NOTIFICATION` payload and feeds it into the same processing logic that the `TransactionDistribution.NotifyTransaction` gRPC handler uses.

**Rationale**: Transaction notifications can arrive via two routes: directly through the `TransactionDistribution.NotifyTransaction` RPC (existing path) or relayed as a `PeerMessage` with `MessageType.TRANSACTION_NOTIFICATION` (new path). Both must trigger identical processing — gossip "already seen" deduplication, subscription-based sync triggering.

**Alternatives considered**:
- Separate processing logic for relayed notifications → rejected (code duplication, divergence risk)
- Convert relayed notification back to gRPC call → rejected (circular, unnecessary)

## R7: Circuit Breaker Interaction

**Decision**: Relay failures are tracked via `PeerConnectionPool.RecordFailureAsync(targetPeerId)` against the target peer, NOT the seed node. Relay calls go through the seed node's channel but a relay failure means the target is unreachable, not that the seed is down.

**Rationale**: If relay failures tripped the seed node's circuit breaker, a single unreachable target peer would break all relay communication through that seed. The seed node channel health is tracked separately via its own heartbeat/connection management.

**Alternatives considered**:
- Track against seed node → rejected (breaks all relay on single peer failure)
- No failure tracking → rejected (loses visibility into peer health)

## R8: Response Size Limits

**Decision**: `RelayMessageHandler` caps docket batches at `MaxDockets` (default 50) per response. If serialized size would exceed 3MB, reduce batch size. Caller retries with halved `MaxDockets` on size-exceeded failures.

**Rationale**: Protobuf default is 4MB message size (Peer Service configured to 16MB via `MaxReceiveMessageSize`). Leaving 1MB headroom (or 13MB with the 16MB config) prevents truncation. The `MaxDockets` field in `RegisterSyncRequest` gives the requester control over batch size.

**Alternatives considered**:
- Fixed batch size → rejected (different register data sizes, needs flexibility)
- Compression → rejected (adds complexity, batch sizing is sufficient)

## R9: Periodic Poll Integration Point

**Decision**: Extend `RegisterSyncBackgroundService` with a second `PeriodicTimer` for relay polling. Per-register sync guard via `ConcurrentDictionary<string, SemaphoreSlim>` shared between the periodic poll and notification-triggered sync.

**Rationale**: `RegisterSyncBackgroundService` already manages register subscriptions, state machines, and periodic sync loops. Adding the relay poll here keeps all sync coordination in one place. The semaphore prevents concurrent syncs on the same register (e.g., poll fires while notification-triggered sync is running).

**Alternatives considered**:
- Separate background service → rejected (splits sync coordination, harder to share state)
- Lock per register → rejected (SemaphoreSlim is async-friendly, lock is not)
