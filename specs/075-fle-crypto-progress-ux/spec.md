# Feature Specification: Field-Level Encryption Completion & Crypto Progress UX

**Feature Branch**: `075-fle-crypto-progress-ux`
**Created**: 2026-03-29
**Status**: Draft
**Input**: Complete field-level encryption implementation (test gaps from spec 065), enhance backend to emit per-recipient progress events, and build a floating popover UI that gives users task-oriented feedback during long-running encryption operations.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - User Sees Per-Recipient Progress During Action Submission (Priority: P1)

A user submits an action on a workflow. Because the register has encryption enabled, the system encrypts the payload for each authorised recipient. Instead of a vague spinner, the user sees a floating progress panel showing which recipients are being prepared (by name and disclosed fields), which are complete, and which are waiting. The panel uses task-oriented language ("Securing your submission", "Preparing for ID Department — all fields") rather than cryptographic jargon.

**Why this priority**: This is the core UX improvement. Without per-recipient feedback, users see a meaningless progress bar during a potentially multi-second wait. The existing `EncryptionProgressIndicator` component shows 4 coarse steps — this replaces it with a rich, task-oriented view that communicates *who* can see *what*, reinforcing the DAD disclosure model.

**Independent Test**: Can be tested by submitting an action on a non-DevMode register with 3+ disclosure recipients, verifying the floating panel appears with per-recipient status updates (waiting → encrypting → secured), and confirming completion shows a success message with a transaction link.

**Acceptance Scenarios**:

1. **Given** a user submits an action on an encrypted register with 3 recipients, **When** the encryption pipeline begins, **Then** a floating panel appears (bottom-right, approximately 340px wide) showing "Securing your submission" with a per-recipient list showing participant names and their disclosed field summaries.
2. **Given** the encryption pipeline is processing, **When** each recipient's key wrapping completes, **Then** that recipient's row transitions from "waiting" to "encrypting" to "secured" in real-time via SignalR push events.
3. **Given** the floating panel is visible, **When** the user clicks the minimise button, **Then** the panel collapses to a compact pill showing "Securing — 2/3 recipients" with a mini progress bar, and clicking the pill expands it back to the full panel.
4. **Given** the floating panel is visible, **When** the user clicks the dismiss button, **Then** the panel disappears entirely and a toast notification appears when the operation completes (success or failure).
5. **Given** the floating panel is visible, **When** the user navigates to a different page, **Then** the panel persists and continues showing progress on the new page.
6. **Given** the encryption completes successfully, **When** the success state is shown (either in-panel or as toast), **Then** it displays "Submission secured — 3 recipients can now access their disclosed fields" with a "View transaction" link to the transaction explorer.

---

### User Story 2 - Backend Emits Per-Recipient Encryption Progress (Priority: P1)

The encryption pipeline emits a SignalR notification after each recipient's key wrapping completes, including the recipient's display name, their disclosed fields summary, and their processing status. This replaces the current 4-step coarse progress (10/30/60/80%) with fine-grained per-recipient events while retaining the overall pipeline step notifications.

**Why this priority**: Co-equal with US1 — the UI cannot show per-recipient progress without backend events to drive it. The current `EncryptionProgressNotification` model lacks recipient-level detail.

**Independent Test**: Can be tested by submitting an action with 5 recipients, subscribing to the ActionsHub SignalR connection, and verifying that 5 individual recipient progress events are received (one per recipient) in addition to the existing pipeline step events.

**Acceptance Scenarios**:

1. **Given** an encryption pipeline is processing an action with 5 recipients across 3 disclosure groups, **When** the encryption step processes each group, **Then** individual recipient events are emitted for each recipient in each group, containing recipient name, disclosed field paths, and status (encrypting, secured, failed).
2. **Given** a recipient progress event is emitted, **When** the event payload is inspected, **Then** it contains the operation ID, recipient display name, disclosed field summary (e.g., "all fields", "decision, site details"), recipient index, total recipient count, and status.
3. **Given** the existing 4-step pipeline events continue to be emitted, **When** a recipient event is compared with a step event, **Then** recipient events contain a step reference indicating which pipeline step they belong to (step 2: encryption).
4. **Given** the polling fallback endpoint is queried, **When** the operation is in progress, **Then** the response includes per-recipient status in addition to the existing operation-level fields.

---

### User Story 3 - DevMode Unit Test Coverage (Priority: P2)

The DevMode per-register feature (spec 065, US3) has implementation complete but lacks unit test coverage. Tests must verify that DevMode registers store plaintext payloads, that the DevMode toggle endpoint works correctly, and that the plaintext path selection bypasses the encryption pipeline while still enforcing disclosure filtering at read time.

**Why this priority**: DevMode is the foundation for incremental FLE development. Without test coverage, there is no regression safety net. The implementation is already merged — this is gap-filling, not new feature work.

**Independent Test**: Can be tested by running the unit test suite for register initiation with DevMode, DevMode toggle, and plaintext path selection — all must pass in isolation without Docker.

**Acceptance Scenarios**:

1. **Given** a register initiation request with `devMode: true`, **When** the register is created, **Then** the register record has `DevMode = true` stored.
2. **Given** an active register, **When** an administrator sends a toggle request to enable/disable DevMode, **Then** the register's DevMode flag is updated and only administrators with the `CanManageRegisters` permission can toggle it.
3. **Given** a DevMode register, **When** an action payload is submitted, **Then** the encryption pipeline is bypassed and the payload is stored as plaintext.
4. **Given** a DevMode register with a stored plaintext payload, **When** a participant queries their actions, **Then** disclosure rules are still applied to filter which fields they can see.

---

### User Story 4 - Field-Level Encryption Unit Test Coverage (Priority: P2)

The field-level encryption feature (spec 065, US4) has the `EncryptionPipelineService` verified as feature-complete but lacks targeted unit tests for disclosure group encryption and recipient key resolution from instance bindings and published register records. These tests close the coverage gap for the core encryption path.

**Why this priority**: The encryption pipeline is the most security-critical path in the system. Untested code in the encryption layer is a liability. Like US3, implementation exists — tests provide the safety net.

**Independent Test**: Can be tested by running unit tests for disclosure group encryption (verifying correct group formation, ciphertext count, and wrapped key distribution) and recipient key resolution (verifying bound participants, register participants, and revoked participant handling).

**Acceptance Scenarios**:

1. **Given** 2 recipients with identical disclosure field sets, **When** disclosure group encryption is performed, **Then** exactly 1 ciphertext group is created with 2 wrapped keys.
2. **Given** 2 recipients with different disclosure field sets, **When** disclosure group encryption is performed, **Then** 2 ciphertext groups are created, each with 1 wrapped key.
3. **Given** a recipient whose key resolution fails due to revocation, **When** encryption is attempted, **Then** the entire operation fails atomically with a clear error identifying the revoked participant.
4. **Given** a recipient with a published participant record on the register, **When** key resolution is performed, **Then** the public key is resolved from the register's published record.
5. **Given** a recipient bound during the starting action (instance binding), **When** key resolution is performed, **Then** the public key is resolved from the instance binding data.

---

### User Story 5 - Encryption Failure Gives Actionable Feedback (Priority: P2)

When encryption fails (key resolution failure, recipient revocation, size limit exceeded), the user sees a clear, actionable error in the floating panel or as a toast. The error identifies what went wrong, which recipient caused the failure (if applicable), and offers a retry action.

**Why this priority**: Error handling is essential for a production-quality encryption UX. Without actionable errors, users hit a dead end when encryption fails and have no path to resolution.

**Independent Test**: Can be tested by simulating a key resolution failure for one recipient, verifying the floating panel shows the error with the failing recipient's name, and confirming the retry button resubmits the action.

**Acceptance Scenarios**:

1. **Given** an encryption operation fails because a recipient's participant record was revoked, **When** the failure is displayed, **Then** the error message names the recipient and states "participant record may be revoked" with a "Retry" action and a "Details" link.
2. **Given** an encryption operation fails because the encrypted payload exceeds the size limit, **When** the failure is displayed, **Then** the error message states the size limit was exceeded and suggests reducing payload size.
3. **Given** an error toast or panel with a "Retry" button, **When** the user clicks Retry, **Then** the action is resubmitted and a new encryption operation begins with fresh progress tracking.

---

### User Story 6 - Docker E2E Validation of Full Encryption Flow (Priority: P3)

The full field-level encryption flow — from action submission through encrypted storage to per-participant decryption — is validated end-to-end in the Docker Compose environment. This covers the council credential workflow with DevMode disabled, verifying that payloads are encrypted in the database and each participant can only decrypt their authorised fields.

**Why this priority**: E2E tests are the final validation layer. They prove the entire stack works together (Blueprint Service → Encryption Pipeline → Register Storage → Recipient Decryption) in a realistic deployment. Lower priority because unit and integration tests cover individual components.

**Independent Test**: Can be tested by running the Docker Compose E2E test suite with a non-DevMode register, executing the council credential workflow, and verifying encrypted payloads in MongoDB and per-participant decryption results.

**Acceptance Scenarios**:

1. **Given** a non-DevMode register with a published blueprint, **When** a citizen submits the starting action, **Then** the payload is encrypted and stored with an encrypted content encoding marker.
2. **Given** an encrypted action with disclosure rules granting the citizen `["/decision"]` and ID Department `["/*"]`, **When** the citizen queries their action data, **Then** they receive only the decrypted `decision` field.
3. **Given** the same encrypted action, **When** the ID Department queries their action data, **Then** they receive all fields decrypted.
4. **Given** the same encrypted action, **When** an unauthorised wallet queries the data, **Then** they receive no payload content.
5. **Given** the E2E test suite, **When** run twice consecutively against a fresh environment, **Then** all tests pass both times (idempotency).

---

### Edge Cases

- What happens when the user has multiple encryption operations running concurrently? The global progress service tracks all active operations. The floating panel shows the most recent operation; a counter badge indicates additional active operations. Clicking the badge cycles through them.
- What happens when SignalR disconnects mid-operation? The panel automatically falls back to 2-second polling. When SignalR reconnects, it switches back to push updates. A subtle connection indicator shows the active data channel.
- What happens when the user closes the browser tab during encryption? The backend operation completes regardless. On next login, an activity event notification informs the user of the result.
- What happens when a DevMode register is toggled to encrypted mode while operations are in flight? New operations from that point forward use encryption. In-flight operations complete with their original mode (plaintext or encrypted).
- What happens when the encrypted payload exceeds the 4MB transaction size limit? The pipeline performs a pre-flight size estimation before starting encryption. If the estimate exceeds the limit, the operation fails immediately with a clear size error — no encryption work is wasted.
- What happens when all recipients share identical disclosure rules? A single ciphertext group is created with wrapped keys for each recipient — the disclosure group optimisation from spec 045 applies.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The encryption pipeline MUST emit a per-recipient SignalR event after each recipient's key wrapping completes, containing recipient display name, disclosed field summary, recipient index, total count, and status.
- **FR-002**: The per-recipient events MUST be sent to the `wallet:{submittingWalletAddress}` SignalR group on ActionsHub, consistent with existing encryption events.
- **FR-003**: The polling fallback endpoint MUST include per-recipient status in its response when the operation is in progress.
- **FR-004**: The UI MUST display a floating progress panel (bottom-right) when an encryption operation begins, showing a progress bar, recipient list with per-recipient status, and disclosed field summaries.
- **FR-005**: The floating panel MUST support three states: expanded (full detail), minimised (compact pill), and dismissed (no panel, toast on completion).
- **FR-006**: The floating panel state MUST persist across page navigation within the application — navigating to a different page does not dismiss or reset the panel.
- **FR-007**: The floating panel MUST use task-oriented language: "Securing your submission", "Preparing for [Recipient Name]", "secured", "waiting" — no cryptographic terminology visible to the user.
- **FR-008**: On successful completion, the UI MUST display "Submission secured — N recipients can now access their disclosed fields" with a link to the transaction in the transaction explorer.
- **FR-009**: On failure, the UI MUST display an actionable error identifying the failing recipient (if applicable) and the nature of the failure, with a "Retry" action that resubmits the original request.
- **FR-010**: The floating panel MUST handle multiple concurrent operations by showing the most recent with a counter badge indicating additional active operations.
- **FR-011**: Unit tests MUST be written for DevMode register initiation, DevMode toggle endpoint authorisation, and plaintext path selection bypassing the encryption pipeline.
- **FR-012**: Unit tests MUST be written for disclosure group encryption (correct grouping, ciphertext count, wrapped key distribution) and recipient key resolution (instance bindings, register lookups, revoked participants).
- **FR-013**: Docker E2E tests MUST validate the full encryption round-trip: submit → encrypt → store → query → decrypt, with per-participant disclosure enforcement.
- **FR-014**: The existing `EncryptionProgressIndicator` component MUST be replaced or wrapped by the new popover component — no duplicate progress UI.
- **FR-015**: The EncryptionProgress SignalR integration test (GAP-005) MUST be completed, verifying that progress events are correctly delivered via ActionsHub.

### Key Entities

- **RecipientProgressEvent**: A per-recipient notification emitted during encryption, containing operation ID, recipient display name, disclosed fields summary, index, total count, and status (waiting, encrypting, secured, failed).
- **CryptoProgressPopover**: A floating UI panel with three visual states (expanded, minimised, dismissed) that subscribes to encryption events and renders task-oriented progress feedback.
- **EncryptionOperationTracker**: A global service that tracks active encryption operations across page navigations, managing operation lifecycle and panel state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can identify which recipients are being prepared during encryption and what fields each recipient will access — verified by per-recipient progress display in the floating panel.
- **SC-002**: Users can continue working on other pages during encryption without losing progress visibility — verified by navigating away and confirming the panel persists.
- **SC-003**: Encryption progress updates appear within 1 second of each recipient's key wrapping completing — verified by measuring time between backend event emission and UI rendering.
- **SC-004**: DevMode and field-level encryption unit tests achieve >85% code coverage for the affected paths — verified by coverage report.
- **SC-005**: Full encryption round-trip (submit → encrypt → store → query → decrypt) completes successfully in E2E tests for a 3-recipient action — verified by E2E test pass.
- **SC-006**: Encryption failures display an actionable error within 2 seconds of the failure occurring, identifying the affected recipient — verified by simulating key resolution failure in tests.
- **SC-007**: All existing tests (1,200+) continue to pass with zero regressions — verified by full test suite run.
- **SC-008**: The floating panel renders correctly in all three states (expanded, minimised, dismissed) across page navigations — verified by Playwright E2E test.

## Assumptions

- The existing `ActionsHubConnection` SignalR infrastructure is stable and does not require architectural changes — only new event handlers and models are needed.
- The `EncryptionBackgroundService` loop structure supports injecting per-recipient notifications without significant refactoring — the per-recipient loop is already present in the pipeline.
- MudBlazor's `MudPopover` or a custom positioned element can achieve the floating panel behaviour without conflicting with the existing layout.
- Recipient display names are available during encryption from the disclosure evaluation output — participant names are resolved before the encryption step in `ActionExecutionService`.
- The 2-second polling interval for the fallback path is acceptable when SignalR is unavailable.
