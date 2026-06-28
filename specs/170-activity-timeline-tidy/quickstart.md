# Quickstart / Validation Guide: Activity Timeline Tidy

Run these to prove the tidy is complete and safe. Maps to the spec's Success Criteria (SC-001…SC-005). This is a verification guide — concrete edits live in `tasks.md`.

## Prerequisites

- .NET 10 SDK, Docker Desktop (for Postgres / fresh-provision check).
- **T-PREP done**: `origin/master` (which contains F169, commit `f479b886`) merged into `170-activity-timeline-tidy`. Confirm:
  ```bash
  git merge-base --is-ancestor f479b886 HEAD && echo "F169 present" || echo "STOP — merge master first"
  ls src/Services/Sorcha.Tenant.Service/Services/PersonaInboxWriter.cs
  ls src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionInboxWriter.cs
  ```
  All three must succeed before any removal.

## 1. SC-002 / FR-002…FR-005 — legacy surface is gone

```bash
# Each of these must return ZERO matches (outside specs/ and deleted files):
grep -rn "class ActivityEvent\|DbSet<ActivityEvent>\|ConfigureActivityEvent" src/
grep -rn "IEventService\b\|class EventService\b\|EventCleanupService" src/Services/Sorcha.Tenant.Service/
grep -rn "IEventServiceClient\|EventServiceClient\|CreateActivityEventRequest" src/ tests/
grep -rn "IActivityLogService\|ActivityLogService" src/
grep -rn "\"/api/events" src/ tests/
```
Expected: no results. (Confirms one activity pipeline remains.)

## 2. FR-006 / D7 — retained PWA feeds still present

```bash
# Must still EXIST (PWA depends on them) — do not delete:
ls src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/History/TransactionHistoryFeed.razor
ls src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Home/RecentActivityFeed.razor
grep -rn "TransactionHistoryFeed\|RecentActivityFeed" src/Apps/Sorcha.Wallet.Pwa/   # still referenced
```

## 3. SC-003 / FR-008 — build + tests green, no new warnings

```bash
dotnet build -c Release            # warning-free (Constitution V); no unresolved refs
dotnet test --filter "FullyQualifiedName~Sorcha.Tenant.Service.Tests"
dotnet test --filter "FullyQualifiedName~Sorcha.Blueprint.Service.Tests"
```
Expected: build succeeds; suites pass; `EventServiceTests` removed; encryption tests pass without `IEventServiceClient` mocks; nothing skipped.

## 4. SC-004 / FR-007 — clean schema on fresh provision

```bash
cd src/Services/Sorcha.Tenant.Service
dotnet ef migrations has-pending-model-changes      # expect: none (snapshot ≡ model)
dotnet ef migrations list                           # expect: NO new "*DropActivityEvents" migration
# Fresh DB:
dotnet ef database update
psql "$TENANT_DB" -c "\d ActivityEvents"            # expect: relation does not exist
psql "$TENANT_DB" -c "\di IX_ActivityEvent_*"       # expect: 0 rows
```
Also inspect `Migrations/20260513152714_InitialCreate.cs` — no `ActivityEvents` `CreateTable`/`CreateIndex`/`DropTable`; and `TenantDbContextModelSnapshot.cs` — no `ActivityEvent` entity block.

## 5. SC-001 — no event-class regression in the timeline (functional)

With the merged branch running (Aspire or docker-compose), trigger each previously-legacy event and confirm it still appears in the unified timeline (now Inbox-sourced):

| Action | Expected timeline entry |
|--------|-------------------------|
| Replace a persona | persona-saved entry appears |
| Delete a persona | persona-deleted entry appears |
| Encryption job completes | encryption-complete entry appears |
| Encryption job fails | encryption-failed entry appears |

Each must appear via the Inbox spine (`InboxPanel` / F169 `ActivityFeed`) — none missing. Reference the coverage map in `data-model.md`.

## 6. SC-005 / FR-009 — docs updated

Confirm these no longer describe the legacy `/api/events*` surface and point to the Inbox spine:
- `src/Services/Sorcha.Tenant.Service/README.md`
- `.claude/skills/sorcha-architecture/SKILL.md`
- `docs/reference/API-DOCUMENTATION.md`
- `docs/reference/development-status.md`

## Done when

- [ ] T-PREP confirmed (F169 in branch)
- [ ] Section 1 greps all empty; Section 2 files retained
- [ ] Build warning-free; both suites green
- [ ] No pending model changes; no drop migration; fresh DB has no `ActivityEvents`
- [ ] All four event classes still visible in the timeline
- [ ] Docs updated
