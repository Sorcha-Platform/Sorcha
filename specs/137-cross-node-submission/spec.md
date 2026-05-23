# Feature Specification: Cross-node submission round-trip (Stage 5)

**Feature Branch**: `137-cross-node-submission`
**Created**: 2026-05-23
**Status**: Draft
**Input**: User description: "Cross-node submission round-trip (Stage 5): citizen on a local SyncOnly replica submits an AssuredIdentity application that reaches the n1 owner/validator node, is validated/sealed there, approved by the verification-analyst on n1, and the resulting AssuredIdentityCredential is delivered back to the citizen's local wallet."

> Source of truth for the architecture and decisions: `docs/superpowers/specs/2026-05-23-cross-node-submission-design.md`. This spec restates that design as falsifiable requirements; where they differ, the design doc wins until this spec is updated.

## User Scenarios & Testing *(mandatory)*

A "node" is a Sorcha installation. The **owner node** holds a register's genesis and validator roster and seals its dockets; a **replica node** subscribes to that register read-only (SyncOnly). The two are separate identity domains — a person's account and wallet on the replica do not exist on the owner. Three actors appear below: the **citizen** (a public applicant on the replica), the **verification-analyst** (an operator on the owner node), and the **platform operator** (who runs and observes the nodes).

### User Story 1 - Citizen submission reaches and seals on the owner node (Priority: P1)

A citizen, signed in on a replica node, starts an AssuredIdentity application and submits it. Even though the replica cannot seal transactions for that register, the submission travels to the owner node, which validates and seals it. The citizen sees their application accepted.

**Why this priority**: This is the foundational write hop. Until a replica-origin submission can be sealed by the owner, no cross-node workflow is possible. It is the minimum viable slice — it proves the write/ledger path crosses nodes — and everything else builds on it.

**Independent Test**: On a replica with a register replicated from the owner, sign in as a citizen, create a workflow instance, and submit the starting action. Verify a sealed docket containing that transaction appears on the owner node and replicates back to the replica. No manual blueprint-service restart is required.

**Acceptance Scenarios**:

1. **Given** a register owned by the owner node and replicated to the replica, and the citizen is authenticated on the replica, **When** the citizen creates a workflow instance from the register's published blueprint, **Then** instance creation succeeds (no "Blueprint not found") without the blueprint existing in the replica's draft/editable store.
2. **Given** the citizen has created an instance on the replica, **When** they submit the starting action, **Then** the submission is delivered to the owner node, validated (signature verified, open-participant late-binding applied), and sealed into a docket on the owner.
3. **Given** the replica is not on the register's validator roster, **When** the starting action is submitted, **Then** the replica does not itself seal the transaction.
4. **Given** instance creation on a replica, **When** the instance is created, **Then** the replica does not attempt to (re)publish the blueprint to the register it does not own.

---

### User Story 2 - Approved application returns a credential to the citizen's local wallet (Priority: P2)

After the application is sealed on the owner node, the verification-analyst on the owner reviews and approves it. A credential is issued to the citizen and is delivered back to the citizen's wallet on the replica, where it appears automatically.

**Why this priority**: This completes the round-trip and delivers the end-user value (the citizen receives their credential). It depends on US1's sealed submission but is independently testable given a sealed application.

**Independent Test**: Given a sealed application on the owner node, drive the verification-analyst's approval action, then observe the `AssuredIdentityCredential` appear in the citizen's wallet on the replica — decryptable only by that citizen — with no manual key entry at any step.

**Acceptance Scenarios**:

1. **Given** a sealed starting-action transaction whose participants include the owner-node analyst, **When** the docket confirms on the owner, **Then** the owner materialises a workflow instance the analyst can act on, with prior state reconstructed from the register.
2. **Given** the analyst approves, **When** the credential-issuance action runs, **Then** the owner resolves the citizen's delivery keys by precedence (published participant record → keys carried in the submission → fail closed) and issues the credential bound to the citizen's holder key and encrypted to the citizen's encryption key.
3. **Given** the issued credential replicates to the replica, **When** the replica processes the docket, **Then** the credential is decrypted by the citizen's local wallet and surfaces to the citizen without manual intervention.
4. **Given** neither a published participant record nor carried keys resolve for the recipient, **When** issuance is attempted, **Then** the system fails closed and does not issue an unusable credential.

---

### User Story 3 - Replicas pick up newly-synced registers without a restart (Priority: P3)

A platform operator subscribes a running replica node to a new register. The register's blueprints become usable for instance creation immediately, without restarting the blueprint service.

**Why this priority**: Operability hardening that removes the manual-restart workaround used in the probe. Valuable on its own (any post-boot subscription works), and it makes US1 robust to subscribe-after-boot ordering.

**Independent Test**: On a running replica, subscribe to a register after the blueprint service has started, then immediately create an instance from that register's blueprint — succeeds with no restart.

**Acceptance Scenarios**:

1. **Given** a running replica node, **When** a new register is replicated/subscribed after start-up, **Then** the register's published blueprints become available for instance creation without a service restart.
2. **Given** a replication signal is missed, **When** the periodic safety reconciliation runs, **Then** the register's blueprints still eventually become available.

---

### Edge Cases

- **Blueprint not yet recovered when the citizen arrives** → the system returns a typed "register still syncing" state (not a bare 400) and the client retries, rather than failing opaquely.
- **Carried keys missing or malformed at issuance** → issuance fails closed; no credential is produced.
- **Both a published participant record and carried keys are present** → the published record wins; any conflict is logged.
- **Owner node unreachable at submission time** → the submission is accepted locally as "awaiting validation" and retried; it is never silently lost.
- **Submitter supplies delivery keys they do not control** → self-defeating (the resulting credential is unusable to them); acceptable in v1 because the open-participant submitter is also the recipient (see Assumptions).

## Requirements *(mandatory)*

### Functional Requirements

**Blueprint availability on replicas**

- **FR-001**: A citizen authenticated on a replica node MUST be able to create a workflow instance from a blueprint that exists only in the node's published (replicated) store, with no copy in the draft/editable store.
- **FR-002**: A replica node MUST NOT attempt to publish or re-publish a blueprint to a register it does not own during instance creation.
- **FR-003**: When a register is newly replicated to / subscribed by a node after that node has started, the node MUST make the register's published blueprints available for instance creation without a service restart.
- **FR-004**: The system MUST retain a periodic safety reconciliation so a missed replication signal eventually yields blueprint availability.

**Cross-node submission & sealing**

- **FR-005**: When a citizen submits a starting action on a replica for a register owned by another node, the submission MUST be delivered to the owner node for validation.
- **FR-006**: The owner node MUST validate and seal a starting-action transaction that originated on another node — verifying the submitter's signature cryptographically and applying open-participant late-binding (no pre-existing participant record required for the applicant).
- **FR-007**: A node that is not on a register's validator roster MUST NOT seal transactions for that register.
- **FR-008**: The same workflow instance MUST be identifiable on both nodes (a shared instance identifier travels with the transaction), so the owner can act on the instance the citizen started.

**Open-participant delivery keys**

- **FR-009**: A blueprint author MUST be able to declare a field that captures the submitting citizen's delivery public keys — a holder public key (for credential binding) and an encryption public key (for credential delivery).
- **FR-010**: The client MUST auto-populate that field from the citizen's own derived public keys, with no manual entry and without exposing private keys.
- **FR-011**: The captured public keys MUST be carried with the submission so they are present in replicated register state on the owner node.
- **FR-012**: When issuing a credential to a recipient, the owner node MUST resolve the recipient's delivery keys by precedence: (1) a published participant record on the register, otherwise (2) the keys carried in the submission, otherwise (3) fail closed without issuing.

**Approval & credential delivery**

- **FR-013**: The verification-analyst on the owner node MUST be able to approve the application by submitting the next action against the owner-materialised instance, with prior state reconstructed from the register.
- **FR-014**: The issued credential MUST be bound to the citizen's holder key and encrypted to the citizen's encryption key, such that only the citizen's local wallet can decrypt it.
- **FR-015**: The delivered credential MUST arrive in the citizen's local wallet and surface to the citizen without manual intervention.

**Trust model & scope boundaries**

- **FR-016**: The cross-node round-trip MUST NOT require JWT access tokens to be portable across installations; the two nodes remain separate identity domains, and cross-node trust is anchored only by the register's validator roster and transaction/docket signatures.
- **FR-017**: The submission key-capture path MUST support open/public-user participants who have no pre-existing participant record.
- **FR-018**: v1 MUST NOT require the citizen to be promoted to a published participant record, and MUST NOT require a proof-of-possession challenge on the captured keys (both are explicitly deferred — see Out of Scope).

### Key Entities *(include if feature involves data)*

- **Owner node / Replica node**: roles of an installation relative to a given register; the owner seals, the replica subscribes read-only. Distinct identity domains.
- **Register**: the shared ledger replicated across nodes; carries the validator roster and is the source of truth for workflow state.
- **Published blueprint**: the replicated, read-only blueprint available on a replica (as opposed to the owner-only draft/editable blueprint).
- **Workflow instance / mirror instance**: the per-node execution shell; the owner and replica each derive their own, keyed by a shared instance identifier, with accumulated state reconstructed from the register.
- **Submission transaction**: the citizen's signed starting-action submission; carries the shared instance identifier, the signature, the action payload, and the delivery-key field.
- **Delivery-key field value**: the citizen's holder public key (for credential binding) and encryption public key (for credential delivery), auto-captured at submission.
- **AssuredIdentityCredential**: the credential issued by the owner, bound and encrypted to the citizen's keys, delivered to the citizen's local wallet.
- **Citizen local wallet**: the wallet on the replica holding the citizen's private keys; the only party able to decrypt the delivered credential.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** (full round-trip, cross-node machine): In a single end-to-end run, a citizen on the replica completes the AssuredIdentity application, the analyst on the owner approves, and the `AssuredIdentityCredential` appears in the citizen's local wallet — with **zero** manual service restarts and **zero** manual key entry.
- **SC-002**: Instance creation on a replica from a published-only blueprint succeeds in 100% of attempts (no "Blueprint not found"), where previously it failed 100% of the time.
- **SC-003**: A register subscribed after node start yields a usable blueprint for instance creation within 30 seconds, without a restart.
- **SC-004**: 100% of credentials issued cross-node are decryptable by the intended citizen's local wallet; the system issues **zero** credentials when neither a published record nor carried keys resolve (fail-closed verified).
- **SC-005** (build-machine gate): All unit and single-node integration tests pass on the build machine, and a scripted, reproducible cross-node verification procedure is committed and runnable on the machine that holds the genesis validator key.

## Assumptions

- **Trust-on-submission (no proof-of-possession) is safe in v1** because the open-participant submitter is late-bound as the applicant and is therefore also the credential recipient: a wrong holder key yields an unpresentable credential and a wrong encryption key yields one the submitter cannot decrypt — both self-defeating. PoP is deferred and paired with the backlogged participant-record promotion.
- **Delivery keys are HD-derived** from the citizen's wallet, so the client can reproduce the public halves deterministically; only public material is ever carried.
- **The owner may resolve the citizen's keys but never their identity** — all cross-node resolution reduces to ledger-native facts (a signature verifies; a key is in replicated register state).
- **Cross-node integration testing runs on a separate machine** (holding `genesis-validator-key.json`, owner-node SSH access, and the sync split); the build machine delivers code plus the scripted procedure.
- **Existing cross-node machinery is reused** (genesis validator roster, anonymous-tolerant peer transport, instance-mirror reconstruction, register-as-source-of-truth state reconstruction, ownership-agnostic submission fan-out, register-native credential delivery).

## Out of Scope (backlog)

- **Participant-record promotion** — promoting carried keys into a durable published participant record for reuse by later actions / repeat flows, including the "use-or-supersede" rule for org field agents who are already published. Paired with PoP hardening.
- **Proof-of-possession** on the captured key field.
- Non-SD-JWT credential formats cross-node, multiple registers per round-trip, and more than two nodes (the design must not preclude these but does not test them).
- MCP register-control / sync tooling (the admin slice remains observational).
