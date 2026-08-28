# Feature Specification: Validator Exemption Authority

**Feature Branch**: `196-validator-exemption-authority`

**Created**: 2026-08-28

**Status**: Draft

**Input**: Issue #1591. Design note: `docs/superpowers/specs/2026-08-28-validator-exemption-authority-design.md`

---

## Context

The validator waives six of its thirteen validation rules for administrative transactions —
action-schema validation, blueprint conformance (**including sender authorisation**),
routing-decision attestation, crypto policy, sequence replay, and fork detection. These waivers are
correct and necessary: genesis, governance and blueprint publication genuinely do not fit the
ordinary workflow rules, and two of the six are load-bearing for governance quorum.

The defect is **how the waiver is granted**. Today a transaction receives it by *claiming* to be
administrative, in a field nobody signed. A submitter chooses that field freely, and nothing then
checks whether they were entitled to what they claimed. One of the three claimable values happens to
be covered by a separate check that keys off the same string; the other two substitute nothing.

This feature replaces the **claimed** discriminator with a **proved** one. Nothing about what an
exemption does changes. Only who may obtain one changes.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A forged genesis claim is refused (Priority: P1)

An attacker who can submit a transaction to a node constructs one of their own, signs it with their
own key, and labels it as the network's genesis transaction — by either of the two routes that
label affords. The node must refuse the exemptions, because the attacker cannot produce a signature
from the network's genesis ceremony key.

**Why this priority**: This is the widest hole. Genesis waives sender authorisation itself, and
unlike the governance value it substitutes no compensating check. Both of its routes grant the same
six waivers, so closing one and leaving the other closes nothing.

**Independent Test**: Submit a transaction signed by an unauthorised wallet that claims genesis by
each route in turn, and confirm it is refused sender authorisation. In the same run, confirm the
same wallet is refused *without* the claim — so the test cannot pass by refusing everything.

**Acceptance Scenarios**:

1. **Given** a transaction signed by a wallet with no authority on the register, **When** it claims
   genesis via the transaction-type label, **Then** it is refused sender authorisation and the
   exemptions are not granted.
2. **Given** the same transaction, **When** it claims genesis via the blueprint-identifier route
   instead, **Then** it is refused identically.
3. **Given** the network's genuine genesis transaction, **When** a node bootstraps, **Then** it is
   accepted and sealed exactly as before.
4. **Given** a transaction bearing the genesis transaction identifier but a different payload and a
   valid self-signature, **When** it is submitted, **Then** it is refused, because the signing key
   does not match the node's trusted genesis anchor.

---

### User Story 2 - A forged blueprint-publication claim is refused (Priority: P1)

An attacker labels their transaction as a blueprint publication. Publication is how a definition
enters a register, and the label currently waives conformance and sender authorisation, so an
accepted forgery would place an unvalidated definition on the ledger under the register's own
authority.

**Why this priority**: Equal in severity to Story 1 and independently exploitable — it substitutes
no compensating check either. Separated from Story 1 because the authority it must prove is
different (the register's validator roster, not the network anchor) and resolves from a different
source.

**Independent Test**: Submit a publication-labelled transaction signed by a wallet with no publishing
authority on the register, confirm refusal, and confirm in the same run that a genuine publication
still seals.

**Acceptance Scenarios**:

1. **Given** a transaction signed by a wallet with no publishing authority on the register, **When**
   it claims to be a blueprint publication, **Then** it is refused sender authorisation.
2. **Given** a genuine publication signed by an authorised publisher, **When** it is submitted,
   **Then** it is accepted and the definition is published, with an unchanged publication identifier.
3. **Given** a register whose validator roster has been updated through governance, **When** a
   publication signed under the current roster is submitted, **Then** it is accepted.

---

### User Story 3 - The governance waiver cannot drift apart from its check (Priority: P2)

Today the governance waiver and the roster check that compensates for it are two independent
decisions that happen to read the same value. An edit to either one silently unhooks the other, and
the result is a waiver with nothing behind it — the exact shape of Stories 1 and 2.

**Why this priority**: No exploitable gap exists today, so this is hardening rather than repair. It
is nonetheless in scope: leaving two coincidentally-agreeing checks in place is how the other two
values reached their current state, and this is the only value where the correct check already
exists to couple to.

**Independent Test**: Verify that removing the roster check causes the waiver to be withheld rather
than silently granted — that is, the two can no longer disagree.

**Acceptance Scenarios**:

1. **Given** a governance transaction from a roster member, **When** it is validated, **Then** it is
   accepted exactly as before, with quorum behaviour unchanged.
2. **Given** a governance transaction from a non-member, **When** it is validated, **Then** it is
   refused, as before.
3. **Given** the roster check is made unavailable, **When** a governance transaction is validated,
   **Then** the waiver is withheld rather than granted on the label alone.

---

### User Story 4 - A transaction cannot describe itself differently to different readers (Priority: P3)

Several fields the validator relies on exist twice: once inside the signed content and once as an
unsigned field alongside it. The validator reads the unsigned copies and never compares them, so a
transaction can present one identity to the rules and another to its own signature.

**Why this priority**: This is the general form of the class of defect, and it closes future
instances rather than a currently-demonstrated one. Lower priority because no exploit through it has
been demonstrated by execution, unlike Stories 1 and 2.

**Independent Test**: Submit a transaction whose unsigned fields disagree with their signed
counterparts and confirm refusal; confirm agreeing transactions are unaffected.

**Acceptance Scenarios**:

1. **Given** a transaction whose unsigned blueprint or action identifier disagrees with the value
   inside its signed content, **When** it is validated, **Then** it is refused.
2. **Given** a transaction where the two agree, **When** it is validated, **Then** it is unaffected.
3. **Given** a transaction type that carries no signed counterpart for a field, **When** it is
   validated, **Then** the absent counterpart is not treated as a disagreement.

---

### Edge Cases

- **A node that cannot resolve the authority.** If the trusted anchor or the register's roster
  cannot be resolved, the waiver is withheld in every environment (FR-007 — decided 2026-08-28).
- **Replicas pulling already-sealed history.** A node that pulls a sealed docket verifies the
  docket's own signature and chain rather than re-running the rules. This path must remain
  unaffected, and must be exercised rather than assumed.
- **Transactions sealed before this change.** Not a constraint. The platform is pre-production and
  the node estate may be wiped and re-genesised (maintainer decision, 2026-08-28), so no
  compatibility is owed to data already on a register. What must hold is that a **wiped and
  re-genesised network reaches full function**.
- **The legacy publication era.** The *code* still distinguishes publications labelled as governance
  by a secondary field, and the authority check must evaluate the effective kind rather than the raw
  label so that guard stays correct. But no legacy *data* need survive.
- **Anchor rotation.** The trusted anchor is currently fixed at build time; making it configurable
  is tracked separately. This feature must not make that change harder.
- **A refused claim must not be silent.** A transaction claiming an exemption it is not entitled to
  is the signature of an attempted bypass, and must be recorded distinctly from an ordinary refusal.

---

## Requirements *(mandatory)*

### Functional Requirements

**Granting authority**

- **FR-001**: The system MUST grant an administrative exemption only when the transaction's signer
  is proved entitled to it, and MUST NOT grant one on the basis of any value the submitter can set
  without invalidating a signature.
- **FR-002**: The system MUST prove entitlement to the genesis exemption by matching the signing
  key against the node's trusted genesis anchor, in addition to the transaction's identity and
  register being the network's single genesis values.
- **FR-003**: The system MUST prove entitlement to the blueprint-publication exemption by matching
  the signer against the **register's validator roster**, resolved from the register rather than
  read from the transaction. *(Revised 2026-08-28. The original wording said "the register's own
  control key"; research established that publications are signed by the node's system wallet, which
  is deliberately absent from the governance roster, so the governance roster cannot answer this. The
  validator roster is the per-register, replicated, governance-updatable registry of node keys and is
  the correct authority. Matching against the validating node's own configured wallet was rejected as
  incorrect rather than merely narrow: it would accept a publication on the node that made it and
  refuse the same publication on every replica, silently partitioning the register.)*
- **FR-004**: The system MUST couple the governance exemption to the roster check that compensates
  for it, such that the exemption cannot be granted where the check has not been applied and passed.
- **FR-005**: The system MUST apply FR-002 to every route that grants the genesis exemption, not
  only the transaction-type label.
- **FR-006**: The system MUST refuse a transaction whose unsigned identifying fields disagree with
  their counterparts inside the signed content, where such a counterpart exists.
- **FR-007**: Where the authority required by FR-002, FR-003 or FR-004 cannot be resolved, the
  system MUST withhold the exemption rather than grant it.

**Preserving behaviour**

- **FR-008**: The system MUST NOT change what any of the six exemptions waives. Two are load-bearing
  for governance quorum, and withdrawing either makes quorum unattainable.
- **FR-009**: The system MUST NOT require any change to the genesis ceremony artefact, and MUST NOT
  require a network re-genesis to adopt.
- **FR-010**: The system MUST NOT change the canonical bytes of a published blueprint definition,
  which would alter every publication identifier on every register.
- **FR-011**: The system MUST bring a **wiped and re-genesised** network to full function: genesis
  bootstrap, blueprint publication, and governance all complete on a clean install. *(Revised
  2026-08-28: this originally required already-sealed transactions to keep validating. The platform
  is pre-production and the estate may be wiped, so historical validity is explicitly NOT required —
  which removes the largest single risk in this feature.)*
- **FR-012**: The system MUST leave the path by which a replica verifies an already-sealed docket
  unaffected.

**Observability**

- **FR-013**: The system MUST record a claim to an exemption the signer was not entitled to
  distinctly from an ordinary validation refusal, so that an attempted bypass is distinguishable
  from a malformed transaction.

### Key Entities

- **Exemption claim**: A transaction's assertion that it is administrative and should be waived
  certain rules. Currently self-asserted; after this feature, an assertion that must be corroborated
  by authority before it has effect.
- **Signer authority**: The proof that the party who signed a transaction is entitled to the
  exemption it claims. Distinct from *authenticity* (that the signature is valid) — a valid
  signature over an attacker's own transaction proves authorship, not entitlement.
- **Genesis trust anchor**: The network's root of trust, fixed at build time, against which a
  genesis claim is proved. Currently reachable only by one service and needed by another.
- **Register control key**: The key under whose authority a register's own administrative
  transactions are issued, and against which a publication claim is proved.
- **Governance roster**: The set of parties entitled to act on a register's governance. Already
  consulted; this feature makes the consultation load-bearing rather than parallel.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All four known routes to an unearned exemption (two genesis routes, publication,
  and field substitution) are refused when claimed by an unauthorised signer — each demonstrated
  with a counterfactual in the same run proving the refusal is caused by the check and not by
  blanket rejection.
- **SC-002**: Every guard added by this feature fails when its own check is removed. A guard that
  survives the removal of what it guards is not counted as delivered.
- **SC-003**: On a clean network, every legitimate administrative operation still completes
  end-to-end: node bootstrap from genesis, blueprint publication, and a governance change carried
  from proposal through approval to enactment.
- **SC-004**: A wiped and re-genesised network reaches full function on both nodes — genesis
  bootstrap, publication, governance, and a replica pull of sealed history — with no manual
  intervention beyond the documented bootstrap.
- **SC-005**: An operator can distinguish an attempted exemption bypass from an ordinary validation
  failure without reading source code.
- **SC-006**: Governance quorum remains attainable — a change requiring multiple approvals still
  reaches enactment, confirming no load-bearing exemption was withdrawn.
- **SC-007**: The definition identifiers of blueprints already published on the live network are
  unchanged after adoption.

---

## Out of Scope

- **The unauthenticated peer submission surface.** The transaction-distribution service accepts and
  forwards peer submissions without authentication, which is what makes these claims reachable from
  off-node. It affects the *reachability and therefore the severity* of this defect but not its
  existence — an authorised peer could exploit it regardless. It carries its own blast radius and
  overlaps an existing open item on service-to-service transport authentication, and is tracked
  separately. **No change to it is specified or implemented here.** Severity assessments arising
  from this feature must state that this surface remains open.
- **A refusal-reason channel.** No endpoint today answers "why was my transaction refused" — a
  transaction is accepted for processing and then simply never seals. This is a known gap and the
  largest obstacle to third-party conformance testing, but it is a separate feature. FR-013 requires
  only that the distinction is *recorded*, not that it is *served*.
- **Making the trust anchor configurable independently of the build.** Tracked separately.
- **Withdrawing or narrowing any exemption.** Explicitly forbidden by FR-008.

---

## Assumptions

- **Unresolvable authority fails closed (FR-007). DECIDED 2026-08-28 — no longer an assumption.**
  Where the authority cannot be resolved, the exemption is withheld, **in every environment**. No
  environment gate (Production/Staging only was considered and rejected — it would mean dev and CI
  exercise a different security decision from production, which is where the guards' own tests run)
  and **no operator bypass flag** (rejected — a flag that disables a security check is one someone
  eventually leaves on, and this node estate has already run with `Development` set in production).
  The accepted cost is that an authority-resolution outage refuses legitimate administrative traffic
  rather than silently downgrading security; the FR-013 "could not resolve" signal exists so that
  outage is diagnosable rather than mysterious.
- **The node estate may be wiped and re-genesised** (maintainer decision, 2026-08-28). Pre-production,
  no installation is owed an upgrade path, and CLAUDE.md §19's pre-release posture applies. This
  removes the backward-compatibility constraint that would otherwise have made this feature's
  publication work conditional on what existing registers happen to contain.
- **The trusted anchor is available to the validating component**, by relocating the existing shared
  abstraction rather than introducing a second source of truth for it.
- **Authority resolution is on the per-transaction validation path**, so its cost matters. It is
  assumed to be cacheable per register; if it cannot be, that is a finding for planning, not a
  reason to skip the check.
- **Live verification on both nodes is part of completion**, not a follow-up. Merged is not proven.
- **Tests must not stub the hashing layer.** A prior defect in this area stayed invisible because
  test doubles made every hash compare equal by construction.
