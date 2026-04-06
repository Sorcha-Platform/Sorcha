# Feature Specification: Validator Key Roster

**Feature Branch**: `086-validator-key-roster`  
**Created**: 2026-04-06  
**Status**: Draft  
**Input**: Declare authorized validator signing keys in register genesis, derived from system wallet, to enable cross-node docket verification and future n-of-m threshold signing.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Remote Peer Verifies Synced Dockets (Priority: P1)

A remote peer subscribes to a register hosted on another node. During full-replica sync, the remote peer pulls dockets (blocks) via the relay network. For each docket, the peer must verify that it was signed by an authorized validator before persisting it to its local register store.

Today this fails because the validator's public key is not declared anywhere the remote peer can access. The validator's signing key must be embedded in the register's genesis record so any peer can extract it and verify all subsequent dockets.

**Why this priority**: Without this, cross-node register replication is non-functional. Peers pull dockets but reject them all, leaving the remote register at height 0. This is the core blocker for multi-node operation.

**Independent Test**: Create a register on Node A, subscribe on Node B, verify dockets are accepted and the register height on Node B matches Node A.

**Acceptance Scenarios**:

1. **Given** a register with 50 sealed dockets on Node A, **When** Node B subscribes and syncs the register via relay, **Then** all 50 dockets pass signature verification and the register height on Node B equals 50.
2. **Given** a genesis record containing one validator entry, **When** a remote peer reads the genesis, **Then** the peer can extract the validator's public key and algorithm without relying on the docket signature structure.
3. **Given** a docket signed by a key NOT in the validator roster, **When** a remote peer attempts to finalize it, **Then** the docket is rejected with a clear error indicating the signer is not authorized.

---

### User Story 2 - Register Genesis Declares Validator Roster (Priority: P1)

When a register is created, the genesis control record must include a list of authorized validator signing keys alongside the existing administrative attestations. Initially this list contains exactly one entry: the local validator's purpose-derived signing key (derived from the system wallet, not the org wallet). The key derivation path is recorded for auditability.

**Why this priority**: This is the data foundation that US1 depends on. Without the declared roster in genesis, there is nothing for remote peers to verify against.

**Independent Test**: Create a new register and inspect the genesis control record. It must contain a `validators` list with one entry including the public key, algorithm, and derivation context.

**Acceptance Scenarios**:

1. **Given** a new register being created, **When** the genesis control transaction is finalized, **Then** the control record contains a `validators` list with exactly one entry for the local validator.
2. **Given** the validator entry in genesis, **When** inspected, **Then** it contains: a public key (purpose-derived from the system wallet, not the master key), the signing algorithm, a validator identifier, and the derivation context.
3. **Given** an existing register created before this feature, **When** the system is upgraded, **Then** all pre-existing registers are deleted and recreated with the new genesis format.

---

### User Story 3 - Validator Pool Expansion via Governance (Priority: P2)

A register owner submits a governance proposal to add a second validator's signing key to the register's authorized validator roster. The proposal follows the existing governance quorum-approval flow. Once approved, a new control transaction is recorded containing the updated validator list. Remote peers picking up this control transaction update their cached authorized key set.

**Why this priority**: Multi-validator consensus is required for production trustworthiness but a single validator is sufficient for the current development and testing phase. The data model must support multiple validators from day one to avoid schema migration later.

**Independent Test**: Add a validator to an existing register's roster via a governance proposal, then verify a docket signed by the new validator is accepted by remote peers.

**Acceptance Scenarios**:

1. **Given** a register with one validator in its roster, **When** the owner proposes adding a second validator and it is approved, **Then** a new control transaction is recorded with two validators in the roster.
2. **Given** a register with two authorized validators, **When** either validator signs a docket, **Then** remote peers accept the docket because both keys are in the authorized set.
3. **Given** a validator that has been removed from the roster via governance, **When** that validator signs a new docket, **Then** remote peers reject the docket.

---

### User Story 4 - Schema Supports Future n-of-m Threshold Signing (Priority: P3)

The validator roster schema includes fields to support future threshold signing (n-of-m key composition), where a docket requires signatures from n out of m authorized validators before it is considered valid. The initial implementation requires m=1 (single signer), but the data model carries the threshold parameters so they can be activated without schema changes.

**Why this priority**: n-of-m signing is essential for production Byzantine fault tolerance but is not needed for the current phase. Designing the schema now avoids a breaking migration later.

**Independent Test**: Inspect the validator roster schema and confirm it includes threshold fields (minimum signatures required, total validators) even if they default to single-signer mode.

**Acceptance Scenarios**:

1. **Given** a newly created register, **When** the validator roster is inspected, **Then** it contains threshold parameters defaulting to `requiredSignatures=1`.
2. **Given** a register with `requiredSignatures=1` and one validator, **When** the system evaluates a docket with one valid signature, **Then** the docket passes the threshold check.
3. **Given** the validator roster schema, **When** a future update sets `requiredSignatures=2` with 3 validators, **Then** the schema accommodates this without structural changes.

---

### Edge Cases

- What happens when a register created before this feature (no `validators` field in genesis) is encountered? Pre-existing registers are deleted as part of the upgrade. The system does not need backward compatibility for legacy genesis formats.
- What happens when a validator's key is rotated? The old key remains in the roster with a "Rotated" status (verify-only for historical dockets), and the new key is added as the active entry.
- What happens if the system wallet that derived the validator key is destroyed or re-created? The validator's signing authority is revoked and a new key must be added via governance proposal.
- What happens if the genesis control record's validator list is empty? Register creation must reject this, at least one validator is mandatory.
- What happens during docket finalization when the genesis was synced but the control payload deserialization fails? Reject the docket and log an error. All registers must have the new genesis format.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The register genesis control record MUST include a `validators` list containing at least one authorized validator signing key entry.
- **FR-002**: Each validator entry MUST contain: a unique validator identifier, a public key (purpose-derived from system wallet), the signing algorithm, a derivation context string, the authorization timestamp, and a status indicator.
- **FR-003**: The validator signing key MUST be derived from the system wallet (not the org wallet), so the system wallet's master key is not directly exposed. The derivation context must be recorded for auditability.
- **FR-004**: Remote peers MUST extract authorized validator keys from the genesis control record's `validators` list when verifying synced dockets.
- **FR-005**: All pre-existing registers (created before this feature) MUST be deleted as part of the upgrade. The system does not require backward compatibility for legacy genesis formats without a `validators` field.
- **FR-006**: The validator roster MUST be updatable via governance control transactions, following the existing quorum-approval flow for roster changes.
- **FR-007**: Validator roster updates MUST propagate to remote peers when they sync control transactions, updating their cached set of authorized keys.
- **FR-008**: Dockets signed by keys not present in the current validator roster MUST be rejected during finalization on remote peers.
- **FR-009**: The validator roster schema MUST include threshold signing parameters: `requiredSignatures` (minimum valid signatures per docket, default 1) and the total validator count.
- **FR-010**: Register creation MUST fail if zero validators would be declared in the genesis roster.
- **FR-011**: Validator key rotation MUST be supported by adding a new key entry and marking the old entry as "Rotated" (eligible only for verifying historical dockets, not for signing new ones).
- **FR-012**: The docket builder MUST sign dockets using the purpose-derived validator key (matching the key declared in the roster), not the system wallet's root key directly.

### Key Entities

- **ValidatorRosterEntry**: An authorized validator's signing key declaration within a register's control record. Contains validator identifier, public key, algorithm, derivation context, status (Active, Rotated, Revoked), and authorization timestamp.
- **ValidatorRoster**: The list of ValidatorRosterEntries within a RegisterControlRecord, plus threshold parameters (requiredSignatures). Represents the set of keys authorized to sign dockets for this register.
- **RegisterControlRecord (extended)**: The existing governance record, extended with a `validators` field containing the ValidatorRoster.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of dockets synced from a remote register with a declared validator roster pass signature verification on the receiving peer (currently 0% pass).
- **SC-002**: Register creation completes with the validator roster populated in under 5 seconds (no measurable regression from current creation time).
- **SC-003**: After upgrade, all newly created registers include the validator roster in genesis and sync successfully across nodes.
- **SC-004**: A governance proposal to add or remove a validator from the roster completes within the existing governance approval flow with no additional manual steps.
- **SC-005**: The validator roster schema supports declaring up to 10 validators per register without requiring future schema changes.

## Foundation Requirements for System Register (087)

The following requirements are included in this feature to ensure the validator key roster supports the future System Register architecture without requiring schema or API changes later. See `specs/087-system-register-governance` (to be specced) for the full design.

**Context**: The Sorcha System Register will become a singleton canonical register synced by all nodes, carrying system blueprints, platform config, and upgrades. Each node currently creates its own independent copy — the future model has one authoritative copy with a curated platform validator roster. Nodes that are orphaned from the network should still be able to bootstrap a minimal private system register.

- **FR-013**: The validator roster in genesis MUST support declaring validators that are NOT the local node's own validator. This enables creating a register with a pre-defined set of remote validators (required for the System Register's curated validator pool).
- **FR-014**: The register creation flow MUST accept an optional externally-provided validator roster (list of public keys, algorithms, and identifiers) rather than always auto-detecting only the local validator. When not provided, the local validator is used as the default (single-entry roster).
- **FR-015**: The validator roster MUST NOT embed any assumption that the declaring validator is co-located with the register. Remote validators must be first-class entries indistinguishable from local ones.

## Assumptions

- The system wallet's key derivation capability already exists and supports purpose-derived child keys (confirmed: system wallet uses derivation path `"sorcha:register-control"` for signing).
- Org ownership attestation (admin roster) remains a separate concern from validator signing authority. These are independent lists within the control record.
- n-of-m threshold signing enforcement (requiring multiple signatures per docket) is schema-only for this feature. Actual multi-signature validation is deferred to a future feature.
- The validator identifier format follows the existing convention (wallet address or DID).
- The maximum validator roster size (10) is sufficient for the foreseeable production topology.

## Scope Boundaries

**In scope:**
- Validator roster data model in RegisterControlRecord
- Genesis population with initial validator key (derived from system wallet)
- Remote peer key extraction from genesis control record
- Docket signature verification against declared roster
- Governance proposal flow for roster updates (add/remove/rotate)
- Clean-break upgrade (delete pre-existing registers, no legacy support needed)
- Schema support for threshold signing parameters

**Out of scope:**
- Actual n-of-m threshold signing enforcement (multi-signature docket validation)
- Validator discovery or automatic enrollment
- Key escrow or recovery mechanisms
- Changes to the admin governance roster (Owner/Admin/Auditor/Designer)
- UI for managing the validator roster (governance UI already exists)
- Backward compatibility for pre-existing registers (clean-break, preproduction)
