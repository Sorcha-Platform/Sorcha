# Research: FLE Completion & Crypto Progress UX

## R1: Per-Recipient Event Injection Point

**Decision**: Inject per-recipient progress events from `EncryptionBackgroundService`, not from `EncryptionPipelineService`.

**Rationale**: `EncryptionPipelineService` is a library component in `Sorcha.TransactionHandler` — it has no dependency on `INotificationService` (which lives in Blueprint.Service). Rather than adding a cross-layer dependency, we enhance the pipeline's return value to include per-recipient metadata, then emit events from `EncryptionBackgroundService` which already has `INotificationService`.

**Alternatives considered**:
- Callback/delegate pattern: Pass an `Action<RecipientProgress>` into the pipeline. Rejected — adds complexity and threading concerns to a library component.
- Direct NotificationService injection into pipeline: Rejected — violates the layering (TransactionHandler should not depend on Blueprint.Service).
- Post-hoc emission from EncryptionBackgroundService: **Chosen** — cleanest approach. Enhance `EncryptionResult` to include per-recipient completion metadata, then iterate and emit from the background service.

**Implementation approach**:
1. Add `RecipientProgress[]` to `EncryptionResult` (TransactionHandler layer)
2. Populate during `EncryptGroupAsync()` per-recipient loop (lines 210-238)
3. In `EncryptionBackgroundService`, after `EncryptDisclosedPayloadsAsync()` returns, iterate `RecipientProgress[]` and call `NotifyRecipientProgressAsync()` for each
4. For real-time feel, consider splitting encryption into per-group calls so events emit between groups rather than all at once after completion

**Refinement — streaming per-group**: Instead of calling `EncryptDisclosedPayloadsAsync()` once for all groups, the background service can iterate groups itself, calling `EncryptGroupAsync()` per group and emitting recipient events between calls. This requires making `EncryptGroupAsync()` public or adding a `EncryptSingleGroupAsync()` method.

## R2: Recipient Display Name Resolution

**Decision**: Use participant display names from disclosure evaluation output, falling back to truncated wallet address.

**Rationale**: The `ActionExecutionService.ApplyDisclosuresAsync()` step (which runs before encryption) resolves participant names. The `DisclosureGroup.Recipients` array contains `RecipientInfo` with `WalletAddress` but no display name. The display name must be added to `RecipientInfo` or carried alongside it in the `EncryptionWorkItem`.

**Implementation approach**:
1. Add `DisplayName` field to `RecipientInfo` (or a parallel lookup dictionary in `EncryptionWorkItem`)
2. Populate from `ApplyDisclosuresAsync()` output — participant names are available from `InstanceState.Participants`
3. Fallback: truncated wallet address (first 8 + last 4 chars) when no display name available

## R3: Disclosed Fields Summary Format

**Decision**: Summarise disclosed fields as human-readable text: "all fields" when `["/*"]`, otherwise comma-separated field names extracted from JSON Pointer paths.

**Rationale**: JSON Pointer paths like `["/decision", "/siteAddress", "/drawings"]` are not user-friendly. The UI should show "decision, site address, drawings" or "all fields".

**Implementation approach**:
1. Backend includes raw `DisclosedFields[]` (JSON Pointer paths) in the recipient event
2. UI formats: `["/*"]` → "all fields", otherwise strip leading `/`, convert camelCase to space-separated, join with commas
3. Keep formatting in UI layer — backend sends structured data

## R4: UI Architecture — Global Popover

**Decision**: Scoped `EncryptionOperationTracker` service + layout-level `CryptoProgressPopover` component.

**Rationale**: Sorcha.UI uses scoped DI lifetime for all stateful services (hub connections, preferences, etc.). Scoped services survive Blazor WASM page navigation (they're per-circuit, not per-page). A layout-level component in `MainLayout.razor` renders once and persists across navigation — matching existing patterns (`OperationNotificationListener`, `PendingActionToast`, `ActivityLogPanel`).

**Alternatives considered**:
- Singleton service: Rejected — only `IGraphLayoutService` uses singleton, and only because it's stateless. Scoped is the project convention.
- Component-only state (no service): Rejected — harder to trigger from `NewSubmissionDialog` which needs to tell the tracker about a new operation.
- CascadingValue from MainLayout: Rejected — more coupling than DI injection.

**Implementation approach**:
1. `IEncryptionOperationTracker` interface + `EncryptionOperationTracker` implementation (scoped)
2. Subscribes to `ActionsHubConnection` events (progress, complete, failed, and new per-recipient events)
3. Exposes `ActiveOperations` dictionary + events for UI binding
4. `CryptoProgressPopover.razor` placed in `MainLayout.razor` alongside existing global components
5. Three visual states managed by component: expanded, minimised, dismissed
6. On dismiss → unsubscribe from visual updates, subscribe to completion for toast via `ISnackbar`

## R5: SignalR Event Model Enhancement

**Decision**: Add a new `RecipientEncryptionProgress` event alongside existing events (do not modify existing event schemas).

**Rationale**: Existing `EncryptionProgress` events (step-level) are consumed by the current `EncryptionProgressIndicator`. Adding fields to the existing model would break backward compatibility. A new event type for per-recipient progress keeps concerns separated.

**New event**: `RecipientEncryptionProgress` on ActionsHub
- `OperationId`: string
- `RecipientName`: string (display name or truncated wallet)
- `RecipientIndex`: int (1-based)
- `TotalRecipients`: int
- `DisclosedFieldsSummary`: string[] (JSON Pointer paths)
- `Status`: string ("waiting" | "encrypting" | "secured" | "failed")
- `ErrorMessage`: string? (only when status = "failed")
- `Timestamp`: DateTimeOffset

## R6: Polling Endpoint Enhancement

**Decision**: Add `Recipients[]` array to the `EncryptionOperation` model returned by `GET /api/operations/{operationId}`.

**Rationale**: The polling fallback must provide the same data as SignalR for clients that can't use WebSockets. The `InMemoryEncryptionOperationStore` already tracks operation state — adding per-recipient state is a natural extension.

**Implementation approach**:
1. Add `RecipientStatus[]` property to `EncryptionOperation` model
2. Update `InMemoryEncryptionOperationStore` to accept recipient updates
3. `EncryptionBackgroundService` updates the store after each recipient completes

## R7: Spec 065 Deferred Test Patterns

**Decision**: Follow existing test patterns — `WebApplicationFactory` for endpoint tests, Moq-based unit tests for service tests.

**Rationale**: Existing tests in the same directories use these exact patterns. Consistency is more important than optimisation.

**Test file mapping**:

| Task | File | Pattern | Key Mocks |
|------|------|---------|-----------|
| T022 | `RegisterInitiateDevModeTests.cs` | WebApplicationFactory | In-memory register storage |
| T023 | `RegisterDevModeToggleTests.cs` | WebApplicationFactory | In-memory storage + auth claims |
| T024 | `ActionExecutionDevModeTests.cs` | Moq unit test | `IRegisterServiceClient`, `IEncryptionPipelineService` |
| T032 | `DisclosureGroupEncryptionTests.cs` | Moq unit test | `ISymmetricCrypto`, `ICryptoModule` |
| T033 | `ActionExecutionEncryptionTests.cs` | Moq unit test | `IRegisterServiceClient.ResolvePublicKeysBatchAsync` |
| GAP-005 | `EncryptionNotificationTests.cs` | Moq unit test | `IHubContext<ActionsHub>`, `IHubClients` |

## R8: Existing EncryptionProgressIndicator Disposition

**Decision**: Replace `EncryptionProgressIndicator` usage with `CryptoProgressPopover`. Keep the old component but deprecate it.

**Rationale**: The old component is page-scoped and embedded inline. The new popover is global and floating. They serve the same purpose — running both would confuse users. However, removing the old component immediately risks breaking any page that embeds it. Deprecate and remove in a follow-up cleanup.

**Implementation approach**:
1. Remove `<EncryptionProgressIndicator>` from `NewSubmissionDialog` and any other embedding pages
2. Replace with call to `IEncryptionOperationTracker.TrackOperation(operationId, metadata)` which triggers the global popover
3. Mark `EncryptionProgressIndicator.razor` with `[Obsolete]` comment for later removal
