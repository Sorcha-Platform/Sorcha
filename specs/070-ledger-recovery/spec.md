# Feature Specification: Blueprint Service Ledger Recovery & Register Status Sync

**Feature Branch**: `070-ledger-recovery`
**Created**: 2026-03-26
**Status**: Draft
**Input**: Blueprint Service loses published blueprint state on restart. Recover from the authoritative ledger and sync register status.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Published Blueprints Survive Service Restart (Priority: P1)

A platform administrator publishes a blueprint to a register. Later, the Blueprint Service restarts (due to deployment, scaling, or crash). After restart, users who are subscribed to that register can still see the blueprint in their "New Submission" page and start new workflow instances — without anyone having to manually re-publish.

**Why this priority**: This is the core problem. Without this, every service restart breaks the entire workflow initiation flow for all users on all registers. It's a production blocker.

**Independent Test**: Publish a blueprint to a register, restart the Blueprint Service, then verify the blueprint appears in the available blueprints list for that register.

**Acceptance Scenarios**:

1. **Given** a blueprint is published to Register A, **When** the Blueprint Service restarts, **Then** within 30 seconds of startup the blueprint is available in the "New Submission" page for users subscribed to Register A.
2. **Given** multiple blueprints are published across multiple registers, **When** the Blueprint Service restarts, **Then** all previously published blueprints are recovered from their respective registers.
3. **Given** a blueprint was published, then a newer version was published to the same register, **When** the service restarts, **Then** only the latest published version is available (matching pre-restart behaviour).

---

### User Story 2 - Register Status Reflects Actual Health (Priority: P1)

When the Blueprint Service starts up or periodically during operation, the system checks whether each known register is reachable and updates its status accordingly. Users see accurate "online" or "offline" indicators for registers, and the system does not attempt to route workflows to unreachable registers.

**Why this priority**: Tied with P1 because recovery depends on querying registers. If a register is unreachable, the system needs to know — both for recovery (skip and retry) and for user-facing status.

**Independent Test**: Start the system with one register offline, verify it shows as offline, bring it online, verify status updates within the refresh interval.

**Acceptance Scenarios**:

1. **Given** a register is reachable, **When** the service performs a health check, **Then** the register status shows as online with current height and last activity timestamp.
2. **Given** a register is unreachable (network error, service down), **When** the service performs a health check, **Then** the register status shows as offline or degraded.
3. **Given** an offline register becomes reachable again, **When** the next periodic refresh occurs, **Then** its status updates to online and any previously-unreachable published blueprints are recovered.

---

### User Story 3 - Service Readiness Gating (Priority: P2)

The Blueprint Service should not accept user traffic until it has completed its recovery process. The API Gateway and health check system should recognise that the service is "starting" (not yet ready) versus "healthy" (recovery complete). This prevents users from seeing empty blueprint lists during the recovery window.

**Why this priority**: Without readiness gating, there's a window after restart where users see no blueprints. With gating, the API Gateway holds traffic until recovery completes, and users experience zero disruption.

**Independent Test**: Restart the Blueprint Service and immediately hit the health endpoint — it should report "not ready" until recovery completes, then switch to "healthy".

**Acceptance Scenarios**:

1. **Given** the Blueprint Service has just started, **When** the health check is queried before recovery completes, **Then** it reports a "starting" or "not ready" status.
2. **Given** recovery completes successfully, **When** the health check is queried, **Then** it reports "healthy" and the API Gateway begins routing traffic.
3. **Given** recovery partially fails (some registers unreachable), **When** recovery for reachable registers completes, **Then** the service becomes ready with recovered data and schedules retries for unreachable registers.

---

### User Story 4 - Graceful Degradation for Unreachable Registers (Priority: P2)

If a register is unreachable during startup recovery, the system should not block indefinitely. It should skip the unreachable register, mark it as offline, complete recovery for all reachable registers, become ready, and retry the unreachable register on a background timer.

**Why this priority**: In a distributed system, partial availability is expected. The service must handle it gracefully rather than failing completely because one register is down.

**Independent Test**: Start with one of three registers offline. Verify the service becomes ready with blueprints from the two online registers. Bring the third online and verify its blueprints appear after the retry interval.

**Acceptance Scenarios**:

1. **Given** 3 registers exist and 1 is unreachable at startup, **When** recovery runs, **Then** the service recovers blueprints from the 2 reachable registers and marks the third as offline.
2. **Given** an unreachable register is marked for retry, **When** the background timer fires, **Then** the system attempts recovery for that register.
3. **Given** a register was unreachable but comes back online, **When** the retry succeeds, **Then** its published blueprints are added to the store and its status updates to online.

---

### User Story 5 - Background Periodic Refresh (Priority: P3)

During normal operation (not just startup), the system periodically re-checks register status and discovers any newly published blueprints. This handles the case where a blueprint is published to a register by another service instance or via peer replication while this instance is running.

**Why this priority**: Handles eventual consistency in a distributed deployment. Less critical than startup recovery but important for multi-instance and peer replication scenarios.

**Independent Test**: With the service running, publish a new blueprint to a register via a different path (e.g., directly via Register Service). Verify it appears in the available blueprints list within the refresh interval.

**Acceptance Scenarios**:

1. **Given** the service is running and healthy, **When** a new blueprint is published to a register, **Then** the service discovers it within the configured refresh interval.
2. **Given** a register goes offline during operation, **When** the periodic refresh detects the failure, **Then** the register status is updated to offline.
3. **Given** the refresh interval is configurable, **When** an administrator sets it to 60 seconds, **Then** the system checks registers every 60 seconds.

---

### Edge Cases

- What happens when the register contains a blueprint-publish transaction for a blueprint ID that no longer exists in the Blueprint Service's draft/template store? The system should still recover it — the published version is self-contained on the ledger.
- What happens when two Blueprint Service instances start simultaneously and both attempt recovery? Recovery is idempotent — both will arrive at the same state. No coordination needed.
- What happens when the register has thousands of transactions? The recovery should filter to blueprint-publish transactions only, not scan the entire ledger.
- What happens when the Tenant Service is unreachable during startup (can't fetch register list)? The system should retry with backoff, using any locally cached register list from a previous run if available.
- What happens during a rolling deployment where the old instance is still running? The old instance has its in-memory state; the new instance recovers from the ledger. Both serve correct data during the transition.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST recover all published blueprint state from the register ledger on service startup, without requiring manual re-publication.
- **FR-002**: The system MUST query each known register for blueprint-publish transactions and rebuild the published blueprint index from the results.
- **FR-003**: The system MUST update register status (online, offline, degraded) based on the success or failure of recovery queries.
- **FR-004**: The system MUST report "not ready" on its health check until the initial recovery process completes for all reachable registers.
- **FR-005**: The system MUST gracefully handle unreachable registers by skipping them, completing recovery for reachable registers, and scheduling retries.
- **FR-006**: The system MUST periodically refresh register status and discover newly published blueprints during normal operation.
- **FR-007**: The refresh interval MUST be configurable by the platform administrator.
- **FR-008**: Recovery MUST be idempotent — processing the same ledger transactions multiple times produces the same published blueprint state.
- **FR-009**: The system MUST handle blueprint version ordering correctly — if multiple versions of a blueprint are published, only the latest version should be active.

### Key Entities

- **Published Blueprint Index**: The in-memory mapping of register ID → list of published blueprints. Rebuilt from ledger on startup, refreshed periodically.
- **Register Status**: Per-register health state (online, offline, degraded) with height, last checked timestamp, and consecutive failure count.
- **Blueprint-Publish Transaction**: A ledger transaction recording that a specific blueprint version was published to a specific register. Contains the full blueprint definition.
- **Recovery State**: Tracks which registers have been successfully recovered, which are pending retry, and overall readiness.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After a service restart, all previously published blueprints are available to users within 30 seconds (for up to 10 registers with up to 100 published blueprints total).
- **SC-002**: Register status accuracy is 100% — if a register is reachable, it shows as online; if unreachable, it shows as offline. No stale status persists beyond one refresh interval.
- **SC-003**: Users experience zero disruption during planned service restarts — the health gate prevents empty-state responses.
- **SC-004**: Unreachable registers do not block service readiness — the service becomes ready within 30 seconds even if some registers are down.
- **SC-005**: Newly published blueprints are discoverable by all service instances within the configured refresh interval (default: 60 seconds).

## Assumptions

- Blueprint-publish transactions on the ledger contain the complete blueprint definition (not just a reference). The recovery process does not need to fetch blueprint definitions from a separate store.
- The Register Service provides a way to query transactions by type (e.g., blueprint-publish) or the recovery process can filter from the full transaction list. If a dedicated query doesn't exist, adding one is in scope.
- The list of known registers can be obtained from the Register Service's register list endpoint or from subscription data in the Tenant Service. Both are available at startup.
- The default refresh interval is 60 seconds. This is a reasonable balance between freshness and load. It is configurable.
- The health check currently returns a simple healthy/unhealthy status. Adding a "starting" or "not ready" state is in scope.
- In a multi-instance deployment, each instance independently recovers from the ledger. No leader election or distributed coordination is needed because recovery is idempotent.
