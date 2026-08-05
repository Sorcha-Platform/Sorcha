# Feature Specification: Provenance — trust-anchor and proof lineage

**Feature Branch**: `188-provenance-lineage`

**Created**: 2026-08-05

**Status**: Draft

**Input**: Trust-anchor and proof-lineage views letting an administrator or auditor verify who signed off on what, from a fact back to the trust anchor.

## Why this is called Provenance and not Audit

"Audit" is already taken, and means the opposite thing: the existing audit service *writes* a log of administrative actions. This feature *reads* evidence and reports what can be proven about it. Naming both "audit" would put a read-only evidence viewer and a write-side action logger under one word — the exact class of collision Feature 187 spent its length untangling.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prove a register's history and who signed each docket (Priority: P1)

A platform administrator opens a register and sees its whole life laid out from genesis: every docket in order, who proposed it, and which validators signed it. Where the validator set changed, that change appears in place, so the register's growth is visible as history rather than inferred from a config file. Selecting any docket shows what can be proven about it, check by check, with each check stating what it was compared against.

**Why this priority**: This is the story that must exist before the platform can add validators with confidence. Without it, "the network grew" is an assertion in configuration rather than a fact on the record. It is also the only story wholly answerable from evidence the platform already keeps.

**Independent Test**: Open a register with several dockets and at least one validator-set change. Confirm the history is complete and ordered, the change is visible at the point it happened, and selecting a docket yields a per-check result naming what each check compared.

**Acceptance Scenarios**:

1. **Given** a register with a genesis and subsequent dockets, **When** the administrator opens its provenance view, **Then** every docket appears in order with its proposer and signer set.
2. **Given** a register whose validator set changed at a known point, **When** the administrator views the history, **Then** the change is shown at that point and later dockets show the changed set.
3. **Given** any docket, **When** the administrator selects it, **Then** each check reports Verified, Failed, or Not verifiable, and states what it was compared against.
4. **Given** a docket whose stored transaction list has been altered after sealing, **When** its checks run, **Then** the seal check reports Failed rather than Verified.
5. **Given** a register on a single-validator deployment, **When** the signer check runs, **Then** it reports Not verifiable with the reason, and never reports Verified.

---

### User Story 2 - Trace an application from outcome back to evidence (Priority: P2)

An administrator investigating a decision opens an application and sees its narrative: who submitted it, who decided it, which route was taken and for what recorded reason, and what was issued as a result. Each step names the sealed record backing it, and can be followed through to the docket that sealed it.

**Why this priority**: This answers the question a regulator or complainant actually arrives with, but it depends on the verification machinery proven by Story 1, and leans on attestation checks never exercised outside the sealing path.

**Independent Test**: Open a completed application, confirm each step names its backing record, and follow a step through to the docket containing it.

**Acceptance Scenarios**:

1. **Given** a completed application, **When** the administrator opens its provenance view, **Then** submission, decision, routing reason and issuance each appear with the record backing them.
2. **Given** any step, **When** the administrator follows it, **Then** they arrive at the docket that sealed it.
3. **Given** an application whose decision carries a recorded reason, **When** the view renders, **Then** the citizen-facing wording is resolved from the published definition rather than shown as a bare code.
4. **Given** an application view, **When** it is displayed, **Then** it states plainly that the application record is assembled from sealed records and is not itself signed.

---

### User Story 3 - Follow a docket to what it sealed, and back (Priority: P3)

From a docket, an administrator can see which applications it sealed records for; from an application step, they can reach the docket. The two histories are navigable in both directions without losing place.

**Why this priority**: Cross-linking makes the two views one investigation instead of two lookups, but each view is useful alone.

**Independent Test**: From a docket, reach an application it sealed; from that application, return to the docket.

**Acceptance Scenarios**:

1. **Given** a docket sealing records for one or more applications, **When** viewed, **Then** those applications are listed and reachable.
2. **Given** an application step, **When** followed, **Then** the corresponding docket opens at that step's record.

---

### Edge Cases

- **A register held only in part.** A node holding some but not all of a register's history must report the missing links as Not verifiable, never as Failed. Absence of evidence is not evidence of tampering, and conflating them would make every subscribing node look compromised.
- **A validator removed after signing.** A signature valid when made must stay Verified after that validator leaves the set. Judging a historical signature against the present set is the single most likely way this feature reports a false failure — and it grows more likely the more the network grows.
- **A deployment with no consensus.** Where only one validator seals, there is no quorum evidence. Every affected check reports Not verifiable with the reason; a pass here would be the feature lying about the strongest claim it makes.
- **Records predating this feature.** Dockets sealed before the platform kept a proposer, a sealed commitment or vote records cannot be checked for those things, and must say so.
- **A register with thousands of dockets.** History must remain usable at that size; checking is performed on the docket the user selects, not across the whole history.
- **Evidence that cannot be assembled.** A view that cannot gather what it needs shows which parts could not be established, rather than failing wholesale — knowing *which* link is missing is the point.

## Requirements *(mandatory)*

### Functional Requirements

**Evidence and verification**

- **FR-001**: Every check MUST report exactly one of Verified, Failed, or Not verifiable.
- **FR-002**: Every check MUST state what it was compared against, in terms the reader can act on.
- **FR-003**: Not verifiable MUST be a first-class result carrying its reason, and MUST NOT be presented as an error or as a failure.
- **FR-004**: A check MUST NOT report Verified when it did not run.
- **FR-005**: Where a check confirms that stored evidence is internally consistent rather than independently correct, it MUST say so and MUST NOT imply independent validation.

**Register history**

- **FR-006**: The system MUST present a register's dockets in order from genesis, each with its proposer and signer set.
- **FR-007**: The system MUST show validator-set changes at the point in history where they occurred.
- **FR-008**: The system MUST confirm each docket's link to its predecessor.
- **FR-009**: The system MUST confirm each docket's sealed commitment against its recorded contents.
- **FR-010**: The system MUST confirm each signature against the validator set **as it stood when that docket was sealed**, not as it stands now.
- **FR-011**: The system MUST confirm the register's origin against its trust anchor.
- **FR-012**: Where a docket carries no quorum evidence, the system MUST report Not verifiable and state why.

**Application history**

- **FR-013**: The system MUST present an application as an ordered narrative of submission, decision, routing reason and issuance, each naming the sealed record backing it.
- **FR-014**: The system MUST confirm that the party who signed each step was the party entitled to act at that step.
- **FR-015**: The system MUST independently confirm the attestation carried on each routing decision.
- **FR-016**: The system MUST resolve a recorded decision reason to its published wording, and MUST NOT present a bare code to the reader.
- **FR-017**: The system MUST state that an application record is assembled from sealed records and is not itself a signed object.

**Navigation and access**

- **FR-018**: A docket MUST be navigable to the applications whose records it sealed, and an application step to the docket that sealed it.
- **FR-019**: Both views MUST be restricted to administrators of the owning organisation.
- **FR-020**: A view that cannot assemble part of its evidence MUST render the parts it can and mark the rest Not verifiable with a reason, rather than failing entirely.

**Scale**

- **FR-021**: Register history MUST remain usable on registers with thousands of dockets.
- **FR-022**: Verification MUST be performed for the docket a user selects, not across a whole register in order to list it.

### Key Entities

- **Provenance check** — one question asked of the evidence: which layer it concerns, its result, a short headline, the detail behind it, and what it was compared against.
- **Provenance trail** — the ordered set of checks for a single subject (a docket, or a step in an application).
- **Register history** — the ordered sequence of dockets with their proposers, signer sets, and the points at which the validator set changed.
- **Application history** — the ordered narrative of an application with, for each step, the sealed record backing it.
- **Validator set as of a point in history** — the membership that applied when a given docket was sealed, which is not necessarily today's membership.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can determine who signed a given docket, and whether those signatures hold, without leaving the view or consulting a database.
- **SC-002**: Where a register's validator set has changed, an administrator can identify when it changed and what it changed to, from the history alone.
- **SC-003**: Deliberately altering a docket's recorded contents causes its seal check to report Failed. This is demonstrated, not assumed.
- **SC-004**: A signature made by a validator that has since been removed still reports Verified for the docket it signed; a signature purporting to be made by that validator after removal reports Failed.
- **SC-005**: On a deployment with a single validator, no check reports Verified for quorum; every affected check reports Not verifiable with a stated reason.
- **SC-006**: Every check displayed states what it compared against; a reviewer can restate the basis of any result in their own words from the view alone.
- **SC-007**: Register history opens and remains navigable on a register of at least 5,000 dockets.
- **SC-008**: An administrator can move from an application's outcome to the docket that sealed it, and back, without re-entering identifiers.
- **SC-009**: Where evidence is missing, the view names which link could not be established rather than reporting a general failure.

## Assumptions

- **Administrators first, external auditors later.** The first delivery serves administrators of the owning organisation. Serving an external auditor is a separate capability — a portable evidence export they can check with their own tools — and explicitly *not* a loosening of who may open these views. The design should not have to be rebuilt to add it.
- **Evidence already exists.** The records these views read — sealed commitments, proposer identity, vote records, routing attestations, issuance records — are already kept by the platform as of Feature 187. This feature reads and reports; it does not introduce new evidence.
- **Reporting, not remediation.** The views report what can be proven. Acting on a failure (re-sealing, quarantining, alerting) is out of scope.
- **Nothing is written.** These are read-only views. Recording that someone *performed* an audit is the existing audit-logging concern and is not part of this feature.
- **One node's view.** A view reports what the node serving it can establish. Reconciling disagreement between nodes is a separate problem.
- **Delivery order.** Register history first, because it is what the platform needs before adding validators; application history second; portable export third. Each is independently useful.

## Out of Scope

- Widening access beyond administrators — the external-auditor path is the portable export, delivered separately.
- Aggregating or reconciling provenance across nodes.
- Making the trust anchor independently configurable — tracked as issue #1374. A dependency only in the sense that a node with a mismatched anchor will correctly report its origin check as unverifiable.
- Any remediation action taken in response to a failed check.
