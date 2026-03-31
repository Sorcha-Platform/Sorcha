# Feature Specification: Register Sync Status Lifecycle & UI Improvements

**Feature Branch**: `078-register-sync-status`
**Created**: 2026-03-31
**Status**: Draft
**Input**: Register sync status lifecycle, subscribed register placeholders, encryption warnings, real-time table updates, and sync timer improvements

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register Status Reflects Sync State (Priority: P1)

As a register administrator who has subscribed to a remote register, I need the register's status to accurately reflect whether my local copy is synced, syncing, or disconnected from the authoritative source, so I can trust the data I'm seeing.

**Why this priority**: Without accurate status, operators cannot distinguish between a fully-synced register and one that is stale or mid-recovery. This is fundamental to data trust in a distributed ledger.

**Independent Test**: Subscribe to a remote register and observe the status transition from Checking through Recovery to Online. Disconnect the source peer and verify status changes to Offline. Reconnect and verify it returns to Online through Checking/Recovery.

**Acceptance Scenarios**:

1. **Given** a user subscribes to a remote register, **When** the subscription is created, **Then** the register appears immediately in the list with status "Checking"
2. **Given** a register is in Checking state, **When** the peer service connects to a source peer and begins pulling dockets, **Then** the status transitions to "Recovery"
3. **Given** a register is in Recovery state, **When** the docket chain is fully synced and live transaction streaming begins, **Then** the status transitions to "Online"
4. **Given** a register is Online, **When** the connection to the master validator copy is lost, **Then** the status transitions to "Offline" after a 30-second grace period
5. **Given** a register is Offline, **When** the peer connection is re-established, **Then** the status transitions to "Checking" then "Recovery" until fully caught up, then "Online"
6. **Given** a register is Online, **When** the source peer briefly disconnects and reconnects within 30 seconds, **Then** the status remains Online (no flapping)

---

### User Story 2 - Real-Time Register Detail Updates (Priority: P2)

As a user viewing a register's transaction or docket list, I want new entries to appear automatically in the tables as they arrive, without needing to click a refresh button or dismiss notification boxes.

**Why this priority**: The current notification boxes ("X new transactions") add friction and break the real-time monitoring experience. Auto-updating tables make the register feel alive and trustworthy.

**Independent Test**: Open a register detail page, submit a new transaction on the source node, and verify the transaction appears in the table within seconds without any user interaction.

**Acceptance Scenarios**:

1. **Given** a user is viewing the transaction table for an Online register, **When** a new transaction is confirmed, **Then** it is prepended to the table automatically
2. **Given** a user is viewing the docket table, **When** a new docket is sealed, **Then** it appears at the top of the docket table automatically
3. **Given** the notification boxes currently exist, **When** this feature is implemented, **Then** the notification boxes are removed entirely
4. **Given** a user has scrolled down in the table, **When** a new entry arrives, **Then** it is added to the top without disrupting the user's scroll position

---

### User Story 3 - Immediate Sync on Subscribe (Priority: P2)

As a user who just subscribed to a register, I want the sync to begin immediately rather than waiting up to 5 minutes for the next periodic timer tick.

**Why this priority**: A 5-minute delay after clicking "Subscribe" with no visible progress erodes user confidence. Immediate sync start provides responsive feedback.

**Independent Test**: Subscribe to a register and verify the sync begins within seconds, not minutes.

**Acceptance Scenarios**:

1. **Given** a user subscribes to a register, **When** the subscription is created, **Then** the initial sync attempt starts within 5 seconds
2. **Given** a subscription is in Syncing state after a failure, **When** the periodic timer elapses, **Then** the retry occurs on schedule (5-minute intervals for retries is acceptable)

---

### User Story 4 - Unencrypted Register Warning (Priority: P3)

As a register administrator, I need to see a clear visual warning when a register is operating without field-level encryption (dev mode), so I can take action to enable encryption before the register enters production use.

**Why this priority**: Running without encryption is acceptable during development but dangerous in production. A persistent visual warning prevents accidental data exposure.

**Independent Test**: View a register that has no encryption policy enabled and verify the warning icon appears. Navigate to the Governance tab and enable encryption via the one-way switch.

**Acceptance Scenarios**:

1. **Given** a register has no field-level encryption enabled, **When** the register list is displayed, **Then** a warning icon with tooltip "Unencrypted - update the policy to enable field-level encryption" is shown
2. **Given** a register has no encryption, **When** the user navigates to the Governance tab, **Then** a "Propose Policy Update" section shows an encryption enable switch
3. **Given** the user toggles the encryption switch, **When** confirming the action, **Then** a dialog warns that encryption cannot be disabled once enabled
4. **Given** encryption has been enabled on a register, **When** the user views the Governance tab, **Then** the encryption switch is locked in the enabled position and cannot be toggled off
5. **Given** a register has encryption enabled, **When** the register list is displayed, **Then** no warning icon is shown

---

### Edge Cases

- What happens when a register oscillates rapidly between Online and Offline (flapping)? The system should debounce status transitions with a 30-second grace period before transitioning to Offline.
- What happens when the source peer disappears permanently? After exhausting retries, the register remains Offline with an error message indicating the source is unreachable.
- What happens when encryption is enabled on a register that already has unencrypted transactions? Existing transactions remain unencrypted. Only new transactions are encrypted. The confirmation dialog should explain this.
- What happens when multiple source peers exist and one goes offline? The register remains Online as long as at least one source peer is reachable.
- What happens when many transactions arrive at once while viewing the table? Entries should be batched and prepended together to avoid excessive re-renders.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST map peer sync states to register statuses: Subscribing/initial → Checking, Syncing → Recovery, FullyReplicated/Active → Online, connection lost → Offline
- **FR-002**: System MUST show subscribed registers immediately in the UI with Checking status, even before the Register Service has created a local stub
- **FR-003**: System MUST debounce Offline transitions with a 30-second grace period to prevent status flapping
- **FR-004**: System MUST automatically prepend new transactions and dockets to their respective tables via real-time events, replacing the current notification box pattern
- **FR-005**: System MUST not disrupt the user's scroll position when new table entries are added
- **FR-006**: System MUST display a warning icon on registers without field-level encryption, with a tooltip explaining the risk
- **FR-007**: System MUST provide a one-way encryption enable switch on the Register Governance tab that cannot be reversed once enabled
- **FR-008**: System MUST show a confirmation dialog before enabling encryption, explaining the irreversible nature and that existing transactions remain unencrypted
- **FR-009**: System MUST trigger an immediate sync attempt when a new subscription is created, bypassing the periodic timer wait
- **FR-010**: System MUST persist the encryption policy state on the register's control chain as a governance transaction
- **FR-011**: System MUST transition from Offline → Checking → Recovery → Online when a source peer reconnects, never directly to Online
- **FR-012**: System MUST batch multiple rapid table updates to avoid excessive UI re-rendering

### Key Entities

- **RegisterStatus**: Enum with values Online, Offline, Checking, Recovery — represents the operational state of a register as seen by this node
- **SyncState-to-Status Mapping**: The bridge between peer service sync lifecycle (Subscribing, Syncing, FullyReplicated, Active, Error) and the user-facing RegisterStatus
- **EncryptionPolicy**: Control-chain governance record indicating whether field-level encryption is required for new transactions. Once set to required, cannot be reverted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Subscribed registers appear in the UI within 2 seconds of clicking Subscribe, with appropriate status indicator
- **SC-002**: Register status accurately reflects sync state — verified by observing all transitions (Checking → Recovery → Online → Offline → Checking) during a controlled peer disconnect/reconnect test
- **SC-003**: New transactions appear in the register detail table within 3 seconds of confirmation, without user interaction
- **SC-004**: Notification boxes for new transactions/dockets are completely removed from the UI
- **SC-005**: Unencrypted registers display a visible warning on the register list that is noticeable without scrolling or hovering
- **SC-006**: Initial sync begins within 5 seconds of subscription creation, not on the next 5-minute timer tick
- **SC-007**: Encryption enable is provably one-way — no UI path or API call can revert it once enabled

## Assumptions

- The existing RegisterStatus enum (Online, Offline, Checking, Recovery) is sufficient — no new enum values needed
- The real-time event infrastructure (SignalR hubs) already pushes transaction and docket events — the change is in the UI handling, not the event pipeline
- Encryption policy is stored as a control-chain transaction on the register, following the existing governance pattern
- The 30-second debounce for Offline transitions is applied at the service level, not the UI
- The register list page and detail page are the only UI surfaces affected
