# Feature Specification: Relay-Aware Peer Communication

**Feature Branch**: `060-relay-aware-communication`
**Created**: 2026-03-16
**Status**: Draft
**Input**: User description: "Relay-aware peer communication for NAT traversal via seed node relay"
**Design Spec**: `docs/superpowers/specs/2026-03-16-relay-aware-communication-design.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - NAT'd Peer Sends Messages Via Relay (Priority: P1)

A peer node running behind NAT (no publicly reachable address) needs to send messages to other peers in the network. When the peer detects that a target peer has no reachable address, it automatically routes the message through the first available seed node relay. The sending peer does not need to know it is being relayed — the fallback is transparent.

**Why this priority**: Without message relay, NAT'd peers are completely isolated — they can register and heartbeat with seed nodes but cannot participate in the network. This is the foundational capability that all other relay features depend on.

**Independent Test**: Can be fully tested by deploying two peers behind NAT with a seed node relay, sending a message from Peer A to Peer B, and verifying Peer B receives it.

**Acceptance Scenarios**:

1. **Given** a peer with an empty address field in the peer list, **When** the communication manager attempts to send a message, **Then** the message is routed through the first healthy seed node's relay instead of attempting a direct connection.
2. **Given** no seed node is connected, **When** a relay send is attempted, **Then** the send returns failure gracefully without crashing or hanging.
3. **Given** a peer with a valid address, **When** a message is sent, **Then** the existing direct connection path is used (relay is not invoked).

---

### User Story 2 - Transaction Distribution Reaches NAT'd Peers (Priority: P1)

When a new transaction is created and distributed via the gossip protocol, NAT'd peers must receive transaction notifications through the relay. The gossip engine selects targets as usual, but the send path falls back to relay when the target peer has no reachable address.

**Why this priority**: Transaction distribution is the primary data flow in the network. Without it, NAT'd peers never learn about new transactions and cannot participate in consensus or validation.

**Independent Test**: Can be tested by creating a transaction on Peer A and verifying that Peer B (NAT'd) receives the transaction notification via relay.

**Acceptance Scenarios**:

1. **Given** a NAT'd peer is selected as a gossip target, **When** a transaction is distributed, **Then** the notification is sent via the seed node relay.
2. **Given** a transaction notification arrives via relay, **When** the peer processes it, **Then** it is handled identically to a directly-received notification (same processing logic, no duplicate paths).

---

### User Story 3 - Peer Receives and Dispatches Relayed Messages (Priority: P1)

A peer must be able to receive incoming relayed messages (forwarded by the seed node) and dispatch them to the appropriate handler. This includes sync requests (serve local data back to the requester), sync responses (complete pending correlation), and transaction notifications (trigger sync if subscribed).

**Why this priority**: This is the receiving side of relay — without it, relayed messages arrive but are not processed. It is co-dependent with User Story 1 (sending side).

**Independent Test**: Can be tested by sending a relayed message to a peer and verifying the appropriate handler processes it and responds correctly.

**Acceptance Scenarios**:

1. **Given** a peer receives a relayed register sync request, **When** it has local data for the requested register, **Then** it reads dockets from its cache and sends a response back via relay.
2. **Given** a peer receives a relayed sync response, **When** a pending correlation exists for the correlation ID, **Then** the pending request is completed and the caller receives the response.
3. **Given** a peer receives a relayed transaction notification for a subscribed register, **When** the notification is processed, **Then** a sync request is triggered to pull the latest data.
4. **Given** a response arrives for an expired or unknown correlation ID, **When** the system attempts to match it, **Then** the response is silently discarded.

---

### User Story 4 - Register Sync Between NAT'd Peers (Priority: P2)

A peer needs to synchronize register data (dockets and transactions) from another peer. When the source peer is only reachable via relay, the replication service falls back to a batch-based request/response pattern using relayed messages instead of streaming. The peer sends sync requests through the relay, receives batched responses, and processes them into its local cache.

**Why this priority**: Register replication is essential for data consistency across the network, but it depends on the messaging relay (P1) being functional first. The batch-based approach is less efficient than streaming but enables register sync on test networks where all peers are NAT'd.

**Independent Test**: Can be tested by writing data to a register on Peer A, then verifying Peer B (NAT'd) can pull and cache all dockets and transactions via relay sync requests.

**Acceptance Scenarios**:

1. **Given** a source peer with register data is only reachable via relay, **When** replication is requested, **Then** dockets are pulled in batches via request/response messages through the relay.
2. **Given** a batch of dockets contains transaction IDs, **When** the batch is processed, **Then** the corresponding transaction data is pulled via a separate relay request.
3. **Given** a sync response indicates more data is available, **When** the batch is processed, **Then** subsequent requests are sent to pull the remaining data.
4. **Given** a relay sync request times out, **When** the timeout occurs, **Then** the system tries the next available peer for that register.

---

### User Story 5 - Periodic Sync Catches Missed Updates (Priority: P2)

A background process periodically polls for register updates from NAT'd peers. This catches any transaction notifications that were missed (e.g., due to temporary relay unavailability) and handles startup catch-up when a peer joins the network after register changes have occurred.

**Why this priority**: The notification-triggered sync (User Story 4) provides near-real-time updates, but without a backstop, missed notifications create silent data divergence. The periodic poll is a safety net, not the primary sync mechanism.

**Independent Test**: Can be tested by temporarily disconnecting a peer from the relay, making register changes, reconnecting, and verifying the periodic poll catches up within one poll interval.

**Acceptance Scenarios**:

1. **Given** a peer has active register subscriptions and NAT'd peers hold those registers, **When** the poll timer fires, **Then** the peer sends sync requests to NAT'd peers via relay.
2. **Given** a sync is already in progress for a register, **When** the poll timer fires for the same register, **Then** the poll skips that register.
3. **Given** the first peer queried successfully responds, **When** processing the response, **Then** the poll stops querying additional peers for that register.

---

### Edge Cases

- What happens when a relay response payload exceeds the message size limit? The responding handler caps the number of dockets returned, and the requester retries with a smaller batch size.
- What happens when the seed node relay is down but peers have registered addresses? Direct communication continues unaffected — relay is only used when peer address is empty.
- What happens when a transaction notification arrives both directly and via relay? Processing is idempotent — duplicate notifications are handled safely via the existing "already seen" check in the gossip engine.
- What happens when a peer's correlation dictionary grows unbounded from unanswered requests? Each correlation has a timeout that removes the entry — no unbounded growth.
- What happens when the sender peer ID is missing from a relayed message? The seed node rejects the message before forwarding — the relay service must always populate the sender ID.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST route messages through the first healthy seed node relay when the target peer has no reachable address (empty address field).
- **FR-002**: System MUST use the existing direct communication path when the target peer has a reachable address — relay is a fallback, not a replacement.
- **FR-003**: System MUST support request/response correlation for relay messages, matching responses to requests via a unique correlation ID embedded in the message payload.
- **FR-004**: System MUST support register data synchronization via relay using batch-based request/response messages instead of streaming.
- **FR-005**: System MUST periodically poll for register updates from NAT'd peers as a backstop to notification-triggered sync, with a configurable interval (default 60 seconds).
- **FR-006**: System MUST be able to receive incoming relayed messages and dispatch them to appropriate handlers based on message type.
- **FR-007**: System MUST serve local register data in response to relay sync requests from other peers.
- **FR-008**: System MUST populate the sender peer ID on all relayed messages (seed node rejects messages without it).
- **FR-009**: System MUST enforce response size limits when serving register data via relay to stay within message size constraints.
- **FR-010**: System MUST handle relay failures gracefully — return failure status without crashing, try next peer, or wait for next poll cycle.
- **FR-011**: System MUST NOT require any changes to the PeerRouter (seed node) — all relay communication uses the existing relay endpoint.
- **FR-012**: System MUST prevent concurrent sync operations on the same register (per-register guard).
- **FR-013**: System MUST trigger a register sync when a transaction notification is received via relay for a subscribed register.

### Key Entities

- **Relay Message**: An existing message envelope containing sender ID, recipient ID, message type, binary payload, and timestamp. Extended with new message type values for register sync operations.
- **Correlation Entry**: A pending request/response pair identified by a unique correlation ID, with an associated timeout. Used to match relay responses to their original requests.
- **Register Sync Batch**: A batch of docket entries (version, data, hash chain, transaction IDs) sent as a relay response. Limited in size to stay within message size constraints.
- **Transaction Data Batch**: A batch of full transaction entries (ID, data, checksum) requested by transaction IDs and returned as a relay response.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: NAT'd peers can send and receive messages to/from any other peer in the network within 5 seconds (including relay latency).
- **SC-002**: Transaction notifications reach NAT'd peers via relay with the same reliability as direct peers — no silent message loss in a stable network.
- **SC-003**: Register data synchronizes between NAT'd peers within 2 poll intervals (default: 2 minutes) of the data being created.
- **SC-004**: Relay failures do not impact direct peer communication — peers with reachable addresses continue to use direct connections unaffected.
- **SC-005**: No changes to the PeerRouter are required — the existing relay endpoint handles all new message types without modification.
- **SC-006**: All relay message types are processed correctly when received (sync requests served, sync responses correlated, notifications trigger sync).

## Assumptions

- The seed node relay mode is enabled and the message forwarding endpoint is operational.
- NAT'd peers are reliably identified by having an empty address field in the peer list — this is the ground truth signal.
- The test network has a small number of peers (under 10), so relay bottleneck through a single seed node is acceptable.
- Register data payloads fit within 50-docket batches without exceeding message size limits for typical transaction sizes.
- The PeerRouter is a temporary shim — the relay mechanism is designed to work identically when the seed node becomes a full peer node.
- The existing connection pool maintains stable seed node channels that can be used for relay routing.

## Constraints

- Maximum message size per relay message — register sync batches must be sized accordingly.
- Relay adds latency (sender to seed node to recipient and back for request/response) — acceptable for test network, not suitable for high-throughput production use.
- All relayed traffic funnels through a single seed node — creates a bottleneck that limits network scale.
- Streaming replication is not supported through relay — only batch-based request/response patterns work through the unary relay.

## Dependencies

- Existing seed node relay endpoint must be enabled and operational.
- Connection pool seed node channel management must be working (bootstrap, reconnection).
- Local register cache must support reading docket and transaction data for serving sync requests.
- Background sync service must expose subscription state for notification-triggered sync.
