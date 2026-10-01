# Feature Specification: System Blueprint Lifecycle

**Feature Branch**: `197-system-blueprint-lifecycle`

**Created**: 2026-10-01

**Status**: Draft

**Input**: Issue #1466. Approved design: `docs/superpowers/specs/2026-10-01-system-blueprint-lifecycle-design.md`
(authoritative — this spec restates its decisions as testable requirements).

---

## Context

The platform's own workflow definitions — register creation, register governance, organisation
creation, joining a private register — are published once onto the network's system register and read
from there by every node. Four things about how they change over time are wrong, and every one of them
fails **silently**:

1. A node never updates them. Startup seeding only checks whether a definition *exists*, so a node keeps
   the definitions it had on genesis day while its software ships newer ones. On the public node this
   refused every governance proposal on every register, behind an "accepted" response, until an operator
   republished by hand.
2. Which publication is "current" is decided by a timestamp the publisher chose and nobody signed.
3. Governance changes are judged by whatever governance definition is newest *at the moment each step is
   checked*. A legitimate upgrade therefore breaks every governance change already in progress.
4. After a republish, validators keep using their cached copy, and nothing tells a node that received
   the republish by replication.

Separately, any platform administrator on the publishing node can redefine a network-wide definition.

**Decisions already made** (design §2): upgrades are an explicit operator act, never automatic; drift
must be visible; a governance change keeps the definition it was raised under; only a system
administrator on the node holding the network's publishing key may publish; the definition published is
the one shipped in that node's software, never a hand-supplied body.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator can see when a node's system definitions disagree with the network (Priority: P1)

An operator running any node — the publishing node or a node that only replicates — can see, for each
system definition, whether the version shipped in the node's software matches the version currently in
force on the network, and if not, which side is out of date.

**Why this priority**: The outage behind #1466 lasted because nothing reported the disagreement. Being
able to see drift is the minimum that prevents a repeat, and it is safe on its own — it changes no
behaviour.

**Independent Test**: Start a node whose software carries a definition different from the one on the
network and confirm the node reports the drift (with its direction) through its log, its health status,
its metrics and an operator query; start a node whose software matches and confirm it reports in sync.

**Acceptance Scenarios**:

1. **Given** a node whose shipped definition is identical to the network's current one, **When** the node
   starts, **Then** that definition is reported *in sync* and the node's health is unaffected.
2. **Given** a node whose shipped definition equals an **older** publication on the network, **When** the
   node starts, **Then** it is reported *image behind*, the matching older version is named, and the node
   reports a degraded (not failed) health status.
3. **Given** a node whose shipped definition has **never** been published, **When** the node starts,
   **Then** it is reported *image ahead* (an operator publish is pending) and health is degraded.
4. **Given** two publications of one definition where the one sealed later carries an **earlier**
   timestamp, **When** the current version is determined, **Then** the later-sealed publication is
   current.
5. **Given** a replicating node that has not finished syncing the system register, **When** a definition
   is not yet present, **Then** it is reported *missing* without degrading health.

---

### User Story 2 - A governance change keeps the rules it was raised under (Priority: P1)

A governance change on any register (adding a member, transferring ownership, changing policy) is judged
from start to finish by the governance definition that was in force when it was raised. Upgrading the
governance definition mid-way neither breaks it nor changes its rules.

**Why this priority**: Without this, the upgrade that User Story 3 enables would itself recreate the
#1466 outage for every governance change in flight — the fix would cause the failure.

**Independent Test**: Raise a governance change, publish a new governance definition, then approve and
enact the change; confirm it is judged by the original definition, and confirm the same approval judged
against the new definition would have been refused.

**Acceptance Scenarios**:

1. **Given** a governance change raised under definition version N, **When** version N+1 is published and
   the change is then approved, **Then** the approval is judged against version N and the change enacts.
2. **Given** the same approval, **When** it is judged against version N+1 instead, **Then** it is refused —
   proving the recorded definition, not the newest one, decided the outcome.
3. **Given** a member attempts to raise a governance change under a superseded definition, **When** it is
   validated, **Then** it is refused with a reason naming the superseded definition.
4. **Given** a governance change whose recorded definition cannot be found by a node, **When** that node
   validates it, **Then** it is refused — never judged against a different definition.
5. **Given** a governance change raised before this feature (recording no definition), **When** it is
   validated, **Then** it is judged against the current definition and the fallback is counted.

---

### User Story 3 - An operator upgrades a system definition safely (Priority: P2)

A system administrator on the network's publishing node can publish the system definition shipped in that
node's software, preview the effect first, and be protected from rolling back by accident or from racing
another operator. Everyone else is refused, and every refusal is recorded.

**Why this priority**: This replaces the by-hand remediation used on the public node (extracting a file
from a container and posting it). It depends on User Story 1's notion of drift and is made safe by User
Story 2.

**Independent Test**: As a system administrator on the publishing node, preview then publish a pending
definition and confirm it becomes current on every node; repeat as other identities and on a non-publishing
node and confirm each is refused and recorded.

**Acceptance Scenarios**:

1. **Given** a definition reported *image ahead*, **When** a system administrator previews a publish,
   **Then** the current and candidate versions are shown and nothing is submitted.
2. **Given** the same, **When** they publish, **Then** the shipped definition becomes current and the node
   then reports it *in sync*.
3. **Given** a definition already *in sync*, **When** a publish is requested, **Then** nothing is submitted
   and the response says it is already current.
4. **Given** a definition reported *image behind*, **When** a publish is requested, **Then** it is refused
   because it would roll the network back.
5. **Given** the operator states which version they expect to be current and another publication has
   since landed, **When** they publish, **Then** it is refused.
6. **Given** an organisation administrator, a consumer-tier caller, or a system administrator on a node
   that does not hold the publishing key, **When** any of them requests a publish, **Then** it is refused
   with a reason and the refusal is recorded in the audit log.
7. **Given** a request that names no definition shipped in the node's software, **When** it is made,
   **Then** it is rejected as not found.

---

### User Story 4 - An upgrade takes effect on every node with no manual steps (Priority: P2)

Once a new system definition is sealed, every node — including one that received it only by replication —
starts using it for new work without an operator clearing caches or restarting services.

**Why this priority**: The #1466 remediation needed a cache key deleted and a service recreated on each
node. An upgrade that only works on the node that issued it is not an upgrade.

**Independent Test**: Publish a new definition on the publishing node and confirm a replicating node both
reports *in sync* and validates newly raised governance changes against the new definition, with no
operator action on that node.

**Acceptance Scenarios**:

1. **Given** a new definition sealed on the network, **When** a replicating node receives it, **Then** its
   validator stops serving the previous copy for "current" lookups without a restart.
2. **Given** that, **When** a governance change is next raised on any register, **Then** it records the new
   definition.

---

### Edge Cases

- Two publications of one definition are sealed in the same block: order within the block decides which is
  current.
- An operator publishes byte-identical content: treated as already current; no duplicate publication.
- An operator wants to roll back to an exact earlier body: refused; a rollback must be a distinct, corrected
  body.
- A replicating node lags behind and is asked to validate a governance change pinned to a definition it has
  not received: refused (fail closed); the change must be re-raised once the node has caught up.
- A single transaction that both raises and enacts a governance change (owner override): judged by the
  definition it records itself.
- The system register is unreachable when drift is computed: reported as unknown for that cycle, never as
  in sync.
- A definition read from the ledger does not reproduce its own identity: refused, never used.

## Requirements *(mandatory)*

### Functional Requirements

**Currency**

- **FR-001**: The current publication of a system definition MUST be the one sealed latest in ledger order
  (block, then position within the block). Publisher-supplied timestamps MUST NOT influence it.
- **FR-002**: A definition's version number MUST be its ordinal position in that ledger order and is for
  display only; identity remains the publication itself.

**Drift**

- **FR-003**: Every node, whether it publishes or only replicates, MUST classify each system definition
  shipped in its software as *in sync*, *image behind*, *image ahead* or *missing* at startup and
  periodically thereafter.
- **FR-004**: For *image behind*, the node MUST identify which earlier version the shipped definition
  matches.
- **FR-005**: Drift MUST be surfaced through the node's log, its health status (degraded, never failed),
  a metric per definition and state, and an operator query restricted to system administrators.
- **FR-006**: *Missing* on a replicating node that has not finished syncing MUST NOT degrade health.
- **FR-007**: Startup seeding MUST publish only definitions that are *missing*, and only on the
  publishing node. Nothing MUST republish automatically.

**Operator publish**

- **FR-008**: Publishing a system definition MUST be restricted to system administrators holding a
  platform-tier session, on a node holding an active publishing key on the system register's validator
  roster. The previous publish operation, which accepted any register manager and an arbitrary body, MUST
  be removed.
- **FR-009**: The published definition MUST be the one shipped in that node's software; a request MUST NOT
  be able to supply the definition's content.
- **FR-010**: A publish MUST support a preview mode that submits nothing and reports current and candidate
  versions.
- **FR-011**: A publish MUST be refused when the definition is already current (reported as a no-op), when
  it would roll the network back, or when the operator's stated expected-current version is not current.
- **FR-012**: Every refused publish MUST be recorded in the audit log with its reason, and every successful
  publish MUST be recorded with the previous and new version and the publishing node.
- **FR-013**: The command-line tool MUST offer drift reporting and publishing (including preview and
  expected-current) with the same rules.

**Governance pinning**

- **FR-014**: A newly raised governance change MUST record, inside its signed content, the governance
  definition current when it was raised.
- **FR-015**: Approvals and the enactment of a governance change MUST be judged against the definition
  recorded by the change they belong to. A change that both raises and enacts in one step is judged by
  the definition it records.
- **FR-016**: A single rule MUST determine which definition governs a governance step; every validation of
  governance steps MUST use it.
- **FR-017**: A governance change raised under a definition that is not current MUST be refused with a
  dedicated reason.
- **FR-018**: A recorded definition that a node cannot resolve MUST cause refusal; the node MUST NOT fall
  back to a different definition. Resolution MUST look for system definitions on the system register even
  when the governance step belongs to another register, and MUST reject content that does not reproduce
  its own identity.
- **FR-019**: A governance change recording no definition (raised before this feature) MUST be judged
  against the current definition, and each such fallback MUST be counted.
- **FR-020**: The governance audit view of a change MUST show which definition (identity and version)
  governs it.
- **FR-021**: The governance definition's declared contract MUST include the recorded-definition field,
  kept in step with what producers emit.

**Propagation**

- **FR-022**: When a new system definition is sealed, every node's validator MUST stop serving the
  previous copy for "current" lookups without manual action, including nodes that received it by
  replication.

### Key Entities

- **System definition**: a platform-owned workflow definition published on the system register
  (register creation, register governance, organisation creation, joining a private register).
- **Publication**: one sealed publishing of a definition; its identity is derived from its content;
  ordered by ledger position.
- **Drift report**: per node and definition — state, current publication and version, the node's shipped
  publication identity, and the matching earlier version when behind.
- **Governance change**: a proposal, its approvals, and its enactment; the proposal records the governing
  definition.
- **Publishing key**: the system register's validator-roster entry authorised to publish definitions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a node whose shipped definitions disagree with the network, 100% of the disagreeing
  definitions are reported with the correct direction within one reporting cycle of startup.
- **SC-002**: A governance change raised before a governance-definition upgrade enacts after it, in a live
  run on the public node and its replica, with zero manual steps.
- **SC-003**: After an upgrade, a replicating node reports *in sync* and newly raised governance changes
  record the new definition, with zero operator actions on that node.
- **SC-004**: Every publish attempt by an unauthorised identity or on a non-publishing node is refused and
  appears in the audit log — 0 unrecorded refusals across the test matrix.
- **SC-005**: The counter of governance changes judged without a recorded definition reads zero for changes
  raised after deployment.
- **SC-006**: Each guard (ledger ordering, pin enforcement, cache propagation) has a test that fails when
  the guard is removed.

## Assumptions

- Pre-release: the old publish operation is removed without a transition period; historical specs that
  mention it are left unchanged.
- The publishing key stays in the node's custody; moving it offline is #1380's concern.
- Governing the system register itself (#1464, #1465) and service-scope enforcement (#1393) are out of
  scope; so is any automatic republish.
- Live-testing a changed system definition requires building software that contains it (consequence of
  publishing from shipped content only).
- Rollout order is validator first, then register service, then the operator publish. Whether the register
  service may record the definition before the new governance definition is published depends on whether
  the currently published one tolerates unknown fields — to be settled in planning before any code.
- Whether instances of a system-register-published workflow executed on another register are currently
  affected by the same resolution gap (FR-018) is to be confirmed in planning; if so this feature fixes it.
- Drift reporting cycle: every 10 minutes after startup (design default).
