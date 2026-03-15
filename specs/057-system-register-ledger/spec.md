# Feature Specification: System Register as Real Ledger

**Feature Branch**: `057-system-register-ledger`
**Created**: 2026-03-15
**Status**: Draft
**Input**: Redesign the System Register to be a real register that uses the existing distributed ledger infrastructure instead of a separate MongoDB collection.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Platform Bootstraps System Register on First Startup (Priority: P1)

When the Sorcha platform starts for the first time, it automatically creates the system register as a real register using the standard two-phase creation flow. The system wallet signs as the owner. Once the genesis docket is sealed, the platform publishes the two seed blueprints (register-creation-v1, register-governance-v1) as transactions on the system register. No manual intervention is required.

**Why this priority**: Without automatic bootstrap, no registers can be created and the platform is non-functional. This is the foundation for all other platform operations.

**Independent Test**: Start a fresh Sorcha deployment with no existing data. Verify the system register appears in the registers list with its deterministic ID, has a genesis transaction with the control record, and contains two sealed blueprint transactions.

**Acceptance Scenarios**:

1. **Given** a fresh Sorcha deployment with no existing registers, **When** the platform starts, **Then** the system register is created with the deterministic ID `aebf26362e079087571ac0932d4db973`, a genesis docket containing the control record, and two blueprint transactions sealed in subsequent dockets.
2. **Given** a Sorcha deployment where the system register already exists, **When** the platform restarts, **Then** no duplicate register or duplicate blueprint transactions are created (idempotent).
3. **Given** the system register has been created, **When** an administrator views the registers list, **Then** the system register appears alongside user-created registers with name "Sorcha System Register".

---

### User Story 2 - Administrator Publishes a Blueprint to the System Register (Priority: P2)

An administrator publishes a new blueprint to the system register. The blueprint JSON becomes the payload of a transaction submitted to the system register, which is validated by the validator service and sealed into a docket — following the same cryptographic guarantees as any other register transaction.

**Why this priority**: After bootstrap, the ability to add new blueprints to the system register is the primary ongoing administrative action.

**Independent Test**: With the system register created, submit a new blueprint via the API. Verify it appears as a transaction on the system register, is sealed into a docket, and can be queried.

**Acceptance Scenarios**:

1. **Given** an initialized system register, **When** an administrator publishes a new blueprint, **Then** the blueprint is submitted as a transaction payload, validated, and sealed into a docket on the system register.
2. **Given** a blueprint has been published, **When** a new version of the same blueprint is published, **Then** the new transaction references the previous blueprint transaction as its predecessor, creating a versioned chain.
3. **Given** a published blueprint, **When** any node queries the system register, **Then** the blueprint payload is retrievable from the transaction data.

---

### User Story 3 - Administrator Views System Register Status and Blueprints (Priority: P2)

An administrator navigates to the System Register admin page to view the register's status (initialized, height, transaction count) and browse published blueprints. The UI shows the system register as a real register with transactions and dockets, not as a separate concept.

**Why this priority**: Visibility into the system register's state is essential for platform administration and debugging.

**Independent Test**: Navigate to the admin System Register page with a bootstrapped platform. Verify it shows the register's real metadata (height, docket count) and lists blueprints extracted from transaction payloads.

**Acceptance Scenarios**:

1. **Given** an initialized system register, **When** an administrator views the system register admin page, **Then** the page shows the register's real height, transaction count, and "Initialized" status.
2. **Given** the system register has blueprint transactions, **When** the administrator views the blueprints list, **Then** each blueprint shows its name, version (derived from transaction chain), and publication date.
3. **Given** the system register exists, **When** the administrator navigates to the registers list, **Then** the system register appears as a regular entry with accurate metadata.

---

### User Story 4 - Peer Nodes Replicate the System Register (Priority: P3)

When a new node joins the peer network, it automatically replicates the system register (default on). Blueprint updates published on one node propagate to all peers through normal register replication, ensuring all nodes have the same blueprint catalog.

**Why this priority**: Replication is important for a production network but not required for single-node development or initial bootstrap.

**Independent Test**: Set up two peer nodes. Publish a blueprint on one node. Verify the blueprint appears on the other node's system register through standard replication.

**Acceptance Scenarios**:

1. **Given** two nodes in a peer network, **When** a blueprint is published on node A, **Then** the blueprint transaction replicates to node B through the standard register replication mechanism.
2. **Given** a new node joining a network, **When** the node starts and discovers peers, **Then** it replicates the system register by default (unless explicitly disabled).

---

### Edge Cases

- What happens when the system register genesis creation fails mid-way (e.g., validator service unavailable)? The bootstrapper should retry with backoff and not leave the platform in a partially initialized state.
- What happens when two nodes in a network both attempt to bootstrap the system register simultaneously? The deterministic ID and idempotent creation flow should prevent conflicts.
- What happens when a node has the old separate MongoDB-based system register data? The migration should detect this and bootstrap the real register, ignoring stale data.
- What happens when the genesis docket is sealed but seed blueprint transactions fail to submit? The system register should still show as created (with genesis) but the bootstrapper should retry blueprint submission.
- What happens when querying blueprints on a system register that exists but has no blueprint transactions yet (only genesis)? The API should return an empty blueprint list, not an error.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system register MUST be created as a normal register using the standard two-phase creation flow (initiate/finalize) with the deterministic ID `aebf26362e079087571ac0932d4db973`.
- **FR-002**: The system register genesis transaction MUST contain a control record with the system wallet as the sole Owner attestation.
- **FR-003**: On first startup, the platform MUST automatically bootstrap the system register without manual intervention.
- **FR-004**: Bootstrap MUST be idempotent — restarting the platform when the system register already exists MUST NOT create duplicates or errors.
- **FR-005**: Blueprint publication MUST be implemented as transaction submission to the system register, where the blueprint JSON is the transaction payload.
- **FR-006**: Blueprint versioning MUST use the transaction chain — a new version of a blueprint references the previous version's transaction ID as a predecessor.
- **FR-007**: The two seed blueprints (register-creation-v1, register-governance-v1) MUST be published as transactions on the system register after the genesis docket is sealed.
- **FR-008**: The system register MUST appear in the standard registers list alongside user-created registers.
- **FR-009**: The existing convenience API endpoints (`/api/system-register/blueprints`) MUST continue to function by querying transactions on the real system register.
- **FR-010**: Blueprint publishing MUST require system wallet signature (single signature, no multi-sig quorum for initial implementation).
- **FR-011**: The system register MUST be advertised for peer replication by default.
- **FR-012**: The system register MUST use the display name "Sorcha System Register".
- **FR-013**: The separate MongoDB collection (`sorcha_system_register_blueprints`) and associated repository (`MongoSystemRegisterRepository`) MUST be removed.
- **FR-014**: The `SystemRegisterService` MUST be refactored to query transactions on the real register instead of the separate collection.
- **FR-015**: The admin UI system register page MUST show accurate register metadata (height, transaction count, status) from the real register.
- **FR-016**: Blueprint transactions MUST include metadata identifying them as blueprint publications (e.g., transaction type or metadata tag) to distinguish them from the genesis control record transaction.

### Key Entities

- **System Register**: A register with deterministic ID serving as the platform's public blueprint catalog. Same entity type as any other register.
- **Blueprint Transaction**: A transaction on the system register whose payload contains a blueprint JSON document. Identified by metadata type marker.
- **Blueprint Version Chain**: A sequence of blueprint transactions where each new version references the previous version's transaction ID, forming an auditable version history.
- **Seed Blueprint**: One of the two default blueprints (register-creation-v1, register-governance-v1) published automatically during bootstrap.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fresh platform deployment automatically creates the system register and publishes seed blueprints within 60 seconds of first startup, with no manual intervention.
- **SC-002**: The system register appears in the registers list and is indistinguishable from user-created registers in terms of structure (has genesis, dockets, transactions).
- **SC-003**: Blueprints published to the system register are retrievable via both the convenience API (`/api/system-register/blueprints`) and the standard register transaction query API.
- **SC-004**: Blueprint version chains are traceable — given any blueprint transaction, the full version history can be reconstructed by following predecessor references.
- **SC-005**: The separate MongoDB collection for the system register is fully removed with no remaining references in the codebase.
- **SC-006**: Platform restart after successful bootstrap completes without attempting re-creation or re-publication (idempotent bootstrap verified by no duplicate transactions).
- **SC-007**: All existing unit and integration tests pass after migration (no regressions).
- **SC-008**: The admin UI system register page accurately reflects the real register's state (height, transaction count, blueprint list matches sealed transactions).

## Assumptions

- The system wallet is available and initialized before bootstrap begins (existing `SystemWalletInitializer` handles this).
- The validator service and register service are healthy when bootstrap runs (bootstrapper retries with backoff if not).
- The deterministic system register ID does not collide with any user-generated register ID (SHA-256 derived, collision probability negligible).
- Single-node deployment is the primary target for initial implementation; multi-node peer replication of the system register follows existing register replication patterns.
- The old `MongoSystemRegisterRepository` data can be discarded during migration — no data migration from the separate collection is required.
- Blueprint transaction payloads are stored unencrypted (public blueprints, no field-level encryption needed).

## Scope Boundaries

### In Scope
- System register bootstrap via standard register creation flow
- Blueprint publication as transactions
- Blueprint versioning via transaction chain
- Convenience API wrapper endpoints
- Admin UI updates for real register metadata
- Removal of separate MongoDB storage layer
- Default-on peer replication for system register

### Out of Scope
- Multi-sig quorum governance for blueprint publishing (future work)
- DID-based blueprint references from other registers (future work — use payload directly for now)
- Blueprint schema validation during publishing (future work)
- Blueprint deprecation/deactivation workflow (future work)
- Upgrading governance blueprints in derived registers (acknowledged as future design challenge)
