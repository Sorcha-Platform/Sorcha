# Feature Specification: Peer NAT Traversal (Reverse-Stream Rendezvous)

**Feature Branch**: `143-peer-nat-traversal`
**Created**: 2026-05-30
**Status**: Draft
**Input**: Make a register-owner node behind NAT reachable by public subscribers, by folding a reverse-stream rendezvous capability into the peer service. Authoritative design: `docs/superpowers/specs/2026-05-30-peer-nat-traversal-design.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run a register-owning node behind NAT (Priority: P1)

A node operator wants to host a register (as its owner/validator) on a machine that only has outbound internet access — it sits behind NAT with no public address, port-forwarding, or overlay. Today this is impossible: subscribers on other nodes initiate every cross-node connection, so they cannot reach a NAT'd owner to submit transactions or pull sealed dockets, and the register effectively cannot be used from anywhere else. After this feature, the NAT'd owner reaches out to one or more publicly-reachable peers and stays continuously connected, so subscribers can submit work to it and replicate its sealed dockets exactly as if it had a public address.

**Why this priority**: This is the entire reason for the feature and the gating capability that unblocks the parked Assured Identity demo environment. Without it, the issuing authority cannot run on a NAT'd host. Every other story is an enhancement of this one.

**Independent Test**: Stand up an owner node with no inbound reachability and a public subscriber node; submit an action against the owner's register from the subscriber and confirm it seals on the owner; confirm the sealed docket then appears on the subscriber. Delivers the core value on its own.

**Acceptance Scenarios**:

1. **Given** a register owned by a NAT'd node and a public subscriber node, **When** a user submits an action against that register on the subscriber, **Then** the transaction reaches the owner, is sealed into a docket by the owner, and the result is observable on the subscriber.
2. **Given** the same topology, **When** the owner seals new dockets, **Then** the subscriber replicates those dockets without any inbound connection to the owner.
3. **Given** a NAT'd owner that has just started, **When** it comes online, **Then** it establishes its outbound connection(s) to public peers and becomes reachable for submission and sync within one connection/heartbeat cycle.

---

### User Story 2 - Stay connected when a path drops (Priority: P2)

A node operator wants cross-node traffic to a NAT'd node to keep working when one of its connection paths fails (a public peer restarts, the network blips, a connection is severed). The NAT'd node should automatically re-establish connectivity and traffic should reroute, with no operator intervention.

**Why this priority**: A standing demo and any real federation must survive transient failures unattended. Important, but the feature still delivers its core value (US1) with a single path before this hardening lands.

**Independent Test**: With a NAT'd node connected through more than one public peer, sever one path and confirm that submission and sync continue over a remaining path, and that the severed path is automatically restored, all without operator action.

**Acceptance Scenarios**:

1. **Given** a NAT'd node connected through multiple public peers, **When** one of those paths is lost, **Then** submission and sync to the node continue over a surviving path.
2. **Given** a path has been lost, **When** the underlying network/peer recovers, **Then** the NAT'd node re-establishes that path automatically without operator action.
3. **Given** a NAT'd node loses its only path, **When** connectivity is restored, **Then** the node reconnects and resumes serving submission and sync without manual restart.

---

### User Story 3 - Use the closest/fastest path (Priority: P3)

When a NAT'd node is reachable through several public peers, the platform should prefer the closest/fastest path so cross-node latency stays low, rather than pinning traffic to a statically-configured node.

**Why this priority**: Optimisation and correctness-of-routing. The feature works without it (any reachable path suffices), but path quality matters for a responsive demo and for scaling federation.

**Independent Test**: Make a NAT'd node reachable through two public peers with materially different latencies (one of which is the requester itself); confirm from observable routing/metrics that traffic uses the requester's own direct path when it is itself a path, otherwise the lowest-latency path, and that this preference is recomputed as conditions change.

**Acceptance Scenarios**:

1. **Given** a subscriber that is itself one of a NAT'd node's connection anchors, **When** it sends traffic to that node, **Then** it uses its own direct path with no additional intermediary hop.
2. **Given** a subscriber reaching a NAT'd node only through other public peers, **When** multiple such paths exist, **Then** it selects the lowest-latency path.
3. **Given** the chosen path degrades or fails, **When** the next request is routed, **Then** the platform fails over to the next-best path.

---

### User Story 4 - Remove the standalone relay infrastructure (Priority: P3)

A platform maintainer wants the previously-retired standalone relay component fully removed once the peer service provides the same capability, so operators have one fewer always-on service to deploy and maintain.

**Why this priority**: Cleanup and operational simplification. Valuable but strictly dependent on the rendezvous capability (US1) reaching parity first.

**Independent Test**: Confirm the standalone relay component is gone from the codebase and deployment, and that all NAT-traversal scenarios (US1–US3) pass using only the peer service.

**Acceptance Scenarios**:

1. **Given** the peer service provides rendezvous capability, **When** the platform is deployed, **Then** no separate relay component is required or present.
2. **Given** the standalone relay is removed, **When** the NAT-traversal scenarios run, **Then** they all pass using only the peer service.

---

### Edge Cases

- **NAT'd node reachable through zero paths**: if a NAT'd node has not established any outbound path (e.g. all public peers unreachable at startup), submission and sync to it fail clearly and retry as paths come up — they do not hang indefinitely or silently succeed.
- **Both endpoints behind NAT**: if neither side can be reached inbound and they share no common publicly-reachable peer, the connection is reported as unestablished rather than appearing healthy. (General relaying for two NAT'd endpoints with no common public anchor is out of scope for v1.)
- **Stale anchor advertisement**: a subscriber's knowledge of which paths reach a NAT'd node must converge to reality as paths come and go, so traffic is not routed down a path that no longer exists for longer than one refresh cycle.
- **Duplicate/at-least-once delivery**: a transaction or sync request rerouted after a path failure must not cause incorrect duplicate sealing or corrupt replication.
- **Owner self-fan-out**: a NAT'd owner must be able to push its own newly-sealed dockets out to subscribers over the already-established path, not only respond to pulls.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A node MUST be able to act as an owner/validator of a register while having only outbound network connectivity (no inbound reachability), and remain fully usable by other nodes.
- **FR-002**: A NAT'd node MUST establish and maintain persistent outbound connection(s) to publicly-reachable peers, and those connections MUST be reusable to carry requests in both directions (the public peer reaching the NAT'd node over the connection the NAT'd node opened).
- **FR-003**: A publicly-reachable node MUST be able to act as a rendezvous: accept inbound connections from NAT'd nodes and broker submission and sync requests to those nodes over the established connections.
- **FR-004**: Rendezvous capability MUST be determined by a node's own reachability/configuration (a node with a public address is rendezvous-capable; a node without one operates as a NAT'd/outbound-only node). No separate dedicated relay node is required.
- **FR-005**: The platform MUST carry the two cross-node data flows — action/transaction submission to a register's owner, and sealed-docket synchronisation from the owner — across the rendezvous path with the same outcomes as a direct connection.
- **FR-006**: A NAT'd node MUST be able to maintain connections to multiple publicly-reachable peers simultaneously for resilience.
- **FR-007**: The set of paths through which a NAT'd node is currently reachable MUST be advertised/propagated to other nodes through existing peer gossip, so reachability does not depend on static per-subscriber configuration of a single seed.
- **FR-008**: When a NAT'd node is reachable through multiple paths, a node sending it traffic MUST prefer (a) its own direct path if it is itself an anchor, otherwise (b) the lowest-latency available path, and MUST fail over to the next-best path on failure.
- **FR-009**: When a connection path is lost, the affected node MUST automatically re-establish it and reroute traffic without operator intervention.
- **FR-010**: When a NAT'd node is not reachable through any path, submission and sync to it MUST fail explicitly (not hang or silently succeed) and MUST recover once a path is re-established.
- **FR-011**: Rerouting after a path failure MUST NOT cause duplicate sealing or corrupted replication (delivery remains correct under retry/failover).
- **FR-012**: The platform MUST expose operational visibility into NAT-traversal behaviour: number of active reverse connections at a rendezvous, brokered-request latency by flow (submission vs sync), which path was selected, and failover/reconnect counts.
- **FR-013**: Integrity of brokered traffic MUST be preserved end-to-end — a rendezvous relaying traffic cannot alter signed transactions or dockets undetected. (The rendezvous is trusted only as transport within the federation trust domain; payload re-encryption against the rendezvous is out of scope for v1.)
- **FR-014**: Once the peer service provides rendezvous capability at parity, the standalone relay component MUST be removed from the codebase and deployment with no loss of capability.

### Key Entities *(include if feature involves data)*

- **NAT'd node (spoke)**: A node with only outbound connectivity that owns/serves registers and reaches the network by dialling out to public peers.
- **Rendezvous-capable node (hub)**: A publicly-reachable node that accepts connections from NAT'd nodes and brokers traffic to them.
- **Connection path / anchor**: A persistent connection a NAT'd node has established to a public peer, reusable in both directions; a NAT'd node may have several.
- **Anchor-set advertisement**: The propagated information describing which public peers a given NAT'd node is currently reachable through.
- **Routing preference**: The per-sender decision selecting which path to use to reach a NAT'd node (self-direct, then lowest-latency, with failover).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An action submitted on a public subscriber node against a register owned by a NAT'd node is sealed by the owner and observable back on the subscriber, proven end-to-end across a real network where the owner has no inbound reachability. *(Gating; this is the trigger that un-parks the demo environment.)*
- **SC-002**: When one of a NAT'd node's connection paths is severed, cross-node submission and sync continue uninterrupted over a surviving path, and the severed path is automatically restored, with zero operator actions.
- **SC-003**: When a NAT'd node has no remaining path and connectivity is later restored, it resumes serving submission and sync within one connection/heartbeat cycle, with no manual restart.
- **SC-004**: When a NAT'd node is reachable through multiple paths, observable routing shows traffic using the sender's own direct path when it is an anchor, and otherwise the lowest-latency path, with failover on degradation — verifiable from metrics without inspecting code.
- **SC-005**: All NAT-traversal scenarios pass with no separate relay component deployed; the previously-standalone relay is absent from the codebase and deployment.
- **SC-006**: Cross-node submission/sync against a NAT'd owner completes within the same order-of-magnitude latency as the same flow against a publicly-reachable owner under comparable network conditions (no pathological slowdown introduced by the rendezvous hop).

## Assumptions

- Peers participate in a single federation trust domain; a rendezvous node is trusted as a transport intermediary. Transaction and docket signatures provide integrity, so a rendezvous cannot tamper undetectably.
- A NAT'd node knows at least one publicly-reachable peer to dial out to at startup (via existing seed/peer configuration); it does not need to be told about its subscribers in advance.
- "Closest/fastest" is interpreted as measured round-trip latency, reusing existing peer heartbeat timing; geographic/hop-count notions are not required.
- "Within one connection/heartbeat cycle" uses the platform's existing heartbeat/advert cadence as the unit, rather than a fixed wall-clock target.
- Existing cross-node correctness guarantees (chain integrity, dedupe, replay protection) continue to apply unchanged; this feature changes only the transport reachability, not validation semantics.

## Out of Scope (v1)

- Re-encrypting relayed payloads so a rendezvous cannot read metadata it could already see as an ordinary peer.
- Rendezvous authorization, quotas, and abuse/DoS controls.
- A threat model for a malicious or actively-withholding rendezvous.
- Automatic NAT/reachability detection (STUN-style); reachability is configured/derived.
- General relaying between two endpoints that are both behind NAT with no shared publicly-reachable peer.

## Dependencies

- Existing peer discovery/gossip (advert + heartbeat) propagation.
- Existing cross-node submission and docket-sync flows whose transport this feature extends.
- The parked **Assured Identity Demo Environment** consumes this feature; SC-001 is its un-park trigger.
