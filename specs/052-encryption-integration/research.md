# Research: Envelope Encryption Integration

**Feature**: 052-encryption-integration
**Date**: 2026-03-06

## Decision 1: Operation History Data Source

**Decision**: Use existing ActivityEvent records (30-day TTL) as the persistent history source for completed operations. Use the in-memory EncryptionOperationStore for active/in-progress operations only.

**Rationale**: The EncryptionBackgroundService already stores ActivityEvent records on completion and failure (T047 feature). These have a 30-day TTL and include operation metadata (operationId, status, transactionHash, error). The in-memory store auto-expires after 1 hour, which is fine for active tracking but insufficient for history. Leveraging ActivityEvents avoids introducing a new persistent store.

**Alternatives considered**:
- New PostgreSQL table for operations: Rejected — adds schema migration, repository, and storage dependency for data that already exists in ActivityEvents.
- Extending in-memory store retention: Rejected — memory-bounded, not suitable for history across restarts.
- MongoDB document collection: Rejected — over-engineering when ActivityEvents already provide the data.

## Decision 2: Operations List Endpoint

**Decision**: Add a `GET /api/operations?wallet={address}&page={n}&pageSize={n}` endpoint to the Blueprint Service that queries ActivityEvents filtered by encryption operation type, with pagination.

**Rationale**: The existing `GET /api/operations/{operationId}` only retrieves a single operation. US4 requires listing all operations for a wallet. Rather than adding a list method to IEncryptionOperationStore (which is in-memory and short-lived), we query the persistent ActivityEvent store which already has the data.

**Alternatives considered**:
- Adding ListByWalletAsync to IEncryptionOperationStore: Rejected — in-memory store expires data after 1 hour, so history would be incomplete.
- Querying ActivityEvents directly from the UI: Rejected — UI should go through a dedicated operations endpoint, not raw event queries.

## Decision 3: SignalR Channel for Encryption Progress

**Decision**: Use the existing ActionsHub SignalR connection for encryption progress events. The EncryptionBackgroundService already sends `EncryptionProgress`, `EncryptionComplete`, and `EncryptionFailed` events through the wallet group on ActionsHub. No new hub needed.

**Rationale**: The UI already connects to ActionsHub via `ActionsHubConnection`. Adding handlers for the three encryption event types requires only client-side changes — no backend modification needed for this.

**Alternatives considered**:
- Dedicated EncryptionHub: Rejected — adds connection overhead, wallet group management duplication, and complexity for 3 event types.
- EventsHub: Rejected — EventsHub uses user-based groups, not wallet-based groups; encryption events are wallet-scoped.

## Decision 4: CLI Action Submission

**Decision**: Create a new `sorcha action execute` CLI command under a new `ActionCommand` group. The command accepts blueprint ID, action ID, instance ID, wallet, register, and payload JSON. It calls the existing Blueprint Service endpoint and handles async responses with Spectre.Console progress display.

**Rationale**: No CLI action submission command currently exists. The Blueprint Service endpoint (`POST /api/instances/{instanceId}/actions/{actionId}/execute`) is fully functional — only the CLI client is missing. The Refit interface needs a new method, and the command needs async-aware response handling.

**Alternatives considered**:
- Extending the existing `operation status` command: Rejected — status checking is separate from submission; they belong to different command groups.
- Scripted cURL wrapper: Rejected — doesn't integrate with CLI auth, profiles, or output formatting.

## Decision 5: Retry Mechanism Data Storage

**Decision**: Store the original ActionExecuteRequest in the browser's component state (in-memory) during the operation. On failure, the retry button re-submits from this cached request. No server-side request storage needed.

**Rationale**: The user is on the page when they submit the action, so the request data is already in component state. The EncryptionProgressIndicator can receive the original request as a parameter and use it for retry. If the user navigates away, the action remains in their pending list — they can re-open the form and re-submit normally.

**Alternatives considered**:
- Server-side request storage in EncryptionOperation: Rejected — stores payload data unnecessarily, increases storage requirements, and the EncryptionWorkItem already has the data but it's consumed by the background service.
- Browser localStorage: Rejected — over-engineering for a transient retry; component state is sufficient since the user is on the page.

## Decision 6: Notification Delivery for Disconnected Users

**Decision**: Use the existing ActivityEvent system for persistent notifications. When a user reconnects or visits the operations page, they see completed/failed operations from ActivityEvents. Toast notifications are only delivered to currently connected clients via SignalR.

**Rationale**: The EncryptionBackgroundService already stores ActivityEvent records with 30-day TTL. The EventsHub already pushes `EventReceived` notifications to connected users. For encryption completion, we need to also send a notification through the EventsHub (user-scoped) in addition to the existing ActionsHub (wallet-scoped) notification.

**Alternatives considered**:
- Push notification queue for offline users: Rejected — requires new infrastructure; ActivityEvents already provide the persistent record.
- Email notifications: Rejected — out of scope per spec.
