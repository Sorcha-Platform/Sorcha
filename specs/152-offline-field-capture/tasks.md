# Tasks: PWA Offline / Field Capture

**Input**: Design docs from `/specs/152-offline-field-capture/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Tests**: INCLUDED (TDD) — constitution §IV + design §7. Tests written before implementation.
**Organization**: by user story. **Depends on sub-project A** (reuses `IMyActionsClient`,
`Actions.razor`, `ApplicationInstance`, `SorchaFormRenderer`).

## Conventions (every task)

- Drafts/media/queue **encrypted at rest** (XChaCha20-Poly1305 device key; reuse the credential-cache pattern).
- **Base-relative** nav under `/wallet/`; **no `ISnackbar`** (use `IInlineFeedback`/inline state).
- bUnit `JSRuntimeMode.Loose`; mock the IndexedDB JS-interop seam + the submit delegate.
- Foreground-only queue drain (no Background Sync). Reuse server idempotency for safe retry.

---

## Phase 1: Setup

- [ ] T001 [P] Create draft/queue/context models in `src/Apps/Sorcha.Wallet.Pwa/Services/Drafts/Models/` (`ActionDraft`, `DraftMedia`, `QueuedSubmission`, `CachedActionContext`, status enums) per data-model.md.
- [ ] T002 Add IndexedDB stores `drafts`, `submitQueue`, `actionContext` to `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/indexeddb-bridge.js` (schema version bump; mirror existing store creation).

---

## Phase 2: Foundational (blocking)

- [ ] T003 [P] (TDD) Failing tests for an encrypted IndexedDB store helper in `tests/Sorcha.Wallet.Pwa.Tests/Drafts/` (round-trip put/get/list/delete through a mocked bridge; values encrypted before put).
- [ ] T004 Implement a reusable encrypted-store seam (extract/reuse the `IndexedDbCredentialCache` XChaCha20 pattern) used by the draft/queue/context stores. Make T003 pass.
- [ ] T005 [P] (TDD) Failing tests for `IConnectivity` (online/offline state from a mocked JS bridge; change event raised).
- [ ] T006 Implement `src/Apps/Sorcha.Wallet.Pwa/Services/IConnectivity.cs` over `navigator.onLine` + online/offline events. Make T005 pass.
- [ ] T007 Register the new stores + `IConnectivity` in `Extensions/ServiceCollectionExtensions.cs` DI.

**Checkpoint**: encrypted device-local storage + connectivity signal available.

---

## Phase 3: US2 — Pre-cache pending actions for offline open (Priority: P1, foundational for US1)

**Goal**: When online, cache every pending action's form context so any can be opened offline.
**Independent Test**: online, let it cache; go offline; open a never-opened pending action → renders.

- [ ] T008 [P] [US2] (TDD) Failing tests for `IActionContextCache` in `tests/.../Drafts/ActionContextCacheTests.cs`: caches contexts for the pending list (stub `IMyActionsClient` + loader), offline-get returns a cached context, refresh overwrites, un-cached id returns null.
- [ ] T009 [US2] Implement `IActionContextCache` + `IndexedDbActionContextCache` (`Services/Drafts/`): cache the `LoadFormAsync`-shaped context per `instanceId:actionId`. Make T008 pass.
- [ ] T010 [US2] Hook caching into the online path — refresh from the inbox load / `ISyncService` sync (`Services/ISyncService.cs` or inbox load in `Actions.razor`), iterating the pending list.
- [ ] T011 [US2] `ApplicationInstance.razor`: when offline (or load fails), read the action context from `IActionContextCache`; if absent, render a clear "available when you're back online" state (not the generic error). bUnit test: offline open of cached vs un-cached action.

**Checkpoint**: any prepared pending action opens offline.

---

## Phase 4: US1 — Resume & submit an offline draft (Priority: P1) 🎯 MVP

**Goal**: Open offline, fill, autosave an encrypted draft, resume, submit when online.
**Independent Test**: offline fill → close/reopen → restored → online → submits → draft cleared.

- [ ] T012 [P] [US1] (TDD) Failing tests `tests/.../Drafts/DraftStoreTests.cs`: save/load round-trip (encrypted), list, delete, status transitions.
- [ ] T013 [US1] Implement `IDraftStore` + `IndexedDbDraftStore` (`Services/Drafts/`) keyed `instanceId:actionId`. Make T012 pass.
- [ ] T014 [P] [US1] (TDD) Failing bUnit tests for `ApplicationInstance` draft behaviour: prefills the form from an existing draft; autosaves form changes to the draft store; clears the draft on successful submit.
- [ ] T015 [US1] `ApplicationInstance.razor`: load draft on open (prefill `SorchaFormRenderer`), autosave on change (debounced), clear on submit success. Make T014 pass.
- [ ] T016 [US1] `Actions.razor`: show a per-row draft state badge ("Saved offline" / "Ready to submit") sourced from `IDraftStore`. bUnit test.

**Checkpoint**: MVP — offline work survives close/reopen and submits when online.

---

## Phase 5: US3 — Queued submit that flushes on reconnect (Priority: P2)

**Goal**: Offline submissions queue and auto-send on reconnect with visible status.
**Independent Test**: complete offline → "Queued" → reconnect → "Submitted" (no manual action).

- [ ] T017 [P] [US3] (TDD) Failing tests `tests/.../Drafts/SubmitQueueTests.cs`: enqueue; drain success → Submitted; drain transient → Retry (stays queued, attempts++); ordering; idempotency-key reused so a re-flush doesn't double-submit (stub submit delegate).
- [ ] T018 [US3] Implement `ISubmitQueue` + `IndexedDbSubmitQueue` (`Services/Drafts/`) with a `DrainAsync(submitDelegate)`. Make T017 pass.
- [ ] T019 [US3] On offline submit, enqueue instead of direct send; on `IConnectivity` online + app open + `ISyncService` sync, drain. Wire in `ApplicationInstance` (enqueue) + `ISyncService`/MainLayout (drain trigger).
- [ ] T020 [US3] `Actions.razor`: per-row "Queued" / "Submitting" / "Submitted" badge from `ISubmitQueue`. bUnit test.

**Checkpoint**: US1 + US3 — offline completions submit themselves on reconnect.

---

## Phase 6: US4 — Conflict handling: detect, hold, ask (Priority: P2)

**Goal**: A stale deferred submit is held + explained, never silently dropped.
**Independent Test**: queue → advance action server-side → reconnect → "Needs attention" + reason; discard / re-open-fresh; data retained until chosen.

- [ ] T021 [P] [US4] (TDD) Failing tests `tests/.../Drafts/SubmitConflictClassifierTests.cs`: table-driven server-outcome → `Submitted | Stale(AlreadySubmitted|StepMovedOn|InstanceClosed) | Retry`.
- [ ] T022 [US4] Implement pure `SubmitConflictClassifier` (`Services/Drafts/`). Make T021 pass.
- [ ] T023 [US4] `ISubmitQueue` drain uses the classifier: `Stale` → mark queue item + draft `NeedsAttention` + reason (retain captured data); `Retry` → backoff. Extend SubmitQueueTests for the stale path.
- [ ] T024 [US4] UI: `Actions.razor` "Needs attention" badge + a detail surface explaining the reason with **discard** / **re-open-fresh** (re-fetch current action context). bUnit test: held item shows reason + both choices; discard removes; re-open-fresh routes to the action against fresh context.

**Checkpoint**: no silent loss — every undeliverable submit ends in an explained held state.

---

## Phase 7: US5 — Capture & submit photos/media offline (Priority: P3, backend-touching)

**Goal**: Capture photos offline, persist in the draft, submit as attachments.
**Independent Test**: offline capture → reopen (persisted) → submit → attached to the action.

- [ ] T025 [P] [US5] (TDD) Failing tests: captured media persists in the draft (`DraftStoreTests` extension — media survives round-trip, encrypted); over-ceiling media rejected at capture.
- [ ] T026 [US5] Persist `FileRenderer`/`PortraitCaptureControl` captures into the encrypted draft (`Media`), restore on reopen; warn at capture if over the 40 MB ceiling. Wire in `ApplicationInstance`.
- [ ] T027 [P] [US5] (TDD) Failing Blueprint Service test `tests/Sorcha.Blueprint.Service.Tests/…`: `POST /{id}/actions/{actionId}/execute` with `Files` populated creates file transactions (reusing `BuildFileTransactionsAsync`) and references them from the action.
- [ ] T028 [US5] Wire `request.Files` handling into the `/execute` path (`Program.cs:2315` / `ActionExecutionService`) reusing the legacy endpoint's `BuildFileTransactionsAsync` → `StoreFileContentAsync` → file-hash logic. Make T027 pass. Update the endpoint OpenAPI summary/description + XML docs.
- [ ] T029 [US5] PWA submit/queue includes draft media as `Files` (base64) in the execute body; large-file → `/api/file-chunks` path is a documented refinement (not required for typical photos). End-to-end bUnit/integration assertion that a captured photo reaches the submit body.

**Checkpoint**: full field loop — offline capture through attached submission.

---

## Phase 8: Polish

- [ ] T030 [P] Run `scripts/check-no-snackbar.ps1`; confirm no new `ISnackbar`.
- [ ] T031 [P] New-code coverage ≥85% (constitution §IV) for stores/queue/classifier/cache/connectivity; add tests for any uncovered branch.
- [ ] T032 [P] Docs: note offline/field-capture + the `/execute` attachment support (service README / API docs / `sorcha-architecture` skill as appropriate).
- [ ] T033 Run `quickstart.md` manual verification (offline open, capture, resume, auto-submit, conflict, attachment) against Docker/n1; record results.
- [ ] T034 Clean build (`Sorcha.Wallet.Pwa` + `Sorcha.Blueprint.Service`) 0 warnings; `Sorcha.Wallet.Pwa.Tests` + `Sorcha.Blueprint.Service.Tests` green.

---

## Dependencies & order

- Setup (P1) → Foundational (P2) blocks all stories.
- **US2 (P3 phase)** is foundational for US1's offline open → do first among stories.
- **US1 (MVP)** depends on US2 + Foundational.
- **US3** depends on US1 (drafts) + Foundational (connectivity).
- **US4** depends on US3 (queue drain).
- **US5** depends on US1 (draft persistence) + is the only backend-touching slice → last.
- Polish after the desired stories.

### Within each story
- Tests first and FAIL before implementation; stores before services before UI before backend wiring.

### Parallel
- T001/T003/T005 setup+foundational tests; US-test authoring [P]; polish T030–T032.

---

## Implementation strategy

**MVP** = Setup + Foundational + US2 + US1 → a citizen can open a prepared action offline, fill it,
resume across sessions, and submit on reconnect. Then US3 (auto-flush) → US4 (conflict safety) →
US5 (photos + attachment). Each story independently testable; US5 isolates the backend change.
