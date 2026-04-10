# Feature Specification: System Register Genesis Trust Anchor

**Feature Branch**: `099-genesis-trust-anchor`
**Created**: 2026-04-10
**Status**: Draft
**Input**: User description: "System Register Genesis Trust Anchor - offline genesis ceremony CLI, pre-signed genesis block ingestion, peer sync verification against trust anchor, and modified bootstrap flow."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Genesis Ceremony (Priority: P1)

A network operator runs a scripted ceremony to produce a pre-signed genesis block for a new Sorcha network. The ceremony generates cryptographic key material, signs the genesis transaction, and outputs two files: a genesis file (public, distributable) and a validator key file (private, to be secured). The operator embeds the genesis file into the codebase for automatic inclusion in builds, and secures or destroys the private key after importing it into the first validator.

**Why this priority**: Without the genesis ceremony, no network can bootstrap. This is the foundational step that all other stories depend on.

**Independent Test**: Run the ceremony CLI command and verify it produces a valid, self-consistent genesis file with correct signatures, deterministic register ID, and a separate validator key file.

**Acceptance Scenarios**:

1. **Given** an operator with the CLI tool installed, **When** they run the genesis creation command with a network identifier, **Then** the system produces a genesis file containing a signed control record with a validator roster and a separate validator key file in the current directory.
2. **Given** a genesis file has been created, **When** the operator runs the verification command against it, **Then** the system confirms all signatures are valid and displays the public key fingerprint and validator roster.
3. **Given** a genesis file has been tampered with (modified payload or signature), **When** the operator runs the verification command, **Then** the system reports signature verification failure and exits with a non-zero code.
4. **Given** the ceremony is run twice with different invocations, **When** comparing the outputs, **Then** each genesis file has different signatures and keys (unique keypair per ceremony) but the same deterministic system register ID.

---

### User Story 2 - First Instance Bootstrap (Priority: P1)

The first validator instance on a new network starts up, loads the pre-signed genesis file, imports the validator key, and seals the genesis docket. The system register becomes operational with blueprints seeded. No genesis is ever created at runtime.

**Why this priority**: This is the network bootstrap path. Without it, no Sorcha network can start operating.

**Independent Test**: Start a single instance with a valid genesis file and imported validator key, verify the system register is created and blueprints are seeded.

**Acceptance Scenarios**:

1. **Given** a valid genesis file is configured (via config path or embedded resource), **When** the instance starts with no existing system register and the local validator key matches the roster, **Then** the genesis transaction is ingested, the genesis docket is sealed, and default blueprints are published.
2. **Given** a valid genesis file is configured, **When** the instance starts but the local validator key is NOT in the genesis validator roster, **Then** the service logs a clear message indicating the validator key must be imported and stops.
3. **Given** no genesis file is configured and no embedded default exists, **When** the instance starts, **Then** the service logs a message directing the operator to run the genesis ceremony and stops.
4. **Given** the system register already exists locally with a matching genesis signature, **When** the instance restarts, **Then** bootstrap skips creation and proceeds normally (idempotent).

---

### User Story 3 - Joining Instance Peer Sync (Priority: P1)

A new instance joins an existing network. It discovers peers, syncs the system register from them, and verifies the genesis transaction against its own trust anchor (genesis file) before accepting the data. The instance becomes fully operational without any manual genesis or key import steps.

**Why this priority**: Multi-instance operation is the core problem this feature solves. Without verified peer sync, each instance remains an island.

**Independent Test**: Start a second instance pointed at the same genesis file and a running peer, verify it syncs the system register and rejects peers with non-matching genesis.

**Acceptance Scenarios**:

1. **Given** a new instance with a matching genesis file and at least one operational peer, **When** the instance starts, **Then** it syncs the system register from the peer after verifying the genesis transaction signature matches the trust anchor.
2. **Given** a new instance with a matching genesis file, **When** a peer offers a system register with a different genesis signature, **Then** the instance rejects the sync and logs the expected vs actual fingerprint.
3. **Given** a new instance with a matching genesis file but no reachable peers, **When** the instance starts, **Then** it attempts to ingest the local genesis file and follows the bootstrap flow (seal if rostered, stop if not).

---

### User Story 4 - Genesis File Verification (Priority: P2)

An operator receives a genesis file from another party (e.g., joining a third-party network) and wants to verify its authenticity before deploying. They run the verification command to inspect the file's signatures, validator roster, and network identity.

**Why this priority**: Important for the commissioning model but not required for initial single-network operation.

**Independent Test**: Run verification against known-good and known-bad genesis files, confirm correct pass/fail behaviour.

**Acceptance Scenarios**:

1. **Given** a valid genesis file, **When** the operator runs the verify command, **Then** the system displays the network ID, public key fingerprint, validator roster details, and confirms all signatures are valid.
2. **Given** an invalid or corrupted genesis file, **When** the operator runs the verify command, **Then** the system identifies which signature or field failed verification and exits with a non-zero code.

---

### User Story 5 - Validator Key Import (Priority: P2)

The operator of the first validator instance imports the genesis validator key into the local Wallet Service so the validator can sign dockets as the rostered genesis validator. This is a one-time operation after which the key file can be secured or destroyed.

**Why this priority**: Required for the first instance to seal the genesis docket, but is a one-time operational step.

**Independent Test**: Import a validator key file into a running Wallet Service, verify the validator can subsequently seal dockets.

**Acceptance Scenarios**:

1. **Given** a valid validator key file and a running Wallet Service, **When** the operator runs the import command, **Then** the key is imported and the validator can sign dockets using the rostered genesis key.
2. **Given** an invalid or corrupted key file, **When** the operator runs the import command, **Then** the system reports the error and does not import any key material.
3. **Given** a key that has already been imported, **When** the operator runs the import command again, **Then** the operation is idempotent (no error, no duplicate).

---

### Edge Cases

- What happens when two instances race to ingest the same genesis file simultaneously? Each ingests independently; the first to seal the docket wins, the second syncs from the first via peer replication.
- What happens when the embedded genesis file and a config-path genesis file both exist but differ? Config-path takes precedence. The embedded default is only used when no config path is specified.
- What happens when a validator key is imported but the genesis file hasn't been loaded yet? The bootstrapper loads the genesis file first, then checks whether the local validator is rostered. Key import order doesn't matter as long as both are present before the bootstrapper's seal step.
- What happens when the genesis file version field doesn't match the expected version? The system rejects the file with a clear error about unsupported genesis format version.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a CLI command to generate a pre-signed system register genesis block offline, without requiring any running services.
- **FR-002**: The genesis ceremony MUST produce two separate output files: a distributable genesis file (public data only) and a validator key file (private key material).
- **FR-003**: The genesis file MUST contain the signed genesis transaction, validator roster, network identifier, and genesis public key fingerprint.
- **FR-004**: The system register ID in the genesis MUST be deterministic (derived from SHA-256 of "sorcha-system-register"), matching the existing system register constant.
- **FR-005**: System MUST provide a CLI command to verify a genesis file's signatures and display its contents.
- **FR-006**: System MUST provide a CLI command to import a genesis validator key into the Wallet Service.
- **FR-007**: On startup, if the system register does not exist locally, the instance MUST attempt to sync it from peers before any local ingestion.
- **FR-008**: When syncing the system register from peers, the instance MUST verify the genesis transaction signature against the trusted public key from the configured or embedded genesis file.
- **FR-009**: If the genesis signature from a peer does not match the trust anchor, the instance MUST reject the sync and log the expected vs actual fingerprint.
- **FR-010**: If no peers have the system register, the instance MUST load and ingest the pre-signed genesis from the configured file path or embedded resource.
- **FR-011**: After ingesting the genesis, the instance MUST check whether the local validator's docket-signing key is in the validator roster. If it is rostered, proceed to seal. If not, stop with a clear log message.
- **FR-012**: If no genesis file is found (no config path, no embedded resource) and no peers have the system register, the instance MUST stop with a log message directing the operator to run the genesis ceremony.
- **FR-013**: Instances MUST never create a system register genesis at runtime. The genesis is always an externally produced artifact.
- **FR-014**: The genesis file MUST be loadable from a configurable file path with fallback to an embedded assembly resource.
- **FR-015**: The genesis file MUST be embeddable as an assembly resource so it is automatically included in build artifacts and container images.
- **FR-016**: Genesis signature verification for system register sync MUST only apply to the system register. All other registers MUST sync using the existing trust model.
- **FR-017**: The genesis file MUST include a version field to support future format evolution.
- **FR-018**: The genesis file MUST include a human-readable network identifier that is logged on instance startup.

### Key Entities

- **SystemRegisterGenesis**: The complete genesis file containing version, network ID, signed genesis transaction, validator roster, and public key fingerprint.
- **GenesisTransaction**: The signed control record with transaction ID, encoded payload, and cryptographic signature (public key, signature value, algorithm, timestamp).
- **GenesisValidatorKey**: The private key material output by the ceremony, used for one-time import into the first validator.
- **SystemRegisterOptions**: Configuration for the genesis file path setting, used to resolve the trust anchor on startup.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can create a new Sorcha network (ceremony + first instance bootstrap) in under 5 minutes using documented CLI commands.
- **SC-002**: A new instance joining an existing network syncs the system register from peers and becomes operational without any manual genesis or key steps.
- **SC-003**: An instance presented with a system register genesis signed by a different key rejects it 100% of the time with a clear diagnostic log message.
- **SC-004**: The genesis ceremony produces deterministic, verifiable output — running the verify command on a ceremony output always succeeds.
- **SC-005**: An instance with no genesis file and no reachable peers stops cleanly with an actionable log message rather than creating an unauthorised system register.
- **SC-006**: Different environments (dev, staging, prod) can each operate independent networks by deploying with different genesis files, with no cross-network contamination.

## Assumptions

- ED25519 is the default signing algorithm for the genesis ceremony. Other algorithms can be specified via CLI option.
- The existing cryptography library can perform all required signing and verification operations without a running Wallet Service.
- The existing peer replication protocol and docket finalization service provide sufficient infrastructure for system register sync. Only the genesis signature verification is new.
- The existing governance proposal mechanism is sufficient for post-genesis validator management. No changes to governance are required.
- Blueprint seeding (register-creation-v1, register-governance-v1, create-organisation-v1) continues to run after genesis confirmation, unchanged from current behaviour.
