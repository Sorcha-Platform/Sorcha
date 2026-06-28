---
description: "Task list for Activity Timeline Tidy (Feature 170)"
---

# Tasks: Activity Timeline Tidy

**Input**: Design documents from `/specs/170-activity-timeline-tidy/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

---

## Phase 1: Setup — Merge F169 Prerequisite

**Purpose**: Bring F169 (the Inbox writers) into this branch. The removal is **unsafe** until `PersonaInboxWriter` and `EncryptionInboxWriter` are present in the working tree. All subsequent phases depend on this.

**⚠️ CRITICAL GATE**: No removal task can begin until T001 is confirmed complete.

- [x] T001 Merge `origin/master` into branch `170-activity-timeline-tidy` to bring in F169 (commit `f479b886`), then confirm with `git merge-base --is-ancestor f479b886 HEAD && echo OK`, `ls src/Services/Sorcha.Tenant.Service/Services/PersonaInboxWriter.cs`, and `ls src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionInboxWriter.cs`

**Checkpoint**: All three commands above succeed → F169 is present → safe to proceed.

---

## Phase 2: Foundational — Pre-removal Verification

**Purpose**: Confirm coverage and consumer-safety assertions before any deletion. These are read-only checks; none modify source files.

**⚠️ CRITICAL**: All four checks must pass before any removal work begins.

- [x] T002 [P] Verify event-class coverage map (FR-001): confirm `PersonaInboxWriter` contains `WritePersonaSavedAsync` and `WritePersonaDeletedAsync`, and `EncryptionInboxWriter` contains `WriteEncryptionCompleteAsync` and `WriteEncryptionFailedAsync`, by reading `src/Services/Sorcha.Tenant.Service/Services/PersonaInboxWriter.cs` and `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionInboxWriter.cs`
- [x] T003 [P] Verify no external consumers of `/api/events*` remain (D5 / edge case): run `grep -rn "/api/events" src/ tests/` and `grep -rn "IEventServiceClient\|EventServiceClient\|CreateActivityEventRequest" src/ tests/` — expected callers are only the files listed in the removal scope (endpoints file, service-client files, encryption service); any other hit is a blocker
- [x] T004 [P] Verify F125 feed components are retained (D7 / FR-006): confirm `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/History/TransactionHistoryFeed.razor` and `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Home/RecentActivityFeed.razor` exist and are still referenced in `src/Apps/Sorcha.Wallet.Pwa/Pages/Activity.razor` and `src/Apps/Sorcha.Wallet.Pwa/Pages/Index.razor`
- [x] T005 [P] Verify `EventAdminModels.cs` types (`SystemEventViewModel`, `EventFilterModel`, `EventListResponse`) have no consumers outside `ActivityLogService.cs` by running `grep -rn "SystemEventViewModel\|EventFilterModel\|EventListResponse" src/` before scheduling deletion

**Checkpoint**: All four checks pass with no blockers → safe to begin US1 removals.

---

## Phase 3: User Story 1 — No Regression in the Unified Timeline (Priority: P1) 🎯 MVP

**Goal**: Remove the two remaining legacy `ActivityEvent` writes so the timeline relies solely on the F169 Inbox writers, with zero event-class regression.

**Independent Test**: After this phase, trigger persona.replaced, persona.deleted, EncryptionComplete, and EncryptionFailed events in a running instance and confirm each appears in the unified Inbox-sourced timeline (quickstart.md §5). Build must remain green.

### Implementation for User Story 1

- [x] T006 [US1] Remove the legacy `ActivityEvent` write block from `PersonaService.ReplaceAsync` (`src/Services/Sorcha.Tenant.Service/Services/PersonaService.cs` lines ~268–280): delete the `IEventService.CreateEventAsync` call and its surrounding null-check or try-block; confirm the file retains its `PersonaInboxWriter` call that F169 already added
- [x] T007 [US1] Remove the legacy `ActivityEvent` write block from `PersonaService.DeleteAsync` (`src/Services/Sorcha.Tenant.Service/Services/PersonaService.cs` lines ~306–318): same pattern as T006; confirm `PersonaInboxWriter.WritePersonaDeletedAsync` call is retained
- [x] T008 [US1] Delete the private method `StoreActivityEventAsync` (lines ~383–414) and its two call sites (success path ~l.278, failure path ~l.378) from `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`; confirm F169's `EncryptionInboxWriter` calls are present at those paths
- [x] T009 [US1] Remove the dead `using Sorcha.ServiceClients.Events;` directive from `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceProjector.cs` (~l.16)
- [x] T010 [US1] Build the solution (`dotnet build -c Release`) and confirm zero errors and no new warnings; run `dotnet test --filter "FullyQualifiedName~Sorcha.Tenant.Service.Tests"` and `dotnet test --filter "FullyQualifiedName~Sorcha.Blueprint.Service.Tests"` to confirm both suites remain green after the write removals

**Checkpoint**: Build green, both suites pass, and all four event classes still appear in the unified timeline → US1 complete.

---

## Phase 4: User Story 2 — Reduced Maintenance Surface (Priority: P2)

**Goal**: Delete every remaining legacy surface (entity, service, endpoints, retention worker, HTTP client, orphaned UI service, DTOs) and remove all dead test coverage so exactly one activity pipeline exists.

**Independent Test**: After this phase, search the codebase for all legacy type names (quickstart.md §1) — each grep returns zero results outside deleted/spec files; `dotnet build -c Release` is warning-free; both affected test suites pass (quickstart.md §3).

### Tenant Service — entity, service, endpoints, DI

- [x] T011 [P] [US2] Delete `src/Services/Sorcha.Tenant.Service/Models/ActivityEvent.cs` (entity class `ActivityEvent` + enum `EventSeverity`)
- [x] T012 [P] [US2] Delete `src/Services/Sorcha.Tenant.Service/Services/Interfaces/IEventService.cs`
- [x] T013 [P] [US2] Delete `src/Services/Sorcha.Tenant.Service/Services/EventService.cs`
- [x] T014 [P] [US2] Delete `src/Services/Sorcha.Tenant.Service/Services/EventCleanupService.cs` (the scheduled retention `BackgroundService`)
- [x] T015 [P] [US2] Delete `src/Services/Sorcha.Tenant.Service/Endpoints/EventEndpoints.cs` (the `/api/events*` endpoint group and its inline request records)
- [x] T016 [US2] Modify `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs`: remove the `DbSet<ActivityEvent> ActivityEvents` property (~l.71), remove the `ConfigureActivityEvent(modelBuilder)` call (~l.150), and delete the entire `ConfigureActivityEvent` private method (~l.965–1009); remove the now-dead `using` directive for `ActivityEvent` if present
- [x] T017 [US2] Modify `src/Services/Sorcha.Tenant.Service/Program.cs`: remove `AddScoped<IEventService, EventService>` (~l.130–131), `AddHostedService<EventCleanupService>` (~l.132), and the `MapEventEndpoints()` call (~l.268); remove dead `using` directives

### ServiceClients — HTTP client removal

- [x] T018 [P] [US2] Delete `src/Common/Sorcha.ServiceClients.Http/Events/IEventServiceClient.cs`
- [x] T019 [P] [US2] Delete `src/Common/Sorcha.ServiceClients.Http/Events/EventServiceClient.cs`
- [x] T020 [P] [US2] Delete `src/Common/Sorcha.ServiceClients.Http/Events/Models/CreateActivityEventRequest.cs`; if the `Events/` directory is now empty, delete the directory too
- [x] T021 [US2] Modify `src/Common/Sorcha.ServiceClients.Http/Extensions/HttpServiceCollectionExtensions.cs`: remove the `AddHttpClient`/`AddScoped` lines for `IEventServiceClient`/`EventServiceClient` (~l.72–73) and the corresponding `using` directive; confirm T018–T020 are done first

### Sorcha.UI.Core — orphaned admin UI service

- [x] T022 [P] [US2] Delete `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/ActivityLogService.cs` (contains both `IActivityLogService` interface and `ActivityLogService` implementation)
- [x] T023 [P] [US2] Delete `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/EventAdminModels.cs` (contains `SystemEventViewModel`, `EventFilterModel`, `EventListResponse`) — only after T005 confirmed zero consumers outside `ActivityLogService.cs`
- [x] T024 [US2] Modify `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`: remove the `IActivityLogService` DI registration block (~l.350–358) and the dead `using` directive

### Sorcha.UI.Components.User — orphaned DTO

- [x] T025 [P] [US2] Delete `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Shared/ActivityEventDto.cs` (contains `ActivityEventDto`, `EventsPagedResponse`, `UnreadCountResponse`, `MarkReadResponse`)

### Test hygiene

- [x] T026 [P] [US2] Delete `tests/Sorcha.Tenant.Service.Tests/Services/EventServiceTests.cs` (covers removed `EventService`; do not skip — delete entirely per FR-008)
- [x] T027 [US2] Modify `tests/Sorcha.Blueprint.Service.Tests/Services/EncryptionBackgroundServiceTests.cs` and any sibling `EncryptionBackgroundServiceRecipientTests.cs`: remove the `IEventServiceClient` mock field, its `Setup` calls, and any `Verify`/assertion lines referencing the removed method; retain all remaining test coverage

### Build and test verification

- [x] T028 [US2] Build the solution (`dotnet build -c Release`) and confirm zero compile errors and no new build warnings; if any `CS0246` (missing type) or warning appears, locate the stray reference and remove it
- [x] T029 [US2] Run `dotnet test --filter "FullyQualifiedName~Sorcha.Tenant.Service.Tests"` and `dotnet test --filter "FullyQualifiedName~Sorcha.Blueprint.Service.Tests"` — both suites must pass; confirm `EventServiceTests` is absent and no test is skipped
- [x] T030 [US2] Run the quickstart.md §1 grep battery to confirm all six grep patterns return zero matches outside deleted/spec files: `class ActivityEvent`, `IEventService\b`, `IEventServiceClient`, `IActivityLogService`, `"/api/events"`, `ActivityLogService`

**Checkpoint**: All greps zero, build warning-free, both suites green → US2 complete.

---

## Phase 5: User Story 3 — Clean Schema on Fresh Provision (Priority: P3)

**Goal**: Squash the `ActivityEvents` table and its four indexes out of the initial migration and model snapshot so a fresh database provision never creates the legacy table.

**Independent Test**: Run `dotnet ef migrations has-pending-model-changes` (expect none); run `dotnet ef migrations list` (expect no drop-step migration); provision a fresh DB and confirm `\d ActivityEvents` returns "relation does not exist" (quickstart.md §4).

### Implementation for User Story 3

- [x] T031 [US3] Edit `src/Services/Sorcha.Tenant.Service/Migrations/20260513152714_InitialCreate.cs`: in the `Up()` method remove the `CreateTable("ActivityEvents", ...)` block (l.~20–42) and the four `CreateIndex` calls for `IX_ActivityEvent_*` (l.~802–826); in the `Down()` method remove the corresponding `DropTable("ActivityEvents")` call (l.~1262–1263)
- [x] T032 [US3] Edit `src/Services/Sorcha.Tenant.Service/Migrations/TenantDbContextModelSnapshot.cs`: locate and delete the entire `Sorcha.Tenant.Service.Models.ActivityEvent` entity block (l.~28–100), including all property/index fluent-API calls within it; leave the surrounding model builder intact
- [x] T033 [US3] From `src/Services/Sorcha.Tenant.Service/` run `dotnet ef migrations has-pending-model-changes` — must report no pending changes (snapshot ≡ model); also run `dotnet ef migrations list` and confirm no migration name contains "DropActivityEvents" or "ActivityEvent"

**Checkpoint**: No pending model changes, no extra migration, fresh DB has no `ActivityEvents` table → US3 complete.

---

## Phase 6: Polish — Documentation Sync (FR-009)

**Purpose**: Update all documentation that described the legacy `/api/events*` surface to reflect the Inbox spine as the single activity source (SC-005).

- [x] T034 [P] Update `src/Services/Sorcha.Tenant.Service/README.md`: remove the "Activity Events" endpoints section (the `/api/events` table rows and any description of `IEventService`/`EventCleanupService`); add a note that activity events flow through the Inbox spine (Features 118 + 169)
- [x] T035 [P] Update `.claude/skills/sorcha-architecture/SKILL.md`: remove any reference to the legacy activity-event REST surface, `IEventService`, `IEventServiceClient`, or `/api/events*`; update to describe the Inbox spine as the sole activity pipeline
- [x] T036 [P] Update `docs/reference/API-DOCUMENTATION.md`: remove the `Events` tag section and all `/api/events*` route entries; add a cross-reference note pointing to the Inbox/unified-timeline endpoints
- [x] T037 [P] Update `docs/reference/development-status.md`: update Tenant Service status to reflect the legacy activity-event surface removal; remove any mention of `EventService`/`EventCleanupService` from the service feature list
- [x] T038 Run the full quickstart.md validation suite: §1 (all greps zero), §2 (F125 feeds still present), §3 (build warning-free + both suites green), §4 (schema clean — no `ActivityEvents` table), §5 (trigger the four event classes and confirm timeline appearance), §6 (docs updated) — confirm all six sections pass

**Checkpoint**: All quickstart.md sections pass → feature complete.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 (T001 must pass) — **BLOCKS all user stories**
- **Phase 3 (US1)**: Depends on Phase 2 all-pass — P1 priority
- **Phase 4 (US2)**: Depends on Phase 3 complete — P2 priority; many tasks parallelizable
- **Phase 5 (US3)**: Depends on Phase 4 complete (TenantDbContext must be modified first) — P3 priority
- **Phase 6 (Polish)**: Depends on Phases 3–5 complete; doc tasks [P] run in parallel

### Within Phase 4 (largest phase)

Tasks T011–T015, T018–T020, T022–T023, T025–T026 are fully independent file deletions and can run in parallel. T016, T017, T021, T024 are modifications to shared-infrastructure files and should run after their respective deletions to avoid editing an import that's about to disappear.

### Parallel Opportunities

```bash
# Phase 2 — all four verification checks in parallel:
T002  T003  T004  T005

# Phase 4 — batch 1: independent deletions (run together):
T011  T012  T013  T014  T015   # Tenant entity/service/endpoints
T018  T019  T020               # ServiceClients deletions
T022  T023  T025  T026         # UI service, models, DTO, test deletion

# Phase 4 — batch 2: modifications (after batch 1):
T016  T017   # TenantDbContext + Program.cs
T021         # HttpServiceCollectionExtensions
T024         # UI ServiceCollectionExtensions
T027         # Encryption test pruning

# Phase 4 — batch 3: verification (after batch 2):
T028  T029  T030

# Phase 6 — doc updates in parallel:
T034  T035  T036  T037
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Merge master (T001)
2. Complete Phase 2: Verification checks (T002–T005)
3. Complete Phase 3: Remove straggler writes, build green (T006–T010)
4. **STOP and VALIDATE**: Trigger the four event classes — confirm timeline shows all four
5. US1 is independently shippable (the timeline works correctly; legacy code still present but no longer called)

### Incremental Delivery

1. Phase 1 + 2 → prerequisites confirmed
2. Phase 3 (US1) → writer removals, timeline verified
3. Phase 4 (US2) → all legacy code deleted, build + tests green
4. Phase 5 (US3) → schema squashed, fresh provision clean
5. Phase 6 (Polish) → docs synced, full quickstart validation

### Sequencing note for a single developer

Follow the phase order strictly. The gating order exists to protect SC-001: **never delete a legacy writer before confirming the Inbox equivalent handles that event class**. Phase 2 verifications are the formal checkpoint for this.

---

## Notes

- **[P]** tasks target different files and have no inter-task dependencies within their batch
- **[Story]** label maps each task to its user story for traceability
- The F125 feed components (`TransactionHistoryFeed`, `RecentActivityFeed`) are **explicitly NOT in scope** — do not delete them (FR-006, D7)
- After every delete task, remove dead `using` directives from any file that previously imported the deleted type
- Commit after each phase checkpoint so the build-green state is preserved
- Run `dotnet build -c Release` (not just Debug) — Constitution V requires warning-free Release builds
