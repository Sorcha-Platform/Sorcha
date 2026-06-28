# Implementation Plan: Activity Timeline Tidy

**Branch**: `170-activity-timeline-tidy` | **Date**: 2026-06-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/170-activity-timeline-tidy/spec.md`

## Summary

Feature 169 made the **Inbox spine** the single source of truth for the unified activity timeline. This feature is the **tidy phase**: it deletes the now-legacy parallel channels so exactly one activity pipeline remains. Concretely, it removes the Tenant Service `ActivityEvent` store (entity, EF config, indexes, `EventService`/`IEventService`, `EventCleanupService` retention worker, and the `/api/events*` endpoints), the cross-service `IEventServiceClient` HTTP client and its `CreateActivityEventRequest` model, the orphaned admin-facing `IActivityLogService` UI client plus its DTOs, and the now-unused `ActivityEventDto` model — re-pointing or removing the two remaining legacy writers (persona lifecycle, encryption pipeline) onto the F169 Inbox writers. The `ActivityEvent` table + indexes are squashed out of the **initial** EF migration and snapshot (pre-release reset convention; an `n1` reset is the accepted upgrade path). Feature-125 `TransactionHistoryFeed` / `RecentActivityFeed` PWA components are **retained** (still referenced by the PWA) — they are not superseded by F169 in this codebase.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: .NET Aspire 13, EF Core (PostgreSQL provider), Minimal APIs + Scalar, Blazor (web + PWA), xUnit + FluentAssertions + Moq

**Storage**: PostgreSQL via `TenantDbContext` (the `ActivityEvents` table is the removal target). No production data — `n1` reset acceptable.

**Testing**: `dotnet test` (xUnit). Affected suites: `Sorcha.Tenant.Service.Tests`, `Sorcha.Blueprint.Service.Tests`.

**Target Platform**: Linux containers / Aspire-orchestrated services; Blazor WASM clients.

**Project Type**: Web (multi-service backend + Blazor web app + PWA).

**Performance Goals**: N/A — removal only; no runtime behaviour added.

**Constraints**: No user-visible event-class regression (FR-001/SC-001). No new build warnings (SC-003). Build green + tests green after removal. Squash schema change into the **initial** migration (FR-007), no separate drop migration.

**Scale/Scope**: ~13 files deleted, ~6 files modified (DbContext, two Program.cs, two service-client registration sites, one UI DI registration), plus migration + snapshot edits and doc-sync updates. Single repository.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|-----------|
| I. Microservices-First | PASS — removal reduces cross-service coupling (deletes Blueprint→Tenant `/api/events` call). No new upward dependencies. |
| II. Security First | PASS — no auth/crypto change; removes an authenticated endpoint group cleanly (DI + mapping removed together). |
| III. API Documentation | PASS — removes endpoints; API-DOCUMENTATION.md and Scalar surface updated to drop `/api/events*` (FR-009). |
| IV. Testing | PASS — tests covering removed surface are deleted (not skipped); remaining suites must stay green (SC-003). Coverage of retained code unchanged. |
| V. Code Quality | PASS — net reduction; nullable + no-warning rules upheld; removes dead `using` directives. |
| VI. Blueprint Standards | N/A — no blueprint changes. |
| VII. Domain-Driven Design | PASS — consolidates on one "activity" concept (Inbox spine), removing the duplicate "ActivityEvent" vocabulary. |
| VIII. Observability | PASS — no telemetry contract removed that anything depends on; retention worker removal leaves no dangling hosted-service registration. |

**Result**: No violations. Complexity Tracking not required.

**Hard prerequisite (gating, not a constitution violation):** F169 (commit `f479b886`) is **already on `origin/master`** but is **NOT yet in this branch's history** — `170-activity-timeline-tidy` was cut from `8a75eb4b`, before F169 merged. The Inbox writers this tidy depends on (`PersonaInboxWriter`, `EncryptionInboxWriter`) therefore do **not exist in the working tree yet**. The first task (T-PREP) is to merge/rebase `origin/master` into this branch so F169 is present; the removal is unsafe until then (would drop persona + encryption events from the timeline).

## Project Structure

### Documentation (this feature)

```text
specs/170-activity-timeline-tidy/
├── plan.md              # This file
├── research.md          # Phase 0 output — decisions (squash-vs-forward migration, repoint-vs-delete writers, feed retention)
├── data-model.md        # Phase 1 output — the removed ActivityEvent entity + retained Inbox spine boundary
├── quickstart.md        # Phase 1 output — verification guide (build, test, fresh-provision, timeline parity)
├── contracts/
│   └── removed-events-api.md   # The /api/events* REST surface being deleted (negative contract)
├── checklists/
│   └── requirements.md  # Existing spec-quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root) — files in scope

```text
src/Services/Sorcha.Tenant.Service/
├── Models/ActivityEvent.cs                      # DELETE (entity + EventSeverity enum)
├── Data/TenantDbContext.cs                      # MODIFY — drop DbSet (l.71), ConfigureActivityEvent call (l.150) + method (l.965-1009)
├── Services/Interfaces/IEventService.cs         # DELETE
├── Services/EventService.cs                     # DELETE
├── Services/EventCleanupService.cs              # DELETE (retention BackgroundService)
├── Endpoints/EventEndpoints.cs                  # DELETE (/api/events* group + request records)
├── Migrations/20260513152714_InitialCreate.cs   # MODIFY — remove ActivityEvents CreateTable + 4 indexes + DropTable
├── Migrations/TenantDbContextModelSnapshot.cs    # MODIFY — remove ActivityEvent entity block
├── Services/PersonaService.cs                   # MODIFY — drop legacy ActivityEvent writes (l.268-280, 306-318); rely on F169 PersonaInboxWriter
└── Program.cs                                   # MODIFY — drop AddScoped<IEventService> (l.130-131), AddHostedService<EventCleanupService> (l.132), MapEventEndpoints (l.268)

src/Common/Sorcha.ServiceClients.Http/
├── Events/IEventServiceClient.cs               # DELETE
├── Events/EventServiceClient.cs                # DELETE
├── Events/Models/CreateActivityEventRequest.cs # DELETE (then remove empty Events/ dir)
└── Extensions/HttpServiceCollectionExtensions.cs # MODIFY — drop AddHttpClient/AddScoped (l.72-73) + using

src/Services/Sorcha.Blueprint.Service/Services/Implementation/
├── EncryptionBackgroundService.cs              # MODIFY — drop StoreActivityEventAsync (l.383-414) + its 2 call sites; rely on F169 EncryptionInboxWriter
└── InstanceProjector.cs                        # MODIFY — drop dead `using Sorcha.ServiceClients.Events;`

src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Services/Admin/ActivityLogService.cs        # DELETE (IActivityLogService + impl — zero consumers)
├── Models/Admin/EventAdminModels.cs            # DELETE/PRUNE (SystemEventViewModel, EventFilterModel, EventListResponse — if unused elsewhere)
└── Extensions/ServiceCollectionExtensions.cs   # MODIFY — drop IActivityLogService registration (l.350-358)

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Shared/ActivityEventDto.cs # DELETE (ActivityEventDto + paged/unread/markread responses — now unused)

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/History/TransactionHistoryFeed.razor  # RETAIN (PWA Activity.razor uses it)
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Home/RecentActivityFeed.razor          # RETAIN (PWA Index.razor uses it)

tests/
├── Sorcha.Tenant.Service.Tests/Services/EventServiceTests.cs   # DELETE (covers removed EventService)
└── Sorcha.Blueprint.Service.Tests/Services/EncryptionBackgroundService*Tests.cs # MODIFY — drop IEventServiceClient mocks/setups/asserts

docs/  # MODIFY per FR-009 — Tenant Service README, sorcha-architecture skill, API-DOCUMENTATION.md, development-status.md
```

**Structure Decision**: Existing multi-service + Blazor layout is unchanged; this is a pure subtraction within established service folders. No new projects or directories. The only additive artifacts are the spec design docs above.

## Complexity Tracking

> No constitution violations — section intentionally empty.
