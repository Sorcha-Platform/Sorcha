# Feature Specification: Resilient System Register Bootstrap

**Feature Branch**: `100-resilient-bootstrap`
**Created**: 2026-04-11
**Status**: Draft
**Input**: User description: "Resilient System Register Bootstrap with Sync-First Strategy"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Node Joins Existing Network (Priority: P1)

An operator deploys a new Sorcha node to join an existing network. The node starts up, discovers peers, and syncs the system register from them. The operator does not need to provide a genesis file because the network already exists. If peers are temporarily unavailable (e.g., network partition, peers restarting), the node keeps retrying rather than giving up after 14 seconds.

**Why this priority**: This is the primary deployment scenario for any multi-node Sorcha network. Without resilient sync, every new node risks creating an orphaned local system register that diverges from the real network.

**Independent Test**: Deploy a fresh Register Service with `BootstrapMode: SyncOnly`, start it before any peers are available, then bring peers online within 5 minutes. The node should eventually sync the system register without operator intervention.

**Acceptance Scenarios**:

1. **Given** a fresh node configured with `BootstrapMode: SyncOnly` and no peers available, **When** the node starts, **Then** it retries peer sync every 5 seconds for the first 2 minutes, then backs off to polling every 5 minutes indefinitely.
2. **Given** a fresh node in SyncOnly mode retrying peer sync, **When** a peer becomes available and responds, **Then** the node syncs the system register and proceeds to seed blueprints.
3. **Given** a fresh node in SyncOnly mode with no genesis file configured, **When** the node exhausts its initial fast retries, **Then** it does NOT fall back to ingesting an embedded genesis file.
4. **Given** a fresh node in SyncOnly mode that has been polling for 30 minutes, **When** the operator checks logs, **Then** log messages decrease in frequency as backoff increases (not flooding every 5 seconds forever).

---

### User Story 2 - First Node Creates New Network (Priority: P2)

An operator runs the genesis ceremony CLI to create a new Sorcha network, then starts the first node with the generated genesis file. The node ingests the genesis immediately without waiting for peers (there are none). This is an explicit, deliberate act requiring configuration.

**Why this priority**: Network creation is a one-time event per network, but it must work reliably and must be clearly distinguished from the "join existing network" flow.

**Independent Test**: Run `sorcha system-register create`, configure the first node with `BootstrapMode: GenesisFile` and the generated genesis path, start the node. It should ingest the genesis and become operational immediately.

**Acceptance Scenarios**:

1. **Given** a node configured with `BootstrapMode: GenesisFile` and a valid genesis file path, **When** the node starts, **Then** it ingests the genesis immediately without attempting peer sync.
2. **Given** a node configured with `BootstrapMode: GenesisFile` and no genesis file at the configured path, **When** the node starts, **Then** it fails with a clear error message identifying the missing file.
3. **Given** a node configured with `BootstrapMode: GenesisFile` and a genesis file with an invalid signature, **When** the node starts, **Then** it fails with a clear error indicating signature verification failure.

---

### User Story 3 - Developer Local Workflow (Priority: P3)

A developer runs `docker-compose up` for local development. The system starts quickly using the embedded dev genesis without requiring any special configuration. This preserves the current developer experience.

**Why this priority**: Developer productivity must not regress. Local development should remain zero-configuration.

**Independent Test**: Run `docker-compose up` with default configuration (no `BootstrapMode` override). System register should be available within 30 seconds.

**Acceptance Scenarios**:

1. **Given** a node with default configuration (`BootstrapMode: Auto`), **When** the node starts with no peers available, **Then** it briefly attempts peer sync (14 seconds), then falls back to ingesting the embedded genesis.
2. **Given** a node with default configuration and the embedded genesis is valid, **When** the genesis is ingested, **Then** the logs clearly indicate "Ingesting embedded genesis — creating a new local network" so the developer understands what happened.
3. **Given** a node with `BootstrapMode: Auto` and peers are available, **When** a peer responds within the initial retry window, **Then** the node syncs from the peer instead of using the embedded genesis.

---

### User Story 4 - Operator Monitors Bootstrap Progress (Priority: P3)

An operator deploying a new node can observe bootstrap progress through structured logs. They can tell whether the node is actively syncing, waiting for peers, or has encountered an error, without needing to guess from silence.

**Why this priority**: Observability during bootstrap reduces support burden and helps operators diagnose network issues.

**Independent Test**: Start a node in SyncOnly mode with no peers. Observe logs over 10 minutes. Verify phase transitions (fast retry -> backoff) are logged with decreasing frequency.

**Acceptance Scenarios**:

1. **Given** a node starting in SyncOnly mode, **When** bootstrap begins, **Then** the log clearly states the configured bootstrap mode and strategy.
2. **Given** a node in the fast-retry phase (first 2 minutes), **When** each retry fails, **Then** the log includes attempt count, next retry interval, and elapsed time.
3. **Given** a node transitioning from fast-retry to backoff phase, **When** the transition occurs, **Then** the log states "Switching to periodic polling every N minutes" so the operator knows the behaviour has changed.
4. **Given** a node polling every 5 minutes, **When** a retry fails, **Then** only one log line is emitted per attempt (not per-second spam).

---

### Edge Cases

- What happens when a node is configured with `BootstrapMode: SyncOnly` but the network has never had a genesis ceremony? The node retries indefinitely — the operator must either switch to `GenesisFile` mode or run the CLI ceremony and reconfigure.
- What happens when the node successfully syncs a system register from a peer but the genesis signature doesn't match the trusted fingerprint? The sync is rejected and the node continues retrying (existing `SystemRegisterSyncVerifier` behaviour).
- What happens when the service is restarted mid-bootstrap? The idempotent check at the top of each retry detects any already-synced register and skips bootstrap entirely.
- What happens when `BootstrapMode` is set to an unrecognised value? The node fails to start with a configuration validation error.
- What happens if peers are available but respond too slowly during the fast-retry phase? The node transitions to the backoff phase and continues attempting — it does not give up.
- What happens if the host is shut down during an indefinite SyncOnly polling loop? The cancellation token is respected and the service shuts down cleanly within one polling interval.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support a `BootstrapMode` configuration with three values: `SyncOnly`, `GenesisFile`, and `Auto`.
- **FR-002**: When `BootstrapMode` is `SyncOnly`, the system MUST attempt peer sync indefinitely without falling back to genesis file ingestion.
- **FR-003**: When `BootstrapMode` is `SyncOnly`, the system MUST use a two-phase retry strategy: fast retries (every 5 seconds for the first 2 minutes) followed by periodic polling (every 5 minutes).
- **FR-004**: When `BootstrapMode` is `GenesisFile`, the system MUST ingest the configured or embedded genesis file immediately without attempting peer sync.
- **FR-005**: When `BootstrapMode` is `Auto`, the system MUST preserve the current behaviour: brief peer sync window (3 retries, exponential backoff), then fall back to genesis file ingestion.
- **FR-006**: The default `BootstrapMode` for docker-compose local development MUST be `Auto` to preserve the existing developer experience.
- **FR-007**: The system MUST log the active bootstrap mode and strategy at startup before the first retry attempt.
- **FR-008**: The system MUST decrease log frequency as retry backoff increases, emitting at most one message per retry attempt during the backoff phase.
- **FR-009**: The system MUST remain responsive to host shutdown signals (cancellation token) during all retry phases including indefinite polling.
- **FR-010**: The system MUST perform an idempotent check for an existing system register before every retry attempt, terminating bootstrap immediately if the register is found (e.g., synced by Peer Service in the background).
- **FR-011**: When `BootstrapMode` is `GenesisFile` and the configured file path does not exist, the system MUST fail with an actionable error message naming the missing path.
- **FR-012**: When `BootstrapMode` is `Auto` and the system falls back to ingesting an embedded genesis, the log MUST clearly indicate that a new local network is being created.
- **FR-013**: The retry intervals for `SyncOnly` mode (fast-retry duration, fast-retry interval, backoff interval) MUST be configurable via settings.
- **FR-014**: The system MUST validate the `BootstrapMode` configuration value at startup and fail immediately with a clear error if the value is unrecognised.

### Key Entities

- **BootstrapMode**: Enumeration controlling bootstrap strategy — `SyncOnly` (wait for peers), `GenesisFile` (ingest immediately), `Auto` (try peers briefly, fall back to file).
- **SystemRegisterOptions**: Extended configuration model carrying `BootstrapMode`, `GenesisFile` path, and retry timing parameters.
- **Bootstrap Phase**: Logical state within the retry loop — `FastRetry` (high frequency, short duration) or `BackoffPolling` (low frequency, indefinite duration).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fresh node in `SyncOnly` mode with no peers available continues retrying for at least 30 minutes without crashing, leaking memory, or flooding logs.
- **SC-002**: A fresh node in `SyncOnly` mode successfully syncs the system register within 30 seconds of a peer becoming available.
- **SC-003**: A fresh node in `GenesisFile` mode ingests a valid genesis and becomes operational within 10 seconds of startup.
- **SC-004**: A fresh node in `Auto` mode (default) completes bootstrap via embedded genesis within 30 seconds, preserving the current developer experience.
- **SC-005**: Log output during the backoff phase does not exceed 1 message per polling interval (no log flooding).
- **SC-006**: All three bootstrap modes are independently testable via configuration alone, without code changes.

## Assumptions

- The Peer Service's `RegisterSyncBackgroundService` continues operating independently. This feature does not add inter-service signalling between Register and Peer services — the Register Service bootstrapper checks for locally-available registers that the Peer Service may have synced in the background.
- The embedded dev genesis resource remains valid and is only used in `Auto` mode.
- The genesis ceremony CLI (`sorcha system-register create`) requires no changes.
- Docker-compose defaults to `Auto` mode. Production deployments (e.g., n1.sorcha.dev) should use `SyncOnly` or `GenesisFile` explicitly.
- Retry timing defaults (5s fast retry, 2 min fast phase, 5 min backoff) are reasonable starting points and can be tuned via configuration.

## Dependencies

- **Feature 099 (Genesis Trust Anchor)**: Provides the genesis ceremony, embedded genesis, and `SystemRegisterBootstrapper` that this feature modifies.
- **Peer Service replication**: The `SyncOnly` mode relies on Peer Service's existing `RegisterSyncBackgroundService` to replicate the system register into local storage.
