# Feature Specification: Envelope Encryption Integration

**Feature Branch**: `052-encryption-integration`
**Created**: 2026-03-06
**Status**: Draft
**Input**: Wire the existing async encryption pipeline in Blueprint Service to the UI and CLI with full progress tracking, retry, operation history, and notifications.

## User Scenarios & Testing

### User Story 1 - Action Submission with Encryption Progress (Priority: P1)

A workflow participant submits an action that involves multiple recipients with encrypted data. Instead of waiting with no feedback, they see real-time progress showing the encryption stage, percentage complete, and recipient count. When encryption finishes, they see a success confirmation with the resulting transaction reference.

**Why this priority**: This is the core gap — the encryption pipeline runs but users have no visibility into it. Without this, async encryption appears to hang or silently fail.

**Independent Test**: Can be tested by submitting an action on a multi-recipient blueprint and observing that progress updates appear in real time, culminating in a success or failure message.

**Acceptance Scenarios**:

1. **Given** a participant has a pending action on a blueprint with 3+ recipients, **When** they submit the action with valid data, **Then** the system immediately acknowledges the submission and displays an inline progress indicator showing the current encryption stage and percentage.
2. **Given** an encryption operation is in progress, **When** the backend completes each stage (key resolution, encryption, transaction building, signing), **Then** the progress indicator updates in real time without requiring a page refresh.
3. **Given** an encryption operation completes successfully, **Then** the progress indicator shows 100% complete, displays the resulting transaction reference, and the action is removed from the participant's pending list.
4. **Given** an encryption operation is in progress, **When** the participant's browser loses its real-time connection, **Then** the system falls back to periodic status checks and continues displaying progress without interruption.

---

### User Story 2 - Encryption Failure and Retry (Priority: P2)

When encryption fails (e.g., a recipient's public key cannot be resolved or a network error occurs during signing), the participant sees a clear error message identifying what failed and a retry button. Clicking retry re-submits the same action without requiring the participant to re-enter data.

**Why this priority**: Failures are inevitable in distributed systems. Without retry, users must re-navigate to the action form, re-enter data, and re-submit — a frustrating experience that may lead to abandoned workflows.

**Independent Test**: Can be tested by simulating an encryption failure (e.g., unreachable key service) and verifying the error message and retry button appear, and that retry successfully completes the operation.

**Acceptance Scenarios**:

1. **Given** an encryption operation fails during any stage, **Then** the progress indicator displays an error message identifying the failure reason and the affected recipient (if applicable).
2. **Given** a failed encryption operation, **When** the participant clicks the retry button, **Then** the system re-submits the original action data as a new operation and displays fresh progress.
3. **Given** a failed encryption operation, **When** the participant clicks retry and the transient issue has been resolved, **Then** the operation completes successfully.
4. **Given** a failed encryption operation, **When** the participant chooses not to retry, **Then** the original action remains in their pending list for future attempts.

---

### User Story 3 - Cross-Page Completion Notifications (Priority: P3)

A participant submits an action that triggers encryption, then navigates to a different page while encryption runs. They see a brief informational banner noting that encryption is in progress. When the operation completes (success or failure), they receive a notification regardless of which page they are on.

**Why this priority**: Long encryption operations (many recipients, large payloads) may take tens of seconds. Users should not be forced to stare at a progress bar — they need freedom to continue working with confidence they will be notified.

**Independent Test**: Can be tested by starting an encryption operation, navigating to a different page, and verifying a notification appears when the operation completes.

**Acceptance Scenarios**:

1. **Given** an encryption operation is in progress, **When** the participant navigates away from the actions page, **Then** a non-blocking banner appears briefly informing them that encryption is in progress and they will be notified on completion.
2. **Given** the participant is on any page in the application, **When** a background encryption operation completes successfully, **Then** a toast notification appears with the transaction reference and a link to view the result.
3. **Given** the participant is on any page, **When** a background encryption operation fails, **Then** a toast notification appears with the error summary and a link to retry or view details.
4. **Given** the participant closes and reopens the application after an operation completed in the background, **When** they visit the operations page, **Then** they can see the completed or failed operation in their history.

---

### User Story 4 - Operations History (Priority: P4)

An administrator or participant can view a history of their encryption operations, including status, timestamps, associated workflow, and resulting transaction references. This provides an audit trail and a way to check on operations that may have completed while the user was away.

**Why this priority**: Operational visibility is essential for troubleshooting and audit. Without history, users have no way to review past operations or diagnose repeated failures.

**Independent Test**: Can be tested by submitting several actions (some succeeding, some failing), then navigating to the operations page and verifying all operations appear with correct status and details.

**Acceptance Scenarios**:

1. **Given** a participant has submitted multiple actions that triggered encryption, **When** they navigate to the operations page, **Then** they see a list of all their operations ordered by most recent first.
2. **Given** the operations list, **Then** each entry shows: operation status (in progress, completed, failed), workflow name, action name, recipient count, start time, and completion time.
3. **Given** a completed operation, **When** the participant clicks on it, **Then** they see the transaction reference with a link to the register entry.
4. **Given** a failed operation, **When** the participant clicks on it, **Then** they see the error details and a retry button.
5. **Given** many operations exist, **When** the participant scrolls the list, **Then** additional operations load progressively (pagination).

---

### User Story 5 - CLI Async Operation Support (Priority: P5)

A developer or CI pipeline submits an action via the command-line tool that triggers encryption. By default, the CLI displays a progress indicator and waits for completion before returning. Alternatively, a flag allows immediate return with the operation identifier for later status checks.

**Why this priority**: CLI users and automated pipelines need predictable behavior. Blocking by default is intuitive; non-blocking mode enables scripting and CI integration.

**Independent Test**: Can be tested by running the CLI action submission command against a multi-recipient blueprint and verifying the progress display and exit behavior in both blocking and non-blocking modes.

**Acceptance Scenarios**:

1. **Given** a CLI user submits an action that triggers async encryption, **When** no special flags are provided, **Then** the CLI displays a progress indicator showing encryption stage and percentage, and waits until the operation completes before returning.
2. **Given** the CLI is in blocking mode, **When** the operation completes successfully, **Then** the CLI prints the transaction reference and exits with code 0.
3. **Given** the CLI is in blocking mode, **When** the operation fails, **Then** the CLI prints the error details and exits with a non-zero exit code.
4. **Given** a CLI user submits an action with the non-blocking flag, **Then** the CLI immediately prints the operation identifier and exits with code 0.
5. **Given** the user has an operation identifier, **When** they run the existing operation status command, **Then** they see the current status of that operation.

---

### User Story 6 - Real-Time Progress via Push Updates (Priority: P6)

The system delivers encryption progress updates to connected clients in real time via push notifications (not just polling). This reduces latency between backend progress and UI display, and reduces unnecessary network traffic from periodic polling.

**Why this priority**: Polling every 2 seconds adds latency and unnecessary load. Push updates make the progress feel instantaneous and are more efficient. However, the polling fallback (US1) already provides basic functionality, making this an enhancement.

**Independent Test**: Can be tested by connecting to the real-time channel, submitting an action, and verifying that progress events arrive without any polling requests being made.

**Acceptance Scenarios**:

1. **Given** a participant is connected to the real-time notification channel, **When** an encryption operation progresses through stages, **Then** the participant receives push updates for each stage transition within 500ms.
2. **Given** a participant's real-time connection drops, **Then** the system automatically falls back to polling without any user intervention or visible disruption.
3. **Given** a participant reconnects after a temporary disconnection, **Then** they receive the current operation status immediately upon reconnection.

---

### Edge Cases

- What happens when the user submits the same action twice rapidly before the first response arrives? The system must prevent duplicate operations through idempotency.
- What happens when the backend service restarts mid-encryption? The operation should be marked as failed, and the user should be able to retry.
- What happens when the encrypted transaction exceeds the maximum size limit? The system should fail fast during the pre-flight check and display a clear error explaining the size constraint.
- What happens when a user has no active wallet configured? The action submission should be prevented with a clear message before any encryption begins.
- What happens when the operations history grows very large (100+ operations)? The list should paginate and remain responsive.
- What happens when the user's session expires during a long encryption operation? The operation continues server-side, but the user must re-authenticate to view its status or retry.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST return an operation identifier when an action submission triggers asynchronous encryption.
- **FR-002**: The system MUST display an inline progress indicator on the actions page showing the current encryption stage, percentage complete, and recipient progress (e.g., "3 of 5 recipients").
- **FR-003**: The system MUST update the progress indicator in real time as the encryption operation progresses through stages (key resolution, encryption, transaction building, signing/submission).
- **FR-004**: The system MUST display a clear error message when an encryption operation fails, identifying the failure reason and affected recipient if applicable.
- **FR-005**: The system MUST provide a retry mechanism that re-submits the original action data without requiring the user to re-enter form data.
- **FR-006**: The system MUST deliver a notification to the user when a background encryption operation completes (success or failure), regardless of which page the user is currently viewing.
- **FR-007**: The system MUST display a non-blocking informational banner when the user navigates away from the actions page while an encryption operation is in progress.
- **FR-008**: The system MUST provide an operations history page listing all encryption operations for the user's wallet(s), showing status, timestamps, workflow context, and results.
- **FR-009**: The operations history MUST support pagination for users with many operations.
- **FR-010**: The system MUST support real-time push delivery of encryption progress events to connected clients, with automatic fallback to periodic status polling when the real-time connection is unavailable.
- **FR-011**: The CLI MUST display a progress indicator and wait for completion by default when an action triggers async encryption.
- **FR-012**: The CLI MUST support a non-blocking mode (via flag) that immediately returns the operation identifier.
- **FR-013**: The existing CLI operation status command MUST work with encryption operation identifiers.
- **FR-014**: The system MUST prevent duplicate operations through idempotency when the same action is submitted multiple times.
- **FR-015**: The system MUST fail fast with a clear error when the estimated encrypted payload exceeds the maximum transaction size, before beginning encryption.
- **FR-016**: Failed operations MUST leave the original action in the user's pending list so they can retry or re-submit later.

### Key Entities

- **Encryption Operation**: Represents a single async encryption job. Has a unique identifier, status (queued, in-progress stages, completed, failed), associated workflow and action, submitting wallet, recipient count, progress percentage, error details, and timestamps (created, completed).
- **Operation Notification**: A transient event delivered to connected clients when an operation changes state. Contains operation identifier, new status, progress details, and (on completion) the transaction reference or error.
- **Action Submission Result**: The response returned to the user after submitting an action. Includes the operation identifier and async flag when encryption is triggered, or the immediate transaction reference for synchronous operations.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Users see encryption progress updates within 1 second of each backend stage transition when connected via real-time channel.
- **SC-002**: Users can retry a failed encryption operation with a single click, without re-entering any form data.
- **SC-003**: 100% of completed or failed background operations produce a visible notification to the user within 5 seconds of completion, regardless of which page they are viewing.
- **SC-004**: The operations history page loads within 2 seconds for users with up to 200 operations.
- **SC-005**: CLI users in blocking mode see their first progress update within 3 seconds of submission and receive the final result within 5 seconds of backend completion.
- **SC-006**: CLI users in non-blocking mode receive the operation identifier and return to the shell prompt within 1 second of submission.
- **SC-007**: The system handles 10 concurrent encryption operations per user without degradation in progress reporting accuracy or notification delivery.
- **SC-008**: When the real-time connection is unavailable, the polling fallback delivers progress updates with no more than 3 seconds of additional latency compared to real-time delivery.

## Assumptions

- The Blueprint Service async encryption pipeline (background service, channel-based work queue, 4-step progress, polling endpoint) is already fully implemented and operational.
- The UI encryption progress indicator component and operation status service already exist and are functionally correct — they just need to be connected to the action submission flow.
- The existing idempotency mechanism in the Blueprint Service prevents duplicate encryption operations from the same action submission.
- The maximum transaction size limit (currently 4MB) is enforced by the backend pre-flight check and does not need UI-side validation.
- Operation history retention follows the same data retention policy as other system records (no special purge rules needed for this feature).
- The existing CLI operation status command requires no changes to support encryption operation identifiers — only the action execution command needs async awareness.

## Out of Scope

- Changes to the encryption algorithm or key management infrastructure.
- Partial delivery (encrypting for some recipients and skipping failed ones) — operations are atomic (all or nothing).
- Admin-level operation monitoring across all users (this feature is scoped to a user viewing their own operations).
- Offline/PWA support for operation tracking.
- Batching multiple action submissions into a single encryption operation.
