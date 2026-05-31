# Feature Specification: Ledger-Derived Workflow Instances

**Feature Branch**: `145-ledger-derived-instances`  
**Created**: 2026-05-31  
**Status**: Draft  
**Input**: User description: "Ledger-Derived Workflow Instances — make a workflow instance a deterministic projection of the sealed register (single shared state machine, no mirrors, one async submission path, carried+attested routing decisions, idempotent role-gated reactions). Based on the approved design at docs/superpowers/specs/2026-05-31-ledger-derived-instances-design.md"

## Overview

A workflow instance today is independently-stored, per-node mutable state that each node edits in place. Across federated nodes this produces two divergent copies of "the same" workflow (an "origin" on the submitting node and a reconstructed "mirror" on the owner), two different submission behaviours, and a decision (which action comes next) that no node can reproduce from the shared ledger. The result is recurring cross-node breakage: participants can't act on the right node, autonomous agents are blind to pending work, credentials risk double-issue, and "is this workflow done?" is ambiguous.

This feature makes a workflow instance a **deterministic projection of the sealed register** — the register's sealed transactions are the only source of truth, and every node that holds the register computes the **same** instance state from them. There is one instance, the same on every node; one submission path; routing decisions that are carried on the ledger and trusted; and side effects that fire exactly once on the responsible node. The design and its rationale are in `docs/superpowers/specs/2026-05-31-ledger-derived-instances-design.md`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A cross-node workflow advances as one consistent, autonomous state machine (Priority: P1)

A workflow spans two installations: an applicant submits on one node; the register and its validator live on another; an approver (a person or an autonomous agent) acts on the owner node. Today this requires a reconstructed "mirror" and the approver's tooling can't even find the pending action. In the target, every node derives the **same** instance from the sealed ledger: the applicant submits, the owning node seals, and **both nodes project the workflow to the same next state**. The approver finds and acts on their action wherever they are, and the workflow runs to completion with no manual intervention and no node-specific workaround.

**Why this priority**: This is the entire point — a single shared state machine across nodes. It is the MVP; everything else hardens or extends it. It also directly removes the class of cross-node failures observed repeatedly in the field.

**Independent Test**: Run a two-node credential-issuance workflow (anonymous applicant on the subscriber, approver/agent on the owner). Verify the instance's control state (which action is current, which are complete) is identical on both nodes after each seal, the approver discovers and acts on its action autonomously, and the credential is delivered — with no mirror and no manual step.

**Acceptance Scenarios**:

1. **Given** an applicant submits the starting action on a subscriber node, **When** the owning node seals it, **Then** both nodes independently project the same instance (same identity, same current action) from the sealed transaction.
2. **Given** the workflow has advanced to an action whose participant is on the owner node, **When** that participant (or their autonomous agent) lists their pending work on the owner node, **Then** the action appears and can be acted on, without the owner having originated the instance.
3. **Given** any action seals, **When** each node observes the sealed docket, **Then** each node advances the instance identically (no divergence between nodes).
4. **Given** a completed workflow, **When** instance state is read on any node, **Then** all nodes agree it is complete (no "complete vs still-running" ambiguity between nodes).

---

### User Story 2 - Side effects happen exactly once, on the responsible node (Priority: P2)

When a workflow action triggers a side effect — issuing a credential, delivering it to a wallet, sending a notification — that effect must happen exactly once, performed by the node responsible for it, even though every node observes the same sealed transaction and nodes restart or re-observe transactions.

**Why this priority**: Correctness of outcomes. Moving state to a shared projection means many nodes see the same seal; without disciplined side-effect handling that becomes duplicate credentials/notifications. High value, but it layers on the P1 projection.

**Independent Test**: Trigger a credential-issuing action; confirm exactly one credential is minted and delivered. Replay the sealed transaction and restart the responsible node; confirm still exactly one credential (no duplicates), and that non-responsible nodes perform no side effect.

**Acceptance Scenarios**:

1. **Given** an action that issues a credential, **When** it seals and replicates to multiple nodes, **Then** only the entitled node mints + delivers it, exactly once.
2. **Given** the responsible node restarts and re-observes the sealed transaction, **When** it re-processes, **Then** no duplicate side effect is produced.
3. **Given** a node that does not host the responsible wallet, **When** it observes the same sealed transaction, **Then** it performs no side effect.

---

### User Story 3 - Routing decisions (including parallel branches) are trusted ledger facts (Priority: P2)

The decision about which action(s) come next must be recorded on the ledger as a fact every node can read and trust, including workflows that fan out into multiple parallel branches — rather than being recomputed differently per node or collapsed to a single branch.

**Why this priority**: It is the data foundation the projection reads, and it fixes the parallel-branch loss. Prioritised just below the headline because the P1 loop depends on it, but it is independently verifiable.

**Independent Test**: Submit an action whose route fans out to multiple next actions; verify the sealed transaction carries the full next-action set and every node projects all branches. Attempt to submit a decision inconsistent with the workflow's route graph or improperly attested; verify it is rejected at seal.

**Acceptance Scenarios**:

1. **Given** an action with a multi-branch route, **When** it is submitted and sealed, **Then** the sealed transaction carries the complete set of next actions and all nodes advance all branches.
2. **Given** a submitted decision that does not correspond to a valid successor in the blueprint's route graph, **When** it reaches the sealing node, **Then** it is rejected and not sealed.
3. **Given** a register configured to require a stronger routing-trust level, **When** an action is submitted, **Then** the required attestation is enforced before seal.

---

### User Story 4 - Instance state is verifiable and rebuildable from the ledger (Priority: P3)

Because the instance is a projection, its stored form is a cache that can always be reconstructed from the sealed ledger, and any divergence between the cache and a fresh reconstruction can be detected and corrected — giving operators a recovery path and an integrity check.

**Why this priority**: Operability and trust hardening. Valuable for resilience and audit, but the core flows work without explicitly exercising rebuild.

**Independent Test**: Reconstruct an instance from the ledger and confirm it equals the stored state. Corrupt or delete the stored cache and confirm reconstruction restores the correct state.

**Acceptance Scenarios**:

1. **Given** any instance, **When** it is reconstructed from the sealed ledger, **Then** the reconstruction equals the stored state.
2. **Given** a corrupted or missing stored instance cache, **When** reconstruction runs, **Then** the correct state is restored from the ledger.
3. **Given** routine operation, **When** a periodic integrity check runs, **Then** any cache-vs-ledger divergence is surfaced.

---

### User Story 5 - One submission path; legacy duplication removed (Priority: P3)

Submitting an action behaves the same whether or not the submitting node owns the register — one path, one response contract — and the legacy constructs that created the duplication (mirror reconstruction, the dual synchronous/async branch, imperative state mutation, the topology-based ownership guess, the single-next-action shortcut) are removed and prevented from returning.

**Why this priority**: This is the consolidation/cleanup that locks in the model and removes the smell, but the behavioural wins are delivered by P1–P3; this guarantees they can't regress.

**Independent Test**: Submit the same action from an owner node and from a subscriber node; confirm identical response contract and identical resulting state (advanced via projection in both cases). Run the clean-break check and confirm no removed construct remains.

**Acceptance Scenarios**:

1. **Given** an owner node and a subscriber node, **When** each submits an action, **Then** both follow the same submission path and the same response contract, and neither mutates instance state directly.
2. **Given** the sealing node must be chosen, **When** a submission fans out, **Then** the choice is made by validator-roster membership, not by connection topology.
3. **Given** the codebase after this work, **When** the clean-break check runs, **Then** it finds no mirror reconstruction, dual-path branch, imperative mutation, topology heuristic, or single-next-action hint.

---

### User Story 6 - Presentation-driven advancement runs on the projection (Priority: P3)

Workflows that advance on a credential-presentation outcome (rather than a normal action submission) advance through the same projection, preserving the existing chain-integrity guarantees that prevent ordering races.

**Why this priority**: The presentation lifecycle is the most intricate flow to fold into the new model; it is isolated as its own slice so it can be migrated carefully without destabilising the core.

**Independent Test**: Drive a presentation-gated workflow to a terminal outcome; confirm the instance advances consistently across nodes via the projection and that no chain-ordering race occurs.

**Acceptance Scenarios**:

1. **Given** a presentation-gated action, **When** its outcome seals, **Then** the instance advances via the projection identically on every node.
2. **Given** the existing chain-ordering protections, **When** presentation outcomes advance the instance, **Then** ordering integrity is preserved (no out-of-order advancement).

---

### Edge Cases

- **Same sealed input, different arrival order / node restart**: projection must reach identical state regardless of docket arrival order or restarts (idempotent fold).
- **Abandoned draft**: starting an application but never submitting must leave no ledger footprint and no instance on any node.
- **Two applications from the same applicant**: each starting action yields a distinct, deterministic instance identity.
- **Open / late-bound applicant**: instance identity is stable regardless of who is late-bound; the binding is recorded from the sealed starting action.
- **Node cannot decrypt some action data**: it still projects control state (current/next actions) from the validated decision, but materialises only the data it is entitled to see; it performs no side effect it isn't entitled to.
- **Replayed or duplicate sealed transaction**: neither advances state twice nor fires a side effect twice.
- **Forged or inconsistent routing decision**: rejected at seal, never projected.
- **Stronger routing-trust requested by a register but unmet**: submission refused before seal.

## Requirements *(mandatory)*

### Functional Requirements

#### Instance as a ledger projection

- **FR-001**: An instance MUST be a deterministic projection of the sealed register transactions for that instance; given identical sealed transactions, every node holding the register MUST compute identical instance control state (current actions, completed actions, participant bindings).
- **FR-002**: There MUST be exactly one representation of an instance's state per node — no separate "origin" and "mirror" copies.
- **FR-003**: The stored instance MUST be a rebuildable materialized view; the system MUST be able to reconstruct an instance's state from the sealed ledger and MUST be able to detect divergence between the stored view and a fresh reconstruction.
- **FR-004**: The projection MUST be idempotent: re-observing an already-applied sealed transaction MUST NOT change state; folding MUST be order-independent for the same set of sealed transactions.

#### Instance identity

- **FR-005**: An instance's identity MUST be deterministically derived from its starting action's sealed transaction, so every node derives the same identity for the same workflow.
- **FR-006**: Starting an application MUST NOT write to the ledger; the instance is born on the ledger only when its starting action is submitted and sealed. The submitter MUST receive the canonical ledger-derived identity in the submission acknowledgement.

#### Routing as a carried, attested fact

- **FR-007**: Each action transaction MUST carry the complete set of next actions (the routing decision), preserving parallel/fan-out branches end-to-end.
- **FR-008**: The routing decision MUST be validated at seal time against the blueprint's static route graph and the sender's authorisation; an inconsistent decision MUST be rejected and never sealed.
- **FR-009**: The routing decision MUST carry a pluggable attestation. The default (v1) attestation is the authorised sender's signature. A register's governance policy MUST be able to declare a required attestation strength (default: sender-signed), enforced before seal.
- **FR-010**: Projecting instance control state MUST require only the validated routing decision — no node needs to decrypt action payload to advance control state.

#### Single submission path

- **FR-011**: Action submission MUST follow a single path regardless of whether the submitting node owns the register; the submitter MUST NOT directly mutate instance state. State advances only when the seal is observed (via the projection).
- **FR-012**: Submission MUST return a prompt acknowledgement containing the transaction reference and the instance identity. It MAY return the advanced state if the projection materialises within a short bounded wait; otherwise it returns an async acknowledgement that the caller resolves by observing instance advancement.
- **FR-013**: The node that seals MUST be selected by validator-roster membership (the authoritative register relationship), not by connection/seed topology.

#### Cross-node discovery

- **FR-014**: A participant (human or autonomous agent) MUST be able to discover and act on their current action on any node that holds the register, even if that node did not originate the instance.

#### Reactions (side effects)

- **FR-015**: Side effects (credential issuance, delivery, notifications, inbox writes) MUST be performed exactly once, by the entitled node (the node hosting the wallet responsible for the effect), idempotent on the sealed transaction and the effect kind.
- **FR-016**: A replayed or duplicated sealed transaction, or a node restart, MUST NOT produce a duplicate side effect.
- **FR-017**: A node that is not entitled for a given side effect MUST NOT perform it.

#### Presentation lifecycle

- **FR-018**: Presentation-driven advancement (outcome/abandonment) MUST advance the instance through the same projection while preserving existing chain-ordering integrity guarantees.

#### Clean break

- **FR-019**: The legacy mirror reconstruction, the dual synchronous/async submission branch, imperative instance-state mutation, the topology-based ownership heuristic, and the single-next-action hint MUST be removed.
- **FR-020**: An automated check MUST prevent reintroduction of the removed constructs.

#### Caller migration

- **FR-021**: Callers that depend on a synchronous next-action / issued-credential response (user interfaces, walkthroughs, the demo toolkit) MUST be migrated to resolve outcomes by observing instance advancement and credential availability.

### Key Entities *(include if feature involves data)*

- **Instance projection (materialized view)**: the per-node cache of the deterministic projection — ledger-derived identity, current actions, completed actions, participant→wallet bindings, and the disclosure-scoped data view. Reconstructable from the ledger.
- **Routing decision**: a fact attached to an action transaction — the completed action and the **full** set of next actions — plus an attestation.
- **Attestation**: the trust mechanism for a routing decision — sender-signed (v1); validator-re-evaluated and proof-based are future strengths sharing the same slot.
- **Reaction**: a node-role-gated, idempotent side effect keyed on (sealed transaction, effect kind), performed only by the entitled node.
- **Register routing-attestation policy**: a register-governance setting declaring the required attestation strength (default sender-signed).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Given identical sealed transactions, an instance's control state is identical on every node in 100% of checks (cross-node determinism).
- **SC-002**: A two-node workflow (applicant on one node, approver/agent on another) completes **fully autonomously** — the approver discovers and acts on its action with no manual step and no node-specific workaround — in 100% of runs.
- **SC-003**: Reconstructing any instance from the ledger matches its stored state in 100% of checks; a corrupted or deleted cache is restored by reconstruction.
- **SC-004**: Replaying or duplicating a sealed credential-issuing transaction across restarts yields exactly one credential (zero double-issues) in 100% of trials.
- **SC-005**: Parallel-branch workflows preserve every branch across all nodes (zero branch loss), versus the prior single-branch collapse.
- **SC-006**: Action submission returns the same response contract and reaches the same resulting state whether submitted from an owner or a subscriber node; no behaviour selects on connection topology.
- **SC-007**: A forged or route-graph-inconsistent routing decision is rejected before seal in 100% of attempts.
- **SC-008**: After completion, the clean-break check reports zero occurrences of the removed constructs, and the standing two-node demo runs green end-to-end with the autonomous approver.

## Assumptions

- **Pre-release, no data migration.** A clean break is acceptable; existing in-flight instances need not be preserved.
- **v1 attestation is sender-signed.** Stronger attestations (validator re-evaluation via a control-disclosure plane; zero-knowledge / universally-verifiable proofs) are out of scope here — only the pluggable attestation seam and the register governance policy field are delivered now, so they can be added later without re-architecting.
- **The sealing validator validates routing structurally**, not by decrypting payload values, in v1 (consistent with how submitters are already trusted for action data on encrypted registers).
- **Disclosure model unchanged.** A node materialises only the action data it is entitled to decrypt; control-state projection needs only the validated decision.
- **Bounded-wait default ~2–3s, configurable.** Tuning the synchronous-convenience window is an operational setting, not a scope question.
- **Existing platform primitives remain**: the validator roster, the authoritative register relationship, the cross-node replication transport, the cross-node credential-delivery binding, and the presentation-lifecycle chain-ordering protections.

## Dependencies

- The validator-roster model and the authoritative register relationship (who can seal / who owns).
- The cross-node replication transport that delivers sealed dockets to subscribers.
- The cross-node credential-delivery binding (holder-key-bound, encrypted-to-recipient delivery).
- The presentation-lifecycle and its chain-ordering protections (for the presentation slice).

## Out of Scope

- Stronger routing attestations (validator re-evaluation, zero-knowledge proofs) — seam + governance field only.
- A register-governance administration interface for the routing-attestation policy (the setting exists; its admin UX is later).
- Durable outbox-grade delivery guarantees for reactions (at-least-once + idempotent is sufficient now).
- Cross-installation peer gossip / multi-hop mesh routing (unchanged from current deferred scope).
- Preserving or migrating any existing in-flight instances (pre-release clean break).
