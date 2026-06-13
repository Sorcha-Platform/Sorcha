# Phase 0 Research: PWA Offline / Field Capture

**Feature**: 152-offline-field-capture | **Date**: 2026-06-13

Brainstorming + a read-only codebase investigation resolved the substantive unknowns. No open
NEEDS CLARIFICATION items.

## Decision 1 — Local encryption & storage

**Decision**: Reuse the existing device-bound XChaCha20-Poly1305 + IndexedDB mechanism
(`IndexedDbCredentialCache` + `indexeddb-bridge.js` + `xchacha-bridge.js`). Add new IndexedDB stores
`drafts`, `submitQueue`, `actionContext` via a schema bump in `indexeddb-bridge.js`. Drafts, queued
payloads, and captured media are encrypted at rest with the device content key.

**Rationale**: Proven, consumer-tier, satisfies constitution §II (at-rest encryption). No new crypto.

**Consequence**: Drafts are device-bound — lost on device loss/unpair (no server copy). Acceptable
and consistent with the credential cache; messaged honestly (SCOPE-002).

## Decision 2 — Attachment submission path (the one backend-touching slice, US5)

**Decision**: Extend the PWA's existing submit path — `POST /{instanceId}/actions/{actionId}/execute`
(`Program.cs:2315`) — to honor `ActionSubmissionRequest.Files` by reusing the **same** proven logic
the legacy `/api/actions` endpoint already runs (`Program.cs:1560`): `BuildFileTransactionsAsync` →
`StoreFileContentAsync` → file-transaction hashes referenced from the action transaction. Typical
photos go inline (base64 `Files`); the existing `/api/file-chunks` staging pipeline remains the
large-file path if a capture exceeds the inline payload limit.

**Rationale**: The mechanism exists and is consumer-tier; the only gap is that `/execute` accepts the
`Files` field but doesn't process it (it delegates to `ActionExecutionService`, which ignores Files).
Wiring the proven logic into the execute path keeps the PWA on its current submit route (no PWA
submit-route change) and avoids inventing anything. Inline Files suits typical photos
(`FileRenderer` ceiling 40 MB); chunking is a refinement, not MVP.

**Alternatives considered**:
- *Submit through the legacy `/api/actions` endpoint* — rejected: diverges the PWA from the F137
  `/execute` path A already uses; two submit routes to maintain.
- *File-chunks for everything* — rejected for MVP: more moving parts than typical photo sizes need.

## Decision 3 — Pre-cache pending actions for offline open (US2)

**Decision**: Add `IActionContextCache` + an `actionContext` store. When online (on inbox load /
`ISyncService` sync), fetch and cache each pending action's form context (blueprint action schema +
layout + register/sender context — the same shape `ApplicationInstance` loads today via
`IApplicationActionClient.LoadFormAsync`). `ApplicationInstance` reads from the cache when offline;
the cache refreshes whenever online.

**Rationale**: Reuses A's load shape; "open any pending action offline" needs the schema local.
Staleness is backstopped by conflict handling (Decision 5).

## Decision 4 — Submit queue / drain trigger (US3)

**Decision**: `ISubmitQueue` + `submitQueue` store (outbox). A completed offline submission enqueues
`{payload, attachmentRefs, idempotencyKey, state, attempts}`. Drain on foreground connectivity
signals — `IConnectivity` online event, app open, and `ISyncService` sync — **not** Background Sync
(none exists; companion-roadmap P2). Reuse the server idempotency key so a re-flush can't duplicate.

**Rationale**: Foreground drain is all the platform supports today; idempotency makes retries safe.

## Decision 5 — Conflict handling: detect / hold / ask (US4)

**Decision**: A pure `SubmitConflictClassifier` maps a server submit outcome to
`{ Submitted | Stale(reason) | Retry }`. `Stale` (already submitted / step moved on / instance
closed — detected from the execute response / idempotent-reject) marks the queue item + draft
`NeedsAttention` and records the reason; the UI offers **discard** or **re-open-fresh** (re-fetch the
current action). Captured data is retained until the citizen chooses. Transient → `Retry` with
backoff.

**Rationale**: The brainstorm decision (no silent loss). A pure classifier is unit-testable.

## Decision 6 — Connectivity signal (US1/US3)

**Decision**: `IConnectivity` over `navigator.onLine` + `online`/`offline` events via JS interop
(small bridge). Drives offline UI and queue drain. Treat as a hint (a flush attempt is the real
test).

## Decision 7 — Capture reuse (US5)

**Decision**: Reuse `FileRenderer` / `PortraitCaptureControl` (already capture into in-memory form
state, camera→file-picker fallback). Add persistence: captured media is written into the encrypted
draft (not just in-memory), and restored on reopen. No new capture UI.

## Decision 8 — Test placement & feedback surface

**Decision**: Tests in `tests/Sorcha.Wallet.Pwa.Tests` (service + bUnit); US5 backend wiring gets a
`tests/Sorcha.Blueprint.Service.Tests` test if `/execute` is extended. Feedback via `IInlineFeedback`
/ inline page state — never `ISnackbar` (Pattern #12). Navigation base-relative under `/wallet/`.

## Decision 9 — Dependency on A

**Decision**: Built on sub-project A (`IMyActionsClient`, `Actions.razor`, `ApplicationInstance`).
The 152 branch is stacked on A and will be rebased onto master (`--onto`) once A merges, so C's PR
shows only C's diff.
