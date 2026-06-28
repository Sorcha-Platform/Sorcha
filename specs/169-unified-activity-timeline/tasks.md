# Tasks: Unified Activity Timeline Read-Path (Feature 169)

**Input**: Design documents from `/specs/169-unified-activity-timeline/`

**Feature Branch**: `169-unified-activity-timeline`

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- All paths from repository root

---

## Phase 1: Setup

**Purpose**: Confirm baseline before any additive work. No new projects — all changes are additive to existing services and the shared component library.

- [X] T001 Verify `dotnet restore && dotnet build` is clean (no pre-existing warnings in `Sorcha.Tenant.Service`, `Sorcha.Blueprint.Service`, `Sorcha.UI.Components.User`, `Sorcha.UI.Web.Client`, `Sorcha.Wallet.Pwa`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Server-side classification helper and store interface contracts that all three user stories depend on.

**⚠️ CRITICAL**: US2 implementation and US3 writer tests reference the InboxClassification helper and IInboxStore contract. Complete this phase before starting US1 tests or US2 implementation.

- [X] T002 [P] Add `actionableOnly: bool` parameter to `GetPageAsync` and `GetUnreadCountAsync` on `IInboxStore` (with XML `<summary>` on new params) in `src/Services/Sorcha.Tenant.Service/Storage/IInboxStore.cs`
- [X] T003 [P] Create `InboxClassification` static helper with `static bool IsActionable(InboxCategory category, InboxSeverity severity)` implementing `category == Action || severity >= ActionRequired`, plus the equivalent EF-translatable `Expression<Func<InboxEntry, bool>> ActionablePredicate` in `src/Services/Sorcha.Tenant.Service/Services/InboxClassification.cs`

**Checkpoint**: Foundation ready — US1 component work and US3 writer work can start; US2 store implementation can start.

---

## Phase 3: User Story 1 — See my complete activity in one timeline (Priority: P1) 🎯 MVP

**Goal**: A shared `ActivityFeed` Blazor component in `Sorcha.UI.Components.User`, consumed by both the web `/app` host (new `/activity` route) and the PWA (re-pointed existing `/activity` page), reading all inbox-spine entries newest-first with paging, empty state, responsive layout, and live-update subscription.

**Independent Test**: Sign in with a user that has inbox entries, open `/activity` on both web and PWA, and confirm all entries appear in one reverse-chronological list with title, summary, timestamp, and category indicator; confirm the same component is used on both hosts; confirm mobile and desktop viewports both render correctly.

### Implementation for User Story 1

- [X] T004 [P] [US1] Create `ActivityClassification` client-side static helper with `static bool IsActionable(string category, string severity)` (unknown strings → Informational as the safe default) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/ActivityClassification.cs`
- [X] T005 [P] [US1] Write xUnit truth-table tests asserting `InboxClassification.IsActionable` (server) and `ActivityClassification.IsActionable` (client) return identical results across all 28 `InboxCategory × InboxSeverity` combinations from `contracts/classification-mapping.md` in `tests/Sorcha.Tenant.Service.Tests/Services/InboxClassificationTests.cs` (depends on T003, T004)
- [X] T006 [P] [US1] Add `bool actionableOnly = false` parameter to `ListAsync` on `IInboxApiService` and update its HTTP implementation to pass `actionableOnly` as a query string parameter to `GET /api/me/inbox` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/IInboxApiService.cs` and its concrete implementation
- [X] T007 [US1] Create shared `ActivityFeed.razor` component implementing: on-init load (`ListAsync(actionableOnly: false)`), reverse-chronological entry list (title, summary, relative timestamp, category/severity indicator, Actionable/Informational affordance via `ActivityClassification`), navigable entry when `DetailHref` present / non-navigable otherwise, "Load more" paging (shown only while `loadedCount < TotalCount`), `EmptyState` on `TotalCount == 0`, and `TenantHubConnection.OnInboxEntryAdded` subscription to refresh page 1 (no Snackbar per Pattern #12) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Activity/ActivityFeed.razor` (depends on T004, T006)
- [X] T008 [US1] Create web `/app` Activity page at route `/activity` that hosts `<ActivityFeed/>` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Pages/Activity.razor` (depends on T007)
- [X] T009 [US1] Re-point PWA `/activity` page from its current implementation to `<ActivityFeed/>` in `src/Apps/Sorcha.Wallet.Pwa/Pages/Activity.razor` (depends on T007)
- [X] T010 [US1] Write bUnit component tests for `ActivityFeed` covering: entries rendered in reverse-chronological order, navigable vs. non-navigable entries, empty state component shown when `TotalCount == 0`, "Load more" present when `loadedCount < TotalCount`, hidden when loaded ≥ total in `tests/Sorcha.UI.Components.User.Tests/Components/Activity/ActivityFeedTests.cs` (depends on T007)

**Checkpoint**: `ActivityFeed` works on both hosts with all existing inbox-spine entries. US1 can be validated independently of the bell changes (US2) and the rerouted producers (US3).

---

## Phase 4: User Story 2 — The bell shows only what needs my attention (Priority: P1)

**Goal**: The Actionable SQL predicate is wired into the store, passed through the service, exposed as a query filter on `GET /api/me/inbox`, applied permanently to `GET /api/me/inbox/unread-count`, and the bell drawer passes `actionableOnly: true` so Informational entries never appear in it.

**Independent Test**: Seed a user with a mix of Actionable (`Action` category, `ActionRequired`/`Critical` severity) and Informational entries; open the bell drawer and confirm 0 Informational entries appear and the badge counts only unread Actionable entries; open the Activity surface and confirm all entries (both groups) remain visible.

### Implementation for User Story 2

- [X] T011 [P] [US2] Implement `EfCoreInboxStore.GetPageAsync` with Actionable filter (`e.Category == InboxCategory.Action || e.Severity >= InboxSeverity.ActionRequired`) applied when `actionableOnly: true`, and `GetUnreadCountAsync` with the same filter in `src/Services/Sorcha.Tenant.Service/Storage/EfCoreInboxStore.cs` (depends on T002, T003)
- [X] T012 [P] [US2] Write xUnit unit tests for `EfCoreInboxStore` Actionable predicate: seed entries covering all cells of the truth table; assert `actionableOnly: true` includes only Actionable rows and `actionableOnly: false` includes all rows; assert unread-count path always uses Actionable scope in `tests/Sorcha.Tenant.Service.Tests/Storage/EfCoreInboxStoreTests.cs` (depends on T011)
- [X] T013 [US2] Pass `actionableOnly` flag through `InboxService.GetPageAsync`; make `InboxService.GetUnreadCountAsync` always pass `actionableOnly: true` to the store in `src/Services/Sorcha.Tenant.Service/Services/InboxService.cs` (depends on T011)
- [X] T014 [US2] Add `actionableOnly` query parameter to `GET /api/me/inbox` handler; update `GET /api/me/inbox/unread-count` handler comment and semantics to reflect Actionable-only counting; update `.WithSummary()` and `.WithDescription()` on both endpoints in `src/Services/Sorcha.Tenant.Service/Endpoints/MeInboxEndpoints.cs` (depends on T013)
- [X] T015 [US2] Update `InboxPanel.razor` (bell drawer) to call `ListAsync(actionableOnly: true)` so only Actionable entries appear in the drawer in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Inbox/InboxPanel.razor` (depends on T006, T014)

**Checkpoint**: Bell drawer shows only Actionable entries; Activity timeline continues to show all entries. US2 independently testable alongside US1.

---

## Phase 5: User Story 3 — No activity is lost when legacy producers move (Priority: P1)

**Goal**: Two new best-effort inbox writers (`PersonaInboxWriter`, `EncryptionInboxWriter`) are created and injected into `PersonaService` and `EncryptionBackgroundService` respectively; each emits into the spine alongside the existing legacy `ActivityEvent` emit (which is not removed); writes are idempotent, best-effort (never roll back the caller), and classified per the R4 mapping.

**Independent Test**: Trigger profile save, profile delete, encryption complete, and encryption fail; confirm each produces a new `InboxEntry` in the spine (visible on Activity timeline) with correct category, severity, title, and SourceEventId; confirm fault-injection of the writer (make write throw) does not fail or roll back the underlying operation.

### Implementation for User Story 3

- [X] T016 [P] [US3] Create `PersonaInboxWriter` with `WritePersonaSavedAsync(Guid platformUserId, DateTimeOffset occurredAt, CancellationToken ct)` and `WritePersonaDeletedAsync(...)` — both best-effort (try/LogWarning/swallow), using `IInboxService` in-process, with deterministic SourceEventId keys (`sorcha.inbox.persona.{replaced|deleted}:{platformUserId:N}:{unixSecs}`), categories/severities per data-model.md §4a, and XML `<summary>` on the interface — in `src/Services/Sorcha.Tenant.Service/Services/PersonaInboxWriter.cs` (also add `IPersonaInboxWriter` interface alongside it)
- [X] T017 [P] [US3] Create `EncryptionInboxWriter` with `WriteEncryptionCompleteAsync(Guid platformUserId, string operationId, CancellationToken ct)` and `WriteEncryptionFailAsync(...)` — both best-effort, using `IPlatformInboxClient` (cross-service HTTP), deterministic SourceEventId keys (`sorcha.inbox.encryption.{complete|fail}:{operationId}`), categories/severities per data-model.md §4b, skip and log when `platformUserId == Guid.Empty`, and XML `<summary>` on the interface — in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionInboxWriter.cs` (also add `IEncryptionInboxWriter` interface alongside it)
- [X] T018 [US3] Inject `IPersonaInboxWriter` into `PersonaService`; add best-effort `await _personaInboxWriter.WritePersonaSavedAsync(...)` after the existing legacy ActivityEvent emit on profile save, and `WritePersonaDeletedAsync(...)` after the existing legacy emit on profile delete; register `PersonaInboxWriter` as `IPersonaInboxWriter` scoped in the Tenant Service DI in `src/Services/Sorcha.Tenant.Service/Services/PersonaService.cs` and the relevant extension/`Program.cs` (depends on T016)
- [X] T019 [US3] Inject `IEncryptionInboxWriter` into `EncryptionBackgroundService`; add best-effort `await _encryptionInboxWriter.WriteEncryptionCompleteAsync(workItem.UserId, operationId, ct)` after the existing complete emit and `WriteEncryptionFailAsync(...)` after the existing fail emit; register `EncryptionInboxWriter` as `IEncryptionInboxWriter` in Blueprint Service DI in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs` and the relevant extension/`Program.cs` (depends on T017)
- [X] T020 [P] [US3] Write xUnit unit tests for `PersonaInboxWriter` covering: save event produces correct `InboxEntry` fields (category `System`, severity `Info`, correct SourceEventId format); delete event produces correct fields (severity `Warning`); writer throws → caller does not throw (best-effort); same SourceEventId on re-call does not throw (idempotency at write level) in `tests/Sorcha.Tenant.Service.Tests/Services/PersonaInboxWriterTests.cs` (depends on T016)
- [X] T021 [P] [US3] Write xUnit unit tests for `EncryptionInboxWriter` covering: complete event (`Workflow`, `Info`); fail event (`Workflow`, `ActionRequired`); empty `platformUserId` skips and logs; writer throws → caller does not throw; deterministic SourceEventId format in `tests/Sorcha.Blueprint.Service.Tests/Services/EncryptionInboxWriterTests.cs` (depends on T017)

**Checkpoint**: All four producer events (profile save/delete, encryption complete/fail) now appear in the unified Activity timeline. US3 independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation compliance, DI validation, bundle gate, and end-to-end validation.

- [X] T022 [P] Add XML `<summary>` to all new/changed public members that are missing doc comments: `IInboxStore` new params, `InboxClassification`, `InboxService` changed methods, `ActivityClassification`, `ActivityFeed` component parameters, `IPersonaInboxWriter`, `PersonaInboxWriter`, `IEncryptionInboxWriter`, `EncryptionInboxWriter` — verify `dotnet build` produces no new XML-doc warnings
- [X] T023 [P] Run `pwsh scripts/check-pwa-bundle.ps1` and confirm `ActivityFeed` introduces no designer/admin assembly dependencies into the PWA bundle
- [X] T024 Run quickstart.md Scenario 1 (complete timeline on both hosts, both viewports), Scenario 2 (bell Actionable-only, badge count), Scenario 3 (all four producer events, fault injection, idempotency), and Scenario 4 (scope guards — legacy table intact, `/operations` unchanged) against a live stack
- [X] T025 [P] Run targeted test suites and confirm all green: `dotnet test --filter "FullyQualifiedName~Classification"`, `--filter "FullyQualifiedName~InboxStore"`, `--filter "FullyQualifiedName~PersonaInboxWriter"`, `--filter "FullyQualifiedName~EncryptionInboxWriter"`, `--filter "FullyQualifiedName~ActivityFeed"`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — T002 and T003 can run in parallel; BLOCKS US2 store work and US3 writer work
- **US1 (Phase 3)**: T004/T006 can start after Setup; T005 (truth-table tests) depends on T003+T004; T007 depends on T004+T006; T008/T009 depend on T007; T010 depends on T007
- **US2 (Phase 4)**: T011/T012 depend on T002+T003; T013 depends on T011; T014 depends on T013; T015 depends on T006+T014
- **US3 (Phase 5)**: T016/T017 can start after Foundational; T018 depends on T016; T019 depends on T017; T020 depends on T016; T021 depends on T017
- **Polish (Phase 6)**: Depends on all user stories complete

### User Story Dependencies

- **US1**: Independent after Setup + T004/T006 (no dependency on US2 or US3)
- **US2**: Depends on Foundational (T002, T003); depends on US1's `IInboxApiService` update (T006) for the bell wiring
- **US3**: Independent of US1 and US2 — only needs Foundational (T002, T003) for classification context reference

### Parallel Opportunities

- T002 + T003 (Foundational): parallel (different files)
- T004 + T006 (US1 client helpers): parallel (different files, no interdependency)
- T016 + T017 (US3 writers): parallel (different services)
- T020 + T021 (US3 writer tests): parallel (different test projects)
- T022 + T023 + T025 (Polish): parallel

---

## Parallel Example: User Story 3

```bash
# After Foundational phase:
# Slot A — PersonaInboxWriter
T016: Create PersonaInboxWriter + IPersonaInboxWriter
T020: Write PersonaInboxWriter unit tests
T018: Reroute PersonaService

# Slot B — EncryptionInboxWriter (in parallel with Slot A)
T017: Create EncryptionInboxWriter + IEncryptionInboxWriter
T021: Write EncryptionInboxWriter unit tests
T019: Reroute EncryptionBackgroundService
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002, T003)
3. Complete Phase 3: US1 (T004–T010)
4. **STOP and VALIDATE**: both hosts show unified timeline on both viewports
5. Demo / deploy this increment independently

### Incremental Delivery

1. Setup + Foundational → baseline clean
2. US1 (T004–T010) → unified timeline on both hosts (MVP — SC-001, SC-006, SC-008)
3. US2 (T011–T015) → bell filters to Actionable only (SC-002)
4. US3 (T016–T021) → legacy producers rerouted, no events lost (SC-003, SC-004, SC-005)
5. Polish (T022–T025) → docs, bundle gate, full E2E validation

### Parallel Team Strategy

With two developers after Foundational completes:
- Developer A: US1 (T004–T010) + US2 (T011–T015) in sequence
- Developer B: US3 (T016–T021) in parallel

---

## Notes

- Legacy `ActivityEvent` table and migrations: **DO NOT touch** (FR-019)
- `/operations` Encryption Operations page: **DO NOT touch** (FR-020)
- No Snackbar in any new UI code (Pattern #12) — use `IInlineFeedback` for own-action feedback
- All inbox writes are best-effort: wrap in `try` / `LogWarning` / swallow; never `await` inside the underlying operation's transaction scope
- `SourceEventId` keys must be deterministic so retries collapse via the spine's `(PlatformUserId, SourceEventId)` unique constraint
- PWA bundle gate (`check-pwa-bundle.ps1`) must stay green — `ActivityFeed` may only depend on PWA-safe types
- XML `<summary>` on all public members is required (Constitution III / build warning gate)
