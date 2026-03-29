# Feature Specification: P2P Register Replication — End-to-End Transaction Sync

**Feature Branch**: `071-p2p-register-sync`
**Created**: 2026-03-28
**Status**: Draft
**Input**: Enable two NAT'd Sorcha peers to subscribe to each other's registers and sync transactions through the PeerRouter acting as a streaming relay, with docket-driven finalization from cache to register storage.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Streaming Relay for NAT'd Peers (Priority: P1)

Two Sorcha peer nodes sit behind NAT on a local network and cannot accept inbound connections from each other. Both connect outbound to the PeerRouter (n0.sorcha.dev) on the public internet. Each peer establishes a long-lived bidirectional gRPC stream to the Router. When Peer A needs to send a message to Peer B, the Router pushes it down Peer B's existing outbound stream — no inbound connection required.

**Why this priority**: Without this, two NAT'd peers cannot communicate at all. Every other story depends on the relay channel being operational.

**Independent Test**: Start two peer instances behind NAT, both connecting to the PeerRouter. Send a test message from Peer A targeting Peer B via relay. Peer B receives the message on its reverse stream. Verify round-trip message delivery.

**Acceptance Scenarios**:

1. **Given** Peer A and Peer B are both NAT'd with empty external addresses, **When** both establish reverse streams to the PeerRouter, **Then** the Router holds both streams and can push messages to either peer on demand.
2. **Given** Peer A sends a relay message targeting Peer B, **When** the Router receives it, **Then** the Router pushes the message down Peer B's reverse stream within 2 seconds.
3. **Given** Peer B's reverse stream disconnects (network blip), **When** Peer B reconnects, **Then** the stream is re-established and message delivery resumes without manual intervention.
4. **Given** the Router receives a message for a peer with no active reverse stream, **When** delivery fails, **Then** the Router logs the failure and returns an error to the sender (no silent message loss).

---

### User Story 2 - Register Discovery via Heartbeat Advertisements (Priority: P1)

Peer A owns a register containing published blueprints and sealed transactions. Through periodic heartbeats with the PeerRouter, Peer A advertises which registers it holds, including sync state and current version. The PeerRouter shares these advertisements with Peer B during Peer B's heartbeat exchange. Peer B discovers Peer A's register and sees it listed as available on the network.

**Why this priority**: Peers must discover what registers exist before they can subscribe. This is the entry point for the entire replication flow.

**Independent Test**: Peer A advertises a register. Peer B queries available registers and sees Peer A's register listed with correct metadata (name, version, public status).

**Acceptance Scenarios**:

1. **Given** Peer A has a register with published transactions, **When** Peer A sends a heartbeat to the Router, **Then** the heartbeat includes the register's ID, sync state, version, and public flag.
2. **Given** the Router has received Peer A's advertisement, **When** Peer B sends a heartbeat, **Then** the Router's response includes Peer A's advertised registers.
3. **Given** Peer B receives advertised registers from the Router, **When** Peer B processes the heartbeat response, **Then** Peer B's RegisterAdvertisementService stores the remote advertisement and the register appears in `GET /api/registers/available`.
4. **Given** Peer A adds a new transaction increasing the register version, **When** the next heartbeat cycle completes, **Then** Peer B sees the updated version number.

---

### User Story 3 - Subscribe and Sync Full Register History (Priority: P1)

Peer B discovers Peer A's register and subscribes to it for full replication. The Peer Service pulls the complete docket chain and all sealed transactions from Peer A through the streaming relay. The subscription state machine progresses from Subscribing through Syncing to FullyReplicated.

**Why this priority**: This is the core replication flow — getting historical data from the originating peer to the subscribing peer.

**Independent Test**: Peer A has a register with 3 dockets and 10 transactions. Peer B subscribes. After sync completes, Peer B's cache contains all 3 dockets and 10 transactions. Subscription state is FullyReplicated.

**Acceptance Scenarios**:

1. **Given** Peer B has discovered Peer A's register, **When** an operator calls `POST /api/registers/{registerId}/subscribe` on Peer B, **Then** a RegisterSubscription is created with state Subscribing and mode FullReplica.
2. **Given** both peers are NAT'd, **When** RegisterReplicationService attempts to pull dockets from Peer A, **Then** the pull request is routed via the PeerRouter's streaming relay (not direct gRPC).
3. **Given** the relay batch sync is in progress, **When** all dockets and their transactions have been pulled, **Then** the subscription state transitions to FullyReplicated and Peer B's cache contains the full register history.
4. **Given** Peer A is temporarily unreachable during sync, **When** the pull fails, **Then** the subscription records the failure, increments consecutive failures, and retries on the next sync cycle.

---

### User Story 4 - Docket-Driven Finalization to Register Storage (Priority: P1)

When replicated dockets arrive at the subscribing peer, the system examines each docket to determine which transactions it seals. Sealed transactions are validated (signature verification against the originating validator's public key) and moved from the in-memory cache to the local Register Service's persistent storage. After finalization, the transactions are queryable through the subscribing peer's Register Service API.

**Why this priority**: Without finalization, replicated data sits in volatile memory and is lost on restart. Persisting to Register Service storage makes replication durable and useful.

**Independent Test**: Peer A seals transactions tx1, tx2, tx3 in docket D1. After replication, Peer B's Register Service contains tx1, tx2, tx3 as queryable transactions, and the register's height reflects the new docket.

**Acceptance Scenarios**:

1. **Given** a replicated docket D1 arrives at Peer B referencing transactions tx1 and tx2, **When** both tx1 and tx2 are present in the cache, **Then** the system verifies the validator's signature on D1 and moves tx1 and tx2 to Register Service storage.
2. **Given** docket D1 references transaction tx3 but tx3 has not yet arrived in cache, **When** the docket is processed, **Then** finalization of tx3 is deferred until tx3 arrives (other sealed transactions in the docket are not blocked).
3. **Given** transactions have been finalized to Register Service storage, **When** a user queries transactions on Peer B, **Then** the finalized transactions appear in the response.
4. **Given** the validator's signature on a docket is invalid, **When** finalization is attempted, **Then** the docket and its transactions are rejected with an error logged and the subscription is not corrupted.
5. **Given** Peer B restarts after finalization, **When** the Peer Service recovers, **Then** previously finalized transactions remain in Register Service storage (they survived because they were persisted).

---

### User Story 5 - Live Transaction Streaming (Priority: P2)

After the full history sync is complete, Peer B subscribes to a live transaction stream from Peer A. New transactions and dockets created on Peer A are pushed to Peer B in near-real-time via the streaming relay. As new dockets arrive, the finalization process runs automatically, moving sealed transactions to Register Service storage.

**Why this priority**: Live streaming keeps the replica up to date after the initial sync. Without it, Peer B would need periodic full re-syncs.

**Independent Test**: With Peer B fully synced, submit a new action on Peer A's register. Within 10 seconds, the transaction and its docket appear on Peer B's Register Service.

**Acceptance Scenarios**:

1. **Given** Peer B's subscription is FullyReplicated, **When** a new transaction is submitted on Peer A, **Then** the transaction notification reaches Peer B via the streaming relay within 5 seconds.
2. **Given** a new docket is created on Peer A sealing the latest transactions, **When** the docket reaches Peer B, **Then** finalization runs automatically and the transactions move to Register Service storage.
3. **Given** the live stream disconnects, **When** the stream is re-established, **Then** any transactions missed during the gap are caught up automatically (pull any dockets with version greater than last synced version).

---

### User Story 6 - Single Validator Signature Verification (Priority: P2)

The originating peer runs a single validator that builds and signs dockets. Subscribing peers verify the validator's signature on each replicated docket before finalizing its transactions. The validator's public key is discoverable from the register's metadata or shared via advertisements.

**Why this priority**: Signature verification ensures subscribing peers don't accept tampered dockets. Essential for ledger integrity in the single-validator model.

**Independent Test**: Create a docket with a valid signature — finalization succeeds. Tamper with the docket payload — finalization rejects it.

**Acceptance Scenarios**:

1. **Given** a docket signed by the register's designated validator, **When** Peer B verifies the signature, **Then** verification succeeds and finalization proceeds.
2. **Given** a docket with a mismatched or corrupted signature, **When** Peer B attempts verification, **Then** the docket is rejected and an alert is logged.
3. **Given** the validator's public key is not yet known to Peer B, **When** a docket arrives, **Then** Peer B resolves the key from the register's genesis transaction or advertisement metadata before verifying.

---

### Edge Cases

- What happens when Peer B subscribes to a register that has zero transactions (newly created, no dockets yet)? — Subscription should succeed with state FullyReplicated (nothing to sync), then transition to live streaming for new transactions.
- What happens when the PeerRouter restarts while peers have active reverse streams? — Peers detect stream failure and reconnect. No messages are lost that weren't already in flight.
- What happens when two dockets reference overlapping transaction sets? — Each transaction is finalized exactly once (idempotent write to Register Service).
- What happens when the subscribing peer's Register Service is down during finalization? — Finalization retries with backoff. Transactions remain in cache until successfully persisted.
- What happens when Peer A revokes a register or takes it offline? — Advertisement is removed from heartbeats. Peer B's subscription enters Error state. Already-finalized data remains in Peer B's storage.
- What happens when cache memory pressure forces eviction of unfinalised transactions? — The system re-pulls evicted transactions from the source peer when their docket arrives (cache miss triggers on-demand pull).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support long-lived bidirectional gRPC streams between NAT'd peers and the PeerRouter, enabling the Router to push messages to peers that cannot accept inbound connections.
- **FR-002**: System MUST detect when a target peer has no reachable address and route messages through the peer's active reverse stream on the PeerRouter instead of attempting direct connection.
- **FR-003**: System MUST automatically re-establish reverse streams after disconnection without operator intervention, with exponential backoff on repeated failures.
- **FR-004**: System MUST propagate register advertisements through the PeerRouter's heartbeat exchange so that peers behind NAT can discover registers held by other NAT'd peers.
- **FR-005**: System MUST allow an operator to subscribe a peer to a discovered register via a REST endpoint, triggering the full replication state machine (Subscribing → Syncing → FullyReplicated → Active).
- **FR-006**: System MUST pull the complete docket chain and sealed transactions from the source peer via the streaming relay during the initial sync phase.
- **FR-007**: System MUST finalize replicated transactions to the local Register Service's persistent storage when a valid docket sealing those transactions is received.
- **FR-008**: System MUST verify the originating validator's digital signature on each replicated docket before finalizing its transactions.
- **FR-009**: System MUST NOT finalize transactions to Register Service storage without a corresponding valid docket (the docket is the finality signal).
- **FR-010**: System MUST support live transaction streaming from source to subscriber via the relay, delivering new transactions and dockets in near-real-time after initial sync completes.
- **FR-011**: System MUST handle cache eviction gracefully — if a transaction referenced by an arriving docket has been evicted from cache, the system re-pulls it from the source peer before finalization.
- **FR-012**: System MUST persist finalized transactions and dockets to the Register Service, making them queryable through existing Register Service API endpoints.
- **FR-013**: System MUST handle idempotent finalization — writing the same transaction twice to Register Service storage must not create duplicates or errors.
- **FR-014**: System MUST enable relay mode on the PeerRouter deployment (n0.sorcha.dev) to activate streaming relay functionality.

### Key Entities

- **Reverse Stream**: A long-lived bidirectional gRPC connection from a NAT'd peer to the PeerRouter. The peer initiates the connection outbound; the Router uses it to push messages inbound. Keyed by peer ID.
- **Register Advertisement**: Metadata about a register held by a peer, including register ID, name, sync state, current version, docket version, and public flag. Exchanged via heartbeats.
- **Register Subscription**: A peer's commitment to replicate a specific register, with mode (FullReplica or ForwardOnly), sync state, version cursors, and source peer references. Persisted to database.
- **Docket**: A signed batch seal created by the validator, referencing a set of transaction IDs that are now finalized. Contains the validator's signature, merkle root, and sequence number.
- **Finalized Transaction**: A transaction that has been sealed by a docket, verified, and persisted to the local Register Service's storage. Queryable via standard Register Service APIs.
- **Validator Public Key**: The cryptographic public key of the single validator that signs dockets. Discoverable from the register's genesis transaction or advertisement metadata.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Two NAT'd peers behind the same local network can both register with the PeerRouter and exchange messages via the streaming relay with less than 5-second message delivery latency.
- **SC-002**: A subscribing peer fully syncs a register containing 100 transactions across 10 dockets within 60 seconds via the streaming relay.
- **SC-003**: After full sync, new transactions on the source peer appear as finalized, queryable data on the subscribing peer's Register Service within 15 seconds.
- **SC-004**: Finalized transactions survive peer restart — after restarting the subscribing peer, previously finalized data remains queryable in the Register Service without re-syncing.
- **SC-005**: 100% of dockets with invalid signatures are rejected — no tampered data reaches Register Service storage.
- **SC-006**: Reverse streams reconnect automatically after network interruption, with message delivery resuming within 30 seconds of reconnection.
- **SC-007**: The PeerRouter holds no persistent state for relayed messages — all relay traffic is streaming (no in-memory message queues growing unbounded).

## Assumptions

- The PeerRouter at n0.sorcha.dev is the sole public endpoint; all peers connect outbound to it.
- A single validator per register is sufficient for this phase; distributed consensus is a future feature.
- The existing Peer Service infrastructure (RegisterReplicationService, RegisterSyncBackgroundService, RegisterCache, RelayCommunicationService, etc.) is functional and will be extended rather than replaced.
- Register advertisements via heartbeats already propagate through the PeerRouter — any gaps found during implementation will be fixed as part of this feature.
- The Register Service's existing transaction storage can accept replicated transactions without modification (or with minimal adapter changes).
- The validator's public key can be resolved from the register's genesis transaction metadata or from advertisement data shared during heartbeats.

## Dependencies

- PeerRouter (n0.sorcha.dev) must be redeployed with relay mode enabled.
- Peer Service's PostgreSQL migration must be applied (peer.* schema) — this is a known prerequisite from the persistence fix spec.
- Register Service must be accessible from the Peer Service within the same Docker network for finalization writes.
- Validator Service must be running on the originating peer to build and sign dockets.

## Scope Boundaries

**In scope:**
- PeerRouter streaming relay for NAT'd peers (upgrade from fire-and-forget to bidirectional streaming)
- Register advertisement verification and gap fixes
- Subscription trigger via REST endpoint
- Docket-driven finalization from cache to Register Service storage
- Single validator signature verification on replicated dockets
- Live transaction streaming via relay
- Automatic reconnection and catch-up after disconnection

**Out of scope:**
- Distributed consensus or multi-validator docket signing (BLS threshold)
- Leader election
- Peer-to-peer communication without the PeerRouter relay (direct connections between non-NAT'd peers already work)
- UI for subscription management (CLI or API only for this phase)
- Cross-register references or verifiable credential chains during replication
- Rate limiting or quota enforcement at the PeerRouter
