# Feature Specification: Register Subscription Sync Pipeline

**Feature Branch**: `076-register-subscription-sync`
**Created**: 2026-03-30
**Status**: Draft
**Input**: User description: "Register Subscription Sync Pipeline: When a tenant subscribes to a remote register via the Tenant Service, the Tenant Service should notify the Register Service, which then orchestrates the store creation and peer sync."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Subscribe to a Remote Register and See It Immediately (Priority: P1)

An organisation administrator opens the Registers page, clicks "Subscribe to Register", selects a public register advertised on the peer network, and confirms the subscription. The register appears in their Registers list immediately — initially showing a "Syncing" state — and transitions to a fully usable state once peer replication completes.

**Why this priority**: This is the core bug fix. Currently, subscribing succeeds silently but the register never appears because the data doesn't exist locally. Without this, the subscription feature is effectively broken.

**Independent Test**: Can be fully tested by subscribing to a public register from a second peer node and verifying it appears in the Registers list within seconds, progresses through sync states, and becomes fully browsable once sync completes.

**Acceptance Scenarios**:

1. **Given** an administrator is viewing the Subscribe to Register dialog and a public register is available, **When** they click Subscribe, **Then** the register appears in the Registers list within 2 seconds with a visible "Syncing" indicator.
2. **Given** a subscription has been created and the register stub is visible, **When** the Peer Service completes full replication, **Then** the register transitions to "Online" status with correct name, description, and height.
3. **Given** a subscription has been created, **When** the administrator refreshes the Registers page before sync completes, **Then** the register still appears with its current sync state (not lost between page loads).

---

### User Story 2 - Sync Progress Visibility (Priority: P2)

While a subscribed register is syncing from the peer network, the administrator can see meaningful progress information — the register card shows sync state (e.g., "Syncing", "Active") and transitions in real time via notifications without requiring a manual page refresh.

**Why this priority**: Without progress visibility, administrators cannot tell whether sync is working or stuck. This builds confidence in the subscription flow and reduces support burden.

**Independent Test**: Can be tested by subscribing to a register with existing history (multiple dockets) and observing the UI update in real time as sync progresses, without refreshing the page.

**Acceptance Scenarios**:

1. **Given** a register is in "Syncing" state, **When** the Peer Service reports sync progress, **Then** the Registers page updates the register card status in real time without a page refresh.
2. **Given** a register has completed full replication, **When** the sync finishes, **Then** the register status changes to "Online" and the sync indicator is removed.
3. **Given** a register sync encounters an error (e.g., no peers available), **When** the error is reported, **Then** the register shows an error state with a meaningful message, and the administrator can retry.

---

### User Story 3 - Unsubscribe Cleans Up Sync State (Priority: P3)

When an administrator unsubscribes from a register that is still syncing or fully synced, the system stops replication and removes the local register data so it no longer appears in queries.

**Why this priority**: Cleanup is essential for resource management and user expectations, but is secondary to the subscribe flow working correctly.

**Independent Test**: Can be tested by subscribing to a register, waiting for sync to start, then unsubscribing and verifying the register disappears from the list and sync activity stops.

**Acceptance Scenarios**:

1. **Given** an administrator is subscribed to a remote register that is syncing, **When** they unsubscribe, **Then** the register is removed from the Registers list and the Peer Service stops replicating.
2. **Given** an administrator unsubscribes from a fully synced remote register, **When** the unsubscribe completes, **Then** the local register data is removed and the register no longer appears in queries.

---

### Edge Cases

- What happens when the Peer Service is unavailable when the Register Service tries to initiate sync? The register stub should still appear, and sync should retry automatically when the Peer Service becomes available.
- What happens when no peers are advertising the target register at the time of subscription? The register should remain in a "Waiting for peers" state and begin sync when a peer becomes available.
- What happens when the register is subscribed to but the peer network connection is lost mid-sync? The register should remain in its current sync state and resume when connectivity is restored.
- What happens if the administrator subscribes to a register that is already stored locally (e.g., they own it on another org)? The system should recognise the register exists locally and skip sync, linking the subscription to the existing data.
- What happens if two organisations on the same node subscribe to the same remote register? The second subscription should recognise the register is already syncing or synced locally and share the same register data.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: When the Tenant Service creates a new register subscription, it MUST notify the Register Service of the subscription so the Register Service can initiate local storage and peer sync.
- **FR-002**: The Register Service MUST create a stub register record immediately upon receiving a subscription notification, so the register appears in queries before sync completes.
- **FR-003**: The stub register MUST include the register ID, name (if known from peer advertisement), and a status indicating it is not yet fully synced (e.g., "Syncing").
- **FR-004**: The Register Service MUST instruct the Peer Service to begin replication for the subscribed register after creating the stub.
- **FR-005**: As the Peer Service syncs register data, the Register Service MUST update the local register record with real data (height, description, transactions, dockets).
- **FR-006**: The Register Service MUST emit real-time notifications when a subscribed register's sync state changes, so the UI can update without a page refresh.
- **FR-007**: If the Register Service determines the register already exists locally (e.g., owned by this node), it MUST skip sync and return success immediately.
- **FR-008**: If the notification to the Register Service fails, the Tenant Service subscription MUST still persist — the sync can be retried or reconciled later.
- **FR-009**: When an organisation unsubscribes from a remote register, the system MUST stop active replication via the Peer Service.
- **FR-010**: The Registers list query MUST return stub registers alongside fully-synced registers, with a field indicating sync status, so the UI can display all subscribed registers regardless of sync state.

### Key Entities

- **Stub Register**: A register record created before sync completes. Contains the register ID, name, and a sync-related status. Transitions to a full register once replication finishes.
- **Subscription Notification**: A message from the Tenant Service to the Register Service containing the organisation ID, register ID, and register name, triggering local store creation and peer sync.
- **Register Sync State**: The current replication state of a subscribed register — progressing through stages from initial subscription to fully replicated.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A subscribed register appears in the administrator's Registers list within 3 seconds of clicking Subscribe, regardless of sync completion.
- **SC-002**: A subscribed register with existing history completes full replication and transitions to a fully usable state within the time expected for the data volume (proportional to register size, not a fixed timeout).
- **SC-003**: The UI reflects sync state changes in real time (within 2 seconds of state change) without requiring a manual page refresh.
- **SC-004**: Unsubscribing from a register removes it from the Registers list and stops replication within 5 seconds.
- **SC-005**: If the Register Service or Peer Service is temporarily unavailable during subscription, the system recovers automatically and completes sync without administrator intervention once services are restored.

## Assumptions

- The Peer Service's existing register subscription and sync infrastructure is sufficient for handling the replication triggered by this flow.
- The Register Service's existing register storage can accommodate stub register records with a sync-related status without requiring major schema changes.
- The existing real-time notification infrastructure can be extended to cover sync state transitions.
- The Tenant Service subscription endpoint already correctly handles the subscription record creation — this feature only adds the downstream notification to the Register Service.
- Register name information is available from the peer network advertisements at subscription time and can be passed through to the stub register.
