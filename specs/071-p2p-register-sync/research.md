# Research: P2P Register Sync

**Feature**: 071-p2p-register-sync | **Date**: 2026-03-28

## R1: Streaming Relay — Proto & Implementation Pattern

**Decision**: Use existing `PeerCommunication.Stream` bidirectional RPC. No proto changes needed.

**Rationale**: The `Stream(stream PeerMessage) returns (stream PeerMessage)` RPC is already defined in `peer_communication.proto`. The Router's `RouterCommunicationService` has this RPC stubbed as UNIMPLEMENTED. The proven bidirectional streaming pattern from `RouterHeartbeatService.StreamHeartbeat` provides the implementation template.

**Pattern**:
1. NAT'd peer initiates outbound `Stream()` call to Router (seed node)
2. Router holds the `IServerStreamWriter<PeerMessage>` keyed by peer ID
3. When Router receives a `SendMessage` for a NAT'd peer, it pushes the message down that peer's reverse stream via `responseStream.WriteAsync()`
4. Peer reads from `responseStream` in a background loop, dispatches to `RelayMessageHandler`

**Alternatives considered**:
- New proto RPC (e.g., `RelayStream`) — rejected, existing `Stream` RPC is generic enough
- WebSocket/SignalR — rejected, gRPC bidirectional streaming is already the pattern used throughout Sorcha
- Polling mailbox — rejected by user; PeerRouter is lightweight on Azure, streaming avoids memory pressure

**Key files**:
- Proto: `src/Services/Sorcha.Peer.Service/Protos/peer_communication.proto`
- Router stub: `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterCommunicationService.cs`
- Reference pattern: `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterHeartbeatService.cs` (StreamHeartbeat)
- Client pattern: `src/Services/Sorcha.Peer.Service/Communication/StreamingCommunicationClient.cs`

---

## R2: Register Advertisement Gap at PeerRouter

**Decision**: Fix the PeerRouter to store and relay register advertisements via heartbeats.

**Rationale**: Analysis reveals a critical gap:
- Peers correctly send `advertised_registers` in heartbeat requests
- Router's `ProcessHeartbeat()` **ignores** `request.AdvertisedRegisters` — only processes `RegisterVersions`
- Router's heartbeat response **never includes** other peers' advertised registers
- `RoutingTable.UpdateRegisterVersions()` only updates version numbers, not advertisement metadata
- Discovery service (`GetPeerList`, `FindPeersForRegister`) correctly maps advertisements from `RoutingEntry.AdvertisedRegisters` — but those are only populated during initial `RegisterPeer`, not updated during heartbeats

**Fix required** (3 changes):
1. `RouterHeartbeatService.ProcessHeartbeat()` must extract `request.AdvertisedRegisters` and update the routing table
2. `RoutingTable` needs `UpdateAdvertisedRegisters(peerId, advertisements)` method
3. `RouterHeartbeatService` response must include aggregated advertisements from other healthy peers

**Alternatives considered**:
- Rely solely on discovery RPCs (`FindPeersForRegister`) — rejected, heartbeats are the primary state-sync channel and run every 30 seconds vs discovery which is on-demand
- Push advertisements via relay messages — rejected, heartbeats already carry the data, just need to process it

---

## R3: Docket-Driven Finalization Path

**Decision**: Use existing `IRegisterServiceClient.WriteDocketAsync()` for finalization. Verify docket signature using `DocketHasher` + `Sorcha.Cryptography` verification.

**Rationale**: The finalization path already exists in the Validator Service's `DocketDistributor.SubmitToRegisterServiceAsync()`. We replicate this pattern in the Peer Service:
1. Replicated docket arrives with `ProposerSignature` (public key + signature bytes + algorithm)
2. Recompute docket hash using `DocketHasher.ComputeDocketHash()` — deterministic from RegisterId, DocketNumber, PreviousHash, MerkleRoot, Timestamp
3. Verify signature using `Sorcha.Cryptography` verification (ED25519/P-256/RSA-4096)
4. Convert to `DocketModel` with `TransactionModel` list
5. Call `IRegisterServiceClient.WriteDocketAsync()` to persist

**Docket structure (Validator model)**:
- `DocketId`, `RegisterId`, `DocketNumber`, `PreviousHash`, `DocketHash`
- `MerkleRoot` — SHA-256 of transaction tree
- `ProposerSignature` — `{ PublicKey, SignatureValue, Algorithm, SignedAt }`
- `Transactions` — full transaction objects (not just IDs)
- `Votes` — list of `ConsensusVote` (empty for single validator)

**Key insight**: Docket contains full Transaction objects, not just IDs. So when a docket arrives via replication, the transactions come with it. No need to match cache transactions to docket references — the docket IS the delivery mechanism.

**Alternatives considered**:
- Direct MongoDB writes bypassing Register Service — rejected, violates microservices principle and skips Register Service's business logic
- New internal finalization endpoint — rejected, `WriteDocketAsync()` already exists and handles the write

---

## R4: Reverse Stream Lifecycle Management

**Decision**: Implement reconnection with exponential backoff, stream keepalive via periodic ping messages.

**Rationale**: Long-lived gRPC streams can silently die (TCP half-open, load balancer timeout, Azure Container Apps idle timeout). The PeerHeartbeatBackgroundService already handles reconnection for heartbeats — same pattern applies.

**Design**:
- Initial connection attempt on Peer Service startup (after seed node bootstrap)
- On disconnect: exponential backoff (2s, 4s, 8s, 16s, max 60s)
- Keepalive: send empty/ping PeerMessage every 30s to prevent idle timeout
- Azure Container Apps default idle timeout is 4 minutes — keepalive interval must be shorter
- Router tracks active streams in `ConcurrentDictionary<string, IServerStreamWriter<PeerMessage>>`
- On peer disconnect, Router removes stream entry; next `SendMessage` for that peer returns error

**Alternatives considered**:
- Rely on gRPC keepalive pings (HTTP/2 PING frames) — insufficient, application-level keepalive needed for Azure proxy layers
- Client-side health check before each send — adds latency, stream liveness is better maintained proactively

---

## R5: Single Validator Key Resolution

**Decision**: Resolve validator public key from the register's genesis docket (DocketNumber 0), which contains the validator's signature and public key.

**Rationale**: Every register starts with a genesis docket signed by the validator. The `ProposerSignature.PublicKey` on the genesis docket is the validator's public key. Subscribing peers can extract this during initial sync (genesis docket is always the first docket pulled).

**Flow**:
1. During PullFullReplicaAsync, the first docket pulled is genesis (DocketNumber 0)
2. Extract `ProposerSignature.PublicKey` from genesis docket
3. Cache the key per register for subsequent docket verification
4. All subsequent dockets from the same validator can be verified against this cached key

**Alternatives considered**:
- Advertisement metadata — rejected, adds complexity and the key is already in the genesis docket
- Separate key exchange RPC — rejected, overkill for single-validator model
- Register Service lookup — rejected, adds service dependency during verification hot path
