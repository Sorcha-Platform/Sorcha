# Feature Specification: Register State Aggregation & Local Relationship

**Feature Branch**: `108-register-local-relationship`
**Created**: 2026-04-21
**Status**: Draft
**Input**: User description: "Register state aggregation and local relationship derivation. Register-service becomes the single authoritative source of per-register state on this installation, composing inputs from peer-service (network-height high-water-mark from heartbeat adverts) and validator-service (mempool backlog, sealing progress). Introduces RegisterLocalRelationship — a derived view over the latest RegisterControlRecord that identifies this node's role (Owner/Admin/Auditor/Designer/Validator/Subscriber) based on the local wallet/validator key. Replaces the current string-based Register.SyncState with a typed enum and adds derived SyncState transitions (Indeterminate → Syncing → Caught-up → Error) based on LocalHeight vs NetworkHeight + quorum of recent peer adverts. Validator-service is re-wired to pull its monitoring enrollment from register-service ('which registers is my key on the roster for?') rather than auto-enrolling as a side-effect of /validate. This unblocks Finding B (forward-to-owner for NAT'd subscriber submissions) from the PingPongN1 walkthrough: ActionExecutionService can then always call both peer distribution and validator mempool with no ownership-aware branching, because each downstream service uses the derived relationship to decide what to actually do (validator seals iff IsValidator; peer gossip is the only path to sealing for subscribers). Lifecycle events: startup (load, derive role, discover heights), runtime docket write (recompute on control-tx detection), runtime peer advert ingest (update network-height), governance ops (roster changes invalidate cached relationship and push role-change event to validator). Out of scope: refactoring Register entity storage beyond the SyncState enum migration; full gRPC-tunnel for submission (HTTP + existing peer gossip infra is sufficient); multi-owner ambiguity handling."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Subscriber can submit action transactions on a register they don't own (Priority: P1)

A participant running a node behind NAT (or any subscriber installation) submits a blueprint action on a register whose authoritative copy lives on a different node. The submission reaches the register owner, gets sealed into a docket, and replicates back to the submitter's local register — all without the submitter needing to know, at the application layer, who owns what.

**Why this priority**: This is the immediate operational gap. Today the PingPongN1 walkthrough reports PARTIAL because a local (NAT'd) subscriber cannot complete the reverse leg of a round-trip — the action they submit lands only on their local register and never reaches the owner's validator. Fixing this is the last piece needed for full cross-machine P2P round-trips to work end-to-end.

**Independent Test**: Run `walkthroughs/PingPongN1/run.ps1 -Rounds 2` against a local Docker stack subscribing to a register owned by `n1.sorcha.dev`. Verify that action transactions submitted on local appear on n1 (via n1's register query API) within the walkthrough's 120-second window, and that the resulting docket subsequently appears back on local. Walkthrough result flips from PARTIAL to PASS.

**Acceptance Scenarios**:

1. **Given** a node that subscribes to a register owned by a remote peer, **When** a blueprint action is submitted on the local node, **Then** the transaction is accepted by the remote owner's validator, sealed in a docket, and that docket replicates back to the local node.
2. **Given** a node that owns a register locally, **When** a blueprint action is submitted, **Then** the transaction is processed by the local validator with no remote forwarding involved (existing behaviour preserved).
3. **Given** a submission arrives at a node that is neither owner nor validator for the register, **When** it is processed, **Then** the transaction is not sealed locally (no fork), but is gossiped onward to reach a node that can seal it.

---

### User Story 2 — Node derives its role on every register at startup and on changes (Priority: P1)

When a node boots, it reads each register it holds and computes, for each one, whether it is an Owner, Admin, Auditor, Designer, Validator, or Subscriber based on the genesis control record and any subsequent governance transactions. The same derivation runs again whenever a new control transaction is sealed into a docket (governance ops like AddValidator, RotateKey, transfer of ownership).

**Why this priority**: Every downstream decision about routing, sealing, and enrolment depends on this. Without it, each service invents its own heuristic (and today they conflict — the validator auto-enrols via side effects, while the peer-service tracks subscription source peers separately). This is the foundation that unblocks Story 1.

**Independent Test**: Boot a node that holds three registers: one where the node's wallet is an Owner, one where its validator key is on the roster but the owner attestation is someone else, and one where the node is a plain subscriber. Verify that a diagnostic endpoint returns the correct role set for each. Then publish a governance transaction that adds the node's key to the third register's roster, verify the role flips within one docket-seal cycle without a node restart.

**Acceptance Scenarios**:

1. **Given** a register where the local node's wallet signed the Owner attestation in the genesis control record, **When** the relationship is queried, **Then** the response reports `IsOwner = true` and no role-resolution fallback is used.
2. **Given** a register whose latest control record lists the local node's validator public key in the `validators` roster, **When** the relationship is queried, **Then** the response reports `IsValidator = true` regardless of whether the node is also an owner or admin.
3. **Given** a register whose roster is updated by a governance transaction sealed in docket N, **When** that docket is finalised locally, **Then** subsequent relationship queries return the updated role set without requiring a node restart.
4. **Given** a relationship has been computed and cached for a register, **When** any transaction other than a control transaction is sealed, **Then** the cached relationship is not invalidated (avoiding unnecessary recomputation).

---

### User Story 3 — Operator sees an accurate, trustworthy sync state for every register (Priority: P2)

A platform operator looking at a node's admin UI (or health endpoint) can see, per register, whether the node is up-to-date with the network, actively syncing, or has fallen behind. The reported state is derived from real evidence (network-height high-water-mark from recent peer adverts, local docket height, validator mempool backlog) rather than a free-text status string.

**Why this priority**: Replaces the current free-text `SyncState` with a typed lifecycle. Drives the operator's confidence that "Caught-up" actually means caught-up. Prerequisite for any automation that keys off sync state (e.g. "don't route user-facing reads to a lagging replica").

**Independent Test**: Stop a subscriber node for 60 seconds while the owner produces three new dockets. Restart the subscriber; before the pull completes the state reports `Syncing` with a declared gap. Once the pull catches up and two consecutive adverts from the owner confirm the same height, the state transitions to `Caught-up`. Stop the owner; after the peer-advert staleness window elapses, the subscriber's state degrades to `Indeterminate`.

**Acceptance Scenarios**:

1. **Given** a subscriber whose local docket height matches the network-height reported by at least two recent peer adverts within the staleness window, **When** the sync state is queried, **Then** the state is `Caught-up`.
2. **Given** a subscriber whose local docket height is lower than the network-height, **When** the sync state is queried, **Then** the state is `Syncing` and the gap (number of dockets behind) is reported.
3. **Given** a subscriber that has not received any peer advert within the staleness window, **When** the sync state is queried, **Then** the state is `Indeterminate` (the node cannot distinguish "up-to-date in an empty network" from "stale and isolated").
4. **Given** a subscriber whose pull pipeline has failed repeatedly, **When** the sync state is queried, **Then** the state is `Error` and the last-known error is reported.

---

### User Story 4 — Validator enrols itself from the roster, not from submission side-effects (Priority: P2)

A node running a validator service starts processing a register's mempool only when its validator key is on that register's roster. Subscribers that accidentally receive or generate a transaction submission for a register they don't validate never try to seal it — the mempool just never gets processed for them.

**Why this priority**: Removes a latent fork-generating bug. Today, any call to the validator's validate endpoint adds the register to the validator's monitoring list — including on subscribers — so two nodes trying to seal the same register's mempool independently would produce divergent docket chains. Necessary for correctness in any multi-node deployment, not just PingPongN1.

**Independent Test**: Stand up two nodes sharing one register where only Node A is on the roster. Submit a transaction against that register on Node B directly. Observe that Node B's mempool receives the transaction but Node B never produces a docket for that register. Check Node B's monitoring list and confirm the register is absent. Node A still seals normally.

**Acceptance Scenarios**:

1. **Given** a node whose validator key is not on a register's roster, **When** a transaction for that register is submitted to the validator endpoint, **Then** the validator does not start monitoring that register and no docket is produced locally.
2. **Given** a node that owns a register and whose validator key is on its roster, **When** the node boots, **Then** the register is in the monitoring list before the first transaction is submitted.
3. **Given** a governance transaction that adds a new key to the roster is sealed, **When** that docket is finalised on the node holding the new key, **Then** the node begins monitoring that register within one validation tick without requiring a restart.
4. **Given** a governance transaction that removes a key from the roster is sealed, **When** that docket is finalised on the affected node, **Then** the node drains any in-flight sealing work and stops monitoring the register.

---

### User Story 5 — Blueprint action submission is owner-agnostic (Priority: P2)

A developer writing a blueprint action handler submits a signed transaction through a single call. The submission reaches wherever it needs to reach — whether the node is the owner, a subscriber, a validator, or all three. The blueprint layer has no knowledge of register ownership, peer topology, or sealing eligibility.

**Why this priority**: Keeps the blueprint layer clean and prevents ownership-awareness from leaking into application code. Makes future topology changes (re-homing a register, adding a validator, adding a seed peer) transparent to blueprints. This is partly architectural hygiene; partly a precondition for Story 1 working without caller-side branching.

**Independent Test**: Write a blueprint action that calls the submission API with no conditional logic. Run it on a node that owns the register — observe local sealing. Move the register's ownership to a different node and rerun — observe forwarding with no code change in the blueprint.

**Acceptance Scenarios**:

1. **Given** a blueprint action handler that calls the submission API once per transaction, **When** the register is locally owned, **Then** the transaction is processed by the local validator and no peer forwarding is attempted that wouldn't also happen for any owner-produced transaction.
2. **Given** the same blueprint action handler, **When** the register is not locally owned, **Then** the transaction reaches the owner's validator via peer distribution without any branching in the handler.

---

### Edge Cases

- **Empty roster / pre-086 registers**: Legacy registers without a `validators` field in the control record must not break the derivation. Such registers fall back to treating the genesis proposer as the sole validator (matching existing fallback behaviour).
- **Roster rotation in flight**: A transaction signed under the old roster that arrives after a rotation docket is finalised must either be validated under a clearly documented grace-period rule or rejected explicitly — not silently lost.
- **Network with a single peer**: PingPongN1-style topologies have exactly one source peer. The network-height quorum rule must degrade gracefully — a single trusted peer's advert is sufficient when no second peer is available, but the state must clearly flag the reduced confidence.
- **Owner attestation without matching local wallet**: A node reading a control record whose Owner attestation is a DID not resolvable to any local wallet reports `IsOwner = false` (plain subscriber) rather than failing.
- **Clock skew on advert timestamps**: Peer adverts carry timestamps. A peer with clock skew producing adverts stamped far in the future (or past) must not be able to poison the network-height high-water-mark indefinitely; a bounded freshness check and recorded peer identity on the high-water-mark are required.
- **Governance transaction sealed but not yet replicated**: If an owner seals a roster change but a subscriber has not yet pulled that docket, the subscriber still reports the old relationship. This is correct (the subscriber acts on what it knows), but the operator should be able to see "last observed control-record version" to detect staleness.
- **Submission arrives at a node that is itself in the `Indeterminate` state**: The node should still accept the submission into peer-gossip (it can't validate, but someone else will); validator-local processing is skipped.

## Requirements *(mandatory)*

### Functional Requirements

**Register Local Relationship**

- **FR-001**: The platform MUST provide a way to compute, for any register held on a node, a structured relationship record indicating which of the following roles the node holds: Owner, Admin, Auditor, Designer, Validator, Subscriber. These are not mutually exclusive — a single node may hold multiple roles on the same register.
- **FR-002**: Role determination MUST be derived solely from the latest-sealed control record on the register plus the node's local wallet and validator key identifiers. No local flag or configuration may override what the control record says.
- **FR-003**: The relationship record MUST be cacheable per register and MUST be invalidated whenever a control transaction is sealed into a new docket on that register. Non-control transactions MUST NOT invalidate the cache.
- **FR-004**: The relationship record MUST be computable at node startup, when the node first learns of a register (subscribe / initial pull complete), and on any subsequent docket seal that contains a control transaction.

**Sync State**

- **FR-005**: The platform MUST replace the free-text `SyncState` value on the Register with a typed, enumerated sync-state value. The set of states is: `Indeterminate`, `Syncing`, `Caught-up`, `Error`.
- **FR-006**: The sync-state value MUST be derived from three inputs: (a) the register's local docket height, (b) the high-water-mark network-height reported by recent peer adverts, (c) the time since the most recent advert for this register.
- **FR-007**: The sync-state transition `Syncing → Caught-up` MUST require that the local docket height has matched the network-height high-water-mark for at least one confirming advert from a second distinct peer, OR — when only one source peer is known — that the single peer's advert is within the staleness window. The state MUST record which of these two conditions was met for operator visibility.
- **FR-008**: A register whose most recent advert is older than the staleness window MUST degrade to `Indeterminate` regardless of prior state.
- **FR-009**: The sync-state and the structural inputs that feed it (local height, known network-height, time of last advert, advert-source count) MUST be observable via the existing register query endpoints so that operators can see why the system is in the state it reports.

**Cross-Service Intake**

- **FR-010**: The peer service MUST push into the register service, for every advert it ingests, the advertised network-height value, the advertising peer's identity, and the advert timestamp. No register-service logic shall directly depend on peer-service internal state; the relationship is purely a push of observations.
- **FR-011**: The validator service MUST push into the register service, on docket seal and on mempool count change, the sealing-progress indicators (last-sealed docket height, mempool depth). This feed is only required when the node is the validator for the register.
- **FR-012**: Neither the peer service nor the validator service shall be authoritative for sync state or for the relationship record. Both must read these from the register service if they need them (for example, validator startup enrolment reads the relationship to determine which registers to monitor).

**Validator Enrolment**

- **FR-013**: The validator service MUST populate its monitoring enrolment by querying the register service for the list of registers on which the local validator key appears in the roster. This query MUST happen at startup and on every notification that a register's relationship has changed.
- **FR-014**: The validator's transaction-receipt endpoint MUST NOT cause a register to be added to the monitoring enrolment as a side effect. Submissions for registers that the node does not validate are accepted into the mempool (for forwarding) but produce no local docket-sealing work.
- **FR-015**: When a governance transaction removes a validator key from a register's roster, the affected validator MUST stop starting new sealing work for that register within one validation tick after the removing docket is finalised locally, and MUST drain (allow to complete) any in-flight sealing already begun.

**Submission Fan-Out**

- **FR-016**: The blueprint action submission path MUST be ownership-agnostic — a single call that results in the transaction being processed by the validator that can seal it, regardless of where that validator runs.
- **FR-017**: The peer service MUST accept submissions for any register and forward them toward a validator that can seal them, using the existing peer-to-peer distribution mechanism. For registers where the local node is a subscriber, the owner / roster peers are valid forwarding targets discovered via existing subscription state.
- **FR-018**: Accepting a submission into peer distribution MUST NOT require the local node to be the owner or a validator of the register.

**Notification & Lifecycle**

- **FR-019**: When a register's relationship changes (role added or removed for the local node), the register service MUST notify interested services (specifically the validator service) so that they can refresh their derived state without a full restart.
- **FR-020**: The relationship derivation MUST tolerate legacy registers predating Feature 086 (no `validators` field) by treating the genesis proposer key as the sole validator.

### Key Entities

- **RegisterLocalRelationship**: A derived value per (register, local-node-identity) pair capturing the set of roles the node holds on that register. Not persisted — re-derived from authoritative sources. Carries a version marker tied to the control-record docket height so consumers can tell if a cached copy is stale.
- **RegisterSyncState**: A typed enumeration (`Indeterminate`, `Syncing`, `Caught-up`, `Error`) attached to each register held on the node. Replaces the current free-text field. Derived; not a primary-truth store.
- **PeerHeightObservation**: An observation pushed from peer service into register service, recording one peer's advert for one register: the advertised height, the advertising peer identity, and the advert timestamp. Multiple observations per register are retained for quorum determination.
- **ValidatorSealingObservation**: An observation pushed from validator service into register service, recording the latest locally-sealed docket height and current mempool depth. One per register; overwrites.
- **RegisterControlRecord**: Unchanged from existing platform model — this feature consumes it read-only as the source of truth for the relationship derivation.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A subscriber node can complete a full action-transaction round-trip against a remotely-owned register within the PingPongN1 walkthrough's 120-second step window, flipping the walkthrough result from PARTIAL to PASS. Two consecutive round-trips succeed on the same register.
- **SC-002**: A node that boots holding a register reports its correct role set (Owner / Validator / Subscriber / combinations) within 5 seconds of startup, before any transaction traffic is received.
- **SC-003**: A governance transaction that adds or removes a validator roster entry is reflected in the affected node's monitoring enrolment within one validation tick after the docket is sealed locally — without a node restart.
- **SC-004**: An operator inspecting a lagging subscriber sees the sync state transition from `Caught-up` to `Syncing` within one advert interval of the owner producing a new docket that the subscriber has not yet pulled, and sees a numeric gap (dockets behind) alongside the state.
- **SC-005**: A node that has never been on a register's roster, when it accidentally receives a submission for that register, produces zero dockets for that register — verified by inspecting that register's docket chain from the owner and confirming no competing hash sequence appears.
- **SC-006**: No blueprint action handler needs a conditional branch on register ownership; the action-submission API is called identically whether the register is owned locally or remotely. Verified by code inspection of the blueprint service submission path.
- **SC-007**: A subscriber node running for 24 hours under steady-state traffic reports `Caught-up` for more than 99% of wall-clock time, with transitions to `Syncing` only during genuine pull-catchup phases.
- **SC-008**: The previously-merged auth fix (PR #357) remains sufficient for forward-direction replication — no new 401s appear in the peer-service log during normal operation after this feature lands.

## Assumptions

- The authoritative source of role ownership and validator membership is the on-chain control record. No alternative trust-anchor mechanism (external identity provider, local config flag) is in scope.
- The existing peer-to-peer gossip infrastructure is sufficient for forwarding submissions. A full gRPC-tunnel through the heartbeat channel is a possible optimisation but not required for this feature to deliver its P1 value.
- Staleness window for peer adverts is taken as 60 seconds by default, matching the existing heartbeat interval tolerance. Operators may tune.
- Quorum for network-height `Caught-up` confirmation is 2 distinct peers by default, degrading to 1 when only one source peer is known (with an explicit soft-state flag in the response). This matches the PingPongN1 topology while not preventing multi-peer deployments from demanding tighter confidence.
- Multi-owner scenarios (more than one node simultaneously holding Owner attestations on the same register) are treated as a valid platform state — each such node derives `IsOwner = true` independently. Any consequences of that concurrency (split-brain writes, divergent sealing) are existing platform concerns not addressed by this feature.
- Legacy pre-086 registers without a `validators` field remain supported via the existing genesis-proposer-as-sole-validator fallback, with no intention to retroactively migrate them.
- Governance transactions that change the roster are assumed to be idempotent, replay-safe, and already validated upstream — this feature only consumes them at read time.
- The register service is assumed to already hold, or be able to hold, the full control record needed for relationship derivation locally. If the control record is encrypted or lives only in a remote cache, relationship derivation is deferred until the record is accessible locally (no synchronous remote fetch during a submission's hot path).

## Dependencies

- **Authoritative roster availability**: Depends on the existing genesis control-record extraction path (Feature 086 + PR #357) being reliable. This feature assumes a node can read its own register's control record without failure.
- **Peer advert format**: Reuses the existing advert schema from peer heartbeats. No new fields are introduced; existing height and timestamp fields are sufficient.
- **Validator key identity**: Depends on the validator service being able to report the public key under which it signs dockets so that the register service can match it against roster entries. This is today derivable from the validator's system wallet provider.
