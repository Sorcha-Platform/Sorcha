# Feature Specification: Real register governance — organisation-signed control transactions and blueprint-executed multi-party quorum

**Feature Branch**: `189-org-signed-governance`

**Created**: 2026-08-06

**Status**: Draft

**Input**: User description: "Real register governance: organisation-signed control transactions and blueprint-executed multi-party quorum."

## Why this exists

A Sorcha register is meant to be governed by the organisations that own it. Today it is not governed by anyone.

Every governance action — changing a register's cryptographic posture, adding or removing an administrator, transferring ownership, rotating a validator key — is signed by the **node** the request happens to reach, not by the **organisation** exercising the authority. A register's roster of authorised organisations is captured immutably when the register is created, and the node is never on it. So the ledger correctly refuses the change, and no governance action can complete on any register that has finished being created.

The failure is silent. The request returns success, and nothing happens. It was found only by running it against a live node.

Meanwhile the platform already ships a governance *workflow definition* describing exactly the intended behaviour — propose, collect approvals from stakeholders, enact — which nothing executes. Organisations reading it would reasonably believe consortium governance works.

This feature makes governance real: an organisation signs with its own governance authority, multiple organisations can jointly authorise a change under an agreed rule, and the whole process is enacted through the platform's own workflow engine so it is visible and auditable on the ledger like any other business process.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A single organisation governs the register it owns (Priority: P1)

An organisation owns a register. An administrator changes a governed setting — for example promoting the register from its permissive development posture to full encryption. The organisation authorises the change with its own governance authority; the change is recorded permanently on the ledger and takes effect on every node that holds the register, including replicas belonging to other installations.

**Why this priority**: Nothing else works until this does. Every other governance operation — roster changes, validator key rotation, ownership transfer, consortium voting — travels the same path and is blocked by the same defect. This alone restores governance for the overwhelmingly common single-owner case and is independently valuable.

**Independent Test**: Create a register owned by one organisation, change a governed setting, and confirm the change is recorded on the ledger and observable on a second node holding a replica.

**Acceptance Scenarios**:

1. **Given** a register owned by one organisation with its creation complete, **When** an administrator of that organisation changes a governed setting, **Then** the change is permanently recorded on the ledger and reported as effective only once recorded.
2. **Given** the same change, **When** a second node holding a replica of that register receives the record, **Then** that node reflects the changed setting without any separate instruction.
3. **Given** a party that is not on the register's governance roster, **When** it attempts the same change, **Then** the change is refused and nothing is recorded.
4. **Given** a governed setting that may only move in one direction, **When** any party attempts to move it back, **Then** the attempt is refused.
5. **Given** a governance change has been submitted but not yet recorded, **When** the setting is queried, **Then** it still reports its previous value — a submitted change is never reported as an effective one.

---

### User Story 2 - A consortium jointly governs a shared register (Priority: P2)

Several organisations jointly own a register. Any one of them can propose a governance change, but the change does not take effect until the agreed proportion of stakeholder organisations have each approved it with their own authority. Where the register requires unanimity, every stakeholder must approve.

**Why this priority**: This is the reason the platform exists — multi-participant data flow where no single party can unilaterally alter the shared rules. It depends on User Story 1 being correct, and delivers the consortium guarantee that single-owner governance cannot.

**Independent Test**: Create a register owned by three organisations requiring unanimous approval, propose a change, and confirm it is enacted only after the third approval and not before.

**Acceptance Scenarios**:

1. **Given** a register owned by several organisations under a unanimous approval rule, **When** a proposal has fewer approvals than required, **Then** the change is not enacted and the proposal remains open.
2. **Given** the same proposal, **When** the final required approval is given, **Then** the change is enacted and recorded on the ledger.
3. **Given** an approval offered by an organisation that is not on the roster, **When** approvals are counted, **Then** that approval does not count toward the requirement.
4. **Given** an organisation that has already approved a proposal, **When** it approves the same proposal again, **Then** the requirement is not advanced by the repeat.
5. **Given** a register under a simple-majority rule, **When** a majority approves, **Then** the change is enacted without waiting for the remainder.
6. **Given** a proposal to transfer ownership, **When** the current owner is the proposer, **Then** the proposal still requires approval — ownership can never be transferred unilaterally.
7. **Given** an open proposal, **When** the register's roster is changed by a separate enacted decision, **Then** the open proposal is invalidated with a recorded reason and cannot subsequently be enacted.
8. **Given** an open proposal under a unanimous rule with one stakeholder yet to approve, **When** that stakeholder is removed from the roster, **Then** the proposal does **not** become enactable — it is invalidated.

---

### User Story 3 - The governance process is visible and auditable (Priority: P3)

Anyone entitled to see the register can review what was proposed, which organisations approved it, when, and what was ultimately enacted — as a first-class record on the ledger, not as an application log.

**Why this priority**: Governance that cannot be audited is not governance. This falls out of enacting the process through the platform's own workflow engine rather than as bespoke behaviour beside it, so it is largely a consequence of doing User Story 2 properly — but it is separately testable and separately valuable to an auditor.

**Independent Test**: Complete a multi-party governance change, then reconstruct from the ledger alone who proposed it, who approved it, and what changed.

**Acceptance Scenarios**:

1. **Given** a completed governance change, **When** an entitled reader inspects the register, **Then** the proposal, each approval and the enactment are individually attributable to the organisations that made them.
2. **Given** a proposal that never reached its approval requirement, **When** an entitled reader inspects the register, **Then** the proposal and its outcome are still discoverable.
3. **Given** the governance workflow definition published by the platform, **When** compared against what actually happens, **Then** the recorded sequence matches the published definition.

---

### User Story 4 - Ownership of the system register can be transferred (Priority: P4)

The network's own system register — created by an offline ceremony rather than the normal path — can have its ownership transferred to a different organisation through the same governance process as any other register.

**Why this priority**: This is the acceptance test for the whole feature. The system register is the most privileged object in the network and the one created most differently; if governance works on it, the model holds everywhere. It is last because it depends on all three preceding stories.

**Independent Test**: Transfer ownership of a system register created by the ceremony, and confirm the new owner can subsequently exercise governance that the previous owner cannot.

**Acceptance Scenarios**:

1. **Given** a system register created by the offline ceremony, **When** its ownership is transferred through the governance process, **Then** the transfer is recorded on the ledger and replicates to every node on the network.
2. **Given** a completed ownership transfer, **When** the previous owner attempts a governance change, **Then** the attempt is refused.

---

### Edge Cases

- **The roster changes while a proposal is open.** Resolved: the proposal is invalidated (FR-011b). A removed organisation's approval can therefore never carry weight, and a change cannot become enactable merely because the pool shrank. Removing a dissenting stakeholder does not pass their objection — it cancels the vote.
- **A proposal never reaches its requirement.** It must not remain open indefinitely, and an expired proposal must not later be enactable by a late approval.
- **An approver leaves the roster after approving.** Resolved by the same rule: their departure is itself a roster change, so the proposal is invalidated rather than carrying a stale approval forward.
- **Two roster changes are proposed at once.** Enacting the first invalidates the second, which must be re-raised against the new roster rather than applying to a roster that no longer exists.
- **Two proposals for conflicting changes are open at once.** Enacting one must not silently enact or invalidate the other without a recorded outcome.
- **The last remaining owner is removed.** A register must never be left with no organisation able to govern it.
- **A register created before this feature.** Its roster records the organisation's general-purpose identity rather than a distinct governance authority; such registers cannot be governed and must be identifiable as such rather than failing obscurely.
- **A node holds a replica but is not an owner.** It must apply governance decisions it receives but must never originate or authorise them.
- **A governance change is recorded but a node is offline.** On reconnection it must converge to the same governed state as every other node.

## Requirements *(mandatory)*

### Functional Requirements

**Authority and authorisation**

- **FR-001**: A governance change MUST be authorised by one or more organisations named on the target register's governance roster.
- **FR-002**: The system MUST refuse any governance change that is not authorised by a roster member, and MUST record nothing when refusing.
- **FR-003**: The system MUST NOT accept a node, service or infrastructure identity as the authorising party for a governance change.
- **FR-004**: An organisation's governance authority MUST be distinguishable from the credentials it uses for ordinary business activity, so that governance authority can be rotated or delegated without disturbing the organisation's identity.
- **FR-005**: The system MUST evaluate the authority of **every** party that authorises a change, not only the first.
- **FR-006**: The system MUST apply the same authorisation rules to every governance operation, with no operation exempt.

**Approval rules**

- **FR-007**: Each register MUST carry an approval rule stating what proportion of its roster must approve a governance change, chosen when the register is created.
- **FR-008**: The system MUST support requiring a simple majority, a supermajority, or unanimity of the roster.
- **FR-009**: A register owned by a single organisation MUST complete a governance change on that organisation's authority alone, with no additional approval step.
- **FR-010**: A transfer of ownership MUST require approval under the register's approval rule and MUST NOT be completed on the proposing owner's authority alone.
- **FR-011**: Only approvals from organisations on the roster MUST count toward an approval requirement, and repeat approvals by the same organisation MUST count once.
- **FR-011a**: A proposal MUST be evaluated against the roster and approval rule as they stood **when the proposal was raised**, so that neither the set of eligible approvers nor the number of approvals required can change while the proposal is open.
- **FR-011b**: Any enacted change to a register's roster MUST invalidate every proposal open on that register at that moment.
- **FR-011c**: An invalidated proposal MUST record its invalidation, and the reason, as a discoverable outcome — it MUST NOT be silently discarded, and MUST NOT be enactable thereafter.
- **FR-012**: A proposal MUST expire if it has not met its approval requirement within its validity period, and an expired proposal MUST NOT be enactable thereafter.

**Enactment and propagation**

- **FR-013**: A governance change MUST NOT take effect until it has been permanently recorded on the register's ledger.
- **FR-014**: The system MUST NOT report a submitted-but-unrecorded change as effective.
- **FR-015**: Once recorded, a governance change MUST take effect on every node holding the register, including nodes belonging to other installations, without further instruction.
- **FR-016**: A governed setting that is defined as one-way MUST NOT be reversible by any governance operation.

**Process, visibility and audit**

- **FR-017**: The governance process MUST be enacted through the platform's own workflow mechanism, so that it is governed by a published definition rather than by undisclosed behaviour.
  > **Scope, settled 2026-08-09.** "Workflow mechanism" means the published blueprint, its action
  > identities and its payload contracts — the things governance genuinely shares with every other
  > workflow. It does **not** mean a workflow *instance*: quorum is many organisations acting on one
  > step, and the instance model is linear by construction in three independent places (chain-fork
  > detection, `VAL_BP_002` role binding, and `InstanceProjection.OrderByChain`, which keys one
  > successor per predecessor). Forcing governance into an instance would require relaxing all three
  > for every workflow on the platform. See T055.
- **FR-018**: The published governance definition MUST match what the system actually does. Concretely,
  and exhaustively: the **action identities** the platform submits against (propose, collect quorum,
  record control transaction) MUST be the actions the definition declares, and the **payload contract**
  declared for each of those actions MUST be the payload the platform actually seals — enforced by the
  Validator, not merely documented.
  > **Restated 2026-08-09, narrowing a claim the previous wording could not support.** It previously
  > read "…including the approval rules it supports", which implied the definition's *routing* was
  > authoritative. It is not, and cannot be. The blueprint's route conditions referenced
  > `ownerOverride`, `requiresAcceptance`, `quorumMet` and `accepted` — variables **no producer emits
  > and nothing evaluates**, since governance transactions are exempt from `VAL_ROUTING_*` and are not
  > folded into an instance. `quorumMet` in particular is unknowable to the producer of an individual
  > approval, because it depends on how many sibling approvals have sealed. Those routes have been
  > removed rather than left as decoration: a definition that documents behaviour the system does not
  > have is worse than one that documents less, because a reader trusts it.
  >
  > The approval **rule** (`StrictMajority` / `Supermajority` / `Unanimous`) remains governed and
  > enforced — it lives in `RegisterControlRecord.RegisterPolicy.Governance.QuorumFormula` and is
  > frozen onto each proposal as `quorumFormulaAtRaise` (FR-011a), which is where a reader should look
  > for it. It was never expressible as a route condition.
- **FR-019**: Each proposal, each approval and each enactment MUST be individually attributable to the organisation responsible for it.
- **FR-020**: The proposal, its approvals and its outcome MUST be discoverable from the register itself by an entitled reader.
- **FR-021**: The set of governable operations MUST include changes to a register's cryptographic posture, in addition to roster and validator-key changes.

**Creation and continuity**

- **FR-022**: Register creation MUST establish the governance roster and approval rule, including for registers owned by several organisations, each authorising its own place on the roster.
- **FR-023**: Registers created before this feature MUST be identifiable as not governable, and MUST fail any governance attempt with a clear reason rather than an obscure error.
- **FR-024**: A governance change MUST NOT be able to leave a register with no organisation capable of governing it.

### External approval (added 2026-08-07 — see `docs/superpowers/specs/2026-08-07-governance-approval-surface-design.md`)

- **FR-025**: An approval for a multi-party register MUST be produced outside the platform's server-side trust boundary. The server MUST NOT be capable of producing such an approval on its own.
- **FR-026**: The value an approver signs MUST bind the **entire** operation being authorised, such that any subsequent change to that operation invalidates the signature. Binding a hand-selected subset of fields is insufficient.
- **FR-027**: An approver MUST be shown everything their signature binds. Signing an opaque value is not approval.
- **FR-028**: A signing request MUST NOT carry a precomputed digest; the signing client MUST derive it from the operation it displayed, so the two cannot disagree.
- **FR-029**: **Every** approval MUST carry an accountability link to a named individual, distinct from the organisation, so the ledger can answer *which person* stands behind a change. An autonomous approver is not an exception: a machine external to the platform was empowered by a human, so the link is delegated rather than absent.
- **FR-033**: A delegation empowering an autonomous approver MUST be signed by the empowering individual's own key. A server-asserted claim is insufficient — the server mints tokens, so a delegation it can assert is one it can forge, which defeats the purpose of moving signing outside it.
- **FR-034**: A delegation MUST carry a scope and an expiry, and MUST be revocable. Its validity MUST be determinable from sealed ledger content, so every node reaches the same answer (R-009).
- **FR-035**: The public key that produces an accountability signature MUST be provably owned by the individual it names. A key that merely accompanies a claimed identity is a self-declaration, not evidence, and would let anyone sign in a colleague's name with a perfectly valid signature.
- **FR-030**: The record MUST carry how the signing key was held, so a register may require a minimum standard for a given operation.
- **FR-031**: A single-owner register MUST continue to complete governance unattended, without pairing, device or human interaction.
- **FR-032**: An approval submission carrying an invalid or mismatched individual co-signature MUST be refused outright. It MUST NOT be accepted with the co-signature silently discarded.

### Key Entities

- **Register**: The shared, replicated record being governed. Carries its governed settings, its governance roster and its approval rule, established when it is created and thereafter changeable only through governance.
- **Governance Roster**: The set of organisations authorised to govern a register, each with a role (such as owner or administrator) and a governance authority against which their approvals are checked. Fixed at creation and changed only by a governance decision.
- **Approval Rule**: The proportion of the roster required to approve a change — simple majority, supermajority, or unanimity — held per register.
- **Governance Proposal**: A requested change to a register, raised by a roster member, carrying what is to change and accumulating approvals until it meets its approval rule, expires, or is withdrawn.
- **Approval**: One organisation's authorisation of a specific proposal, attributable to that organisation and countable exactly once.
- **Enacted Change**: The permanent ledger record of a governance decision that has taken effect — the authority for the register's governed state on every node.
- **Organisation Governance Authority**: The credential an organisation uses to authorise governance, distinct from the credentials it uses for ordinary business activity.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An organisation that owns a register can change a governed setting and observe it in effect on every node holding that register, with no manual intervention on those other nodes.
- **SC-002**: 100% of governance changes offered by a party outside the register's roster are refused, and leave no trace on the ledger.
- **SC-003**: A register requiring unanimity does not enact a change while any stakeholder has yet to approve, and enacts it once the last approval is given — verified with at least three stakeholder organisations.
- **SC-004**: Every enacted governance change can be traced, from the ledger alone, to the specific organisations that proposed and approved it.
- **SC-005**: Ownership of the network's system register can be transferred, after which the former owner can no longer govern it and the new owner can.
- **SC-006**: Zero governance changes take effect without a corresponding permanent ledger record — a change that is not recorded is never observable as effective.
- **SC-007**: A single-owner register requires no more steps to govern than it does today.
- **SC-008**: A governance change that is refused, or a proposal that expires, reports a reason a register administrator can act on without inspecting system internals.
- **SC-009**: Every acceptance scenario above is demonstrated by live execution against a running multi-node network, not by automated tests alone.
- **SC-010**: No change to a register's roster can cause an open proposal to be enacted — demonstrated by removing the sole outstanding approver from a unanimous proposal and confirming the change is invalidated rather than enacted.

## Assumptions

- **Evidence standard.** A passing automated test suite is explicitly *not* sufficient evidence for this feature. Every defect motivating it was invisible to a large green suite and surfaced only under live execution, including one that passed for the wrong reason because it exercised a timing window rather than the intended behaviour. Acceptance requires live verification on a multi-node network, confirming that governance records are permanently sealed into the ledger rather than merely stored, and that the resulting state converges across nodes.
- **Clean break, no compatibility window.** Because a register's governance roster is fixed permanently when the register is created, this feature applies only to registers created after it ships. Existing registers — including any existing system register — carry the older form of authority and must be recreated to be governable. This is accepted deliberately: the platform is pre-release, networks are recreated routinely, and carrying a dual-authority compatibility path forward would be a permanent ambiguity in the security model.
- **Existing approval machinery is reused, not rebuilt.** The platform already models roster membership, voting pools, approval-rule arithmetic (including unanimity), and a single-owner shortcut. The feature wires these into a working process rather than introducing a second, parallel mechanism.
- **Register creation already supports consortia.** Creating a register already accepts several owners — each authorising their own place on the roster — plus additional administrators and a per-register approval rule. No change to the creation contract is assumed necessary to support consortium governance.
- **Proposal validity period.** Proposals are assumed to expire after a bounded, configurable period, defaulting to a value appropriate for human approval cycles rather than machine timescales. Expiry is recorded as an outcome rather than leaving the proposal silently abandoned.
- **Single-owner behaviour is preserved.** The existing shortcut, whereby a sole owner's own authority satisfies the approval requirement for everything except transferring ownership, is retained. Consortium governance is the general case; single-owner is its degenerate case, not a separate path.
- **Scope includes register creation as a governed process.** Creating a register is treated as the first governance act on it, following the same published definition as later changes, so that there is one way registers come into being and one way they change.
- **Out of scope.** Delegating an organisation's governance authority to another party; rotating a governance authority after creation; governance across registers (a decision on one register affecting another); and any user interface beyond what is needed to demonstrate the acceptance scenarios.

## Clarifications

### Session 2026-08-06

**Q: When the governance roster changes while a proposal is open, are approvals counted against
the roster as it stood when the proposal was raised, or as it stands when the count is taken?**

**A: Snapshot at proposal time, and any change to the roster invalidates every open proposal on
that register.** (Resolved by the maintainer; see FR-011a–FR-011c.)

Rationale for the record. Two alternatives were rejected:

- *Counting against the live roster* would let a proposal become enactable purely because the
  roster shrank — under unanimity, removing a dissenting organisation would convert a blocked
  change into an enacted one. That makes roster removal an attack on any open proposal.
- *Counting against the snapshot but leaving the proposal open* avoids that, but lets an
  organisation that has since lost its authority still determine the outcome — awkward precisely
  when it was removed *because* it should no longer be deciding things.

Invalidation avoids both: no departed member's approval can carry weight, and no change in the
size of the pool can flip an outcome. The cost is accepted — a roster change cancels open
proposals on that register and they must be raised again — because "the roster changed, so the
proposal was cancelled and re-raised" is a sequence an auditor can follow, whereas either
alternative produces an outcome that is hard to explain after the fact.
