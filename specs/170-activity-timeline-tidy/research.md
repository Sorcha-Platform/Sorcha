# Phase 0 Research: Activity Timeline Tidy

All spec items are concrete (no `[NEEDS CLARIFICATION]` markers). Research here resolves the few decisions a removal needs: how to handle the schema change, what to do with the two straggler writers, and which feed components are actually superseded. Findings are grounded in the current working tree (the spec's referenced review doc `docs/superpowers/specs/2026-06-27-transaction-activity-logging-review.md` does not exist in this tree; the inventory below was derived directly from code).

---

## D1 — Prerequisite: is F169 present in this branch?

- **Decision**: Treat "merge `origin/master` into this branch" as the first, gating task (T-PREP). Do not delete any legacy writer until the F169 Inbox writers exist in the working tree.
- **Rationale**: `git merge-base --is-ancestor f479b886 HEAD` is **false** — F169 is not in this branch. It **is** on `origin/master`. The branch forked at `8a75eb4b` (before F169). The Inbox writers (`PersonaInboxWriter`, `EncryptionInboxWriter`) that absorb persona-lifecycle and encryption events therefore are **absent** from the working tree right now. Removing the legacy writes before F169 lands would drop those event classes from the timeline — a direct SC-001 violation.
- **Alternatives considered**: (a) Re-implement the Inbox writers here — rejected, duplicates F169 and risks divergence. (b) Cherry-pick only the writers — rejected, F169 also adds the shared `ActivityFeed` read surface the timeline needs; take the whole thing via master.

## D2 — Schema removal: squash into initial migration vs. forward "drop" migration

- **Decision**: Squash. Remove the `ActivityEvents` `CreateTable` + 4 indexes from the **Up** of `20260513152714_InitialCreate.cs`, remove the matching `DropTable` from its **Down**, and remove the `ActivityEvent` entity block from `TenantDbContextModelSnapshot.cs`. Do **not** add a new forward migration.
- **Rationale**: FR-007 + the project's pre-release reset convention — there is no production data, `n1` reset is the accepted upgrade path, and a separate drop-step would pollute history (SC-004 requires the history contain **no** separate drop-step). After editing, the snapshot must match the model so `dotnet ef migrations has-pending-model-changes` reports clean.
- **Validation method**: Build the Tenant Service, then verify no pending model changes and that a fresh `dotnet ef database update` produces a schema with no `ActivityEvents` table/indexes.
- **Alternatives considered**: Forward `DropTable` migration — rejected by FR-007/SC-004 (leaves a drop-step in history; unnecessary without prod data).

## D3 — Persona-lifecycle straggler writes: repoint vs. delete

- **Decision**: **Delete** the legacy `ActivityEvent` writes in `PersonaService.ReplaceAsync` (l.268-280) and `DeleteAsync` (l.306-318). Do not repoint — F169's `PersonaInboxWriter.WritePersonaSavedAsync` / `WritePersonaDeletedAsync` already emit the equivalent Inbox entries.
- **Rationale**: FR-001 coverage is satisfied by F169 (after T-PREP). The legacy write becomes pure duplication. Deleting (not repointing) avoids a second write path.
- **Verification**: Confirm, in the merged tree, that `PersonaService` injects `IPersonaInboxWriter` and both call sites exist before deleting the `IEventService.CreateEventAsync` calls.

## D4 — Encryption-pipeline straggler writes: repoint vs. delete

- **Decision**: **Delete** `EncryptionBackgroundService.StoreActivityEventAsync` (l.383-414) and its two call sites (success l.278, failure l.378). Remove the `Sorcha.ServiceClients.Events` usings here and the dead `using` in `InstanceProjector.cs` (l.16).
- **Rationale**: F169's `EncryptionInboxWriter.WriteEncryptionCompleteAsync` / `WriteEncryptionFailedAsync` cover `EncryptionComplete` / `EncryptionFailed`. This also removes the only runtime caller of `IEventServiceClient`, which unblocks deleting the HTTP client (FR-004) and the `/api/events` POST consumer.
- **Verification**: After T-PREP, confirm the success/failure paths call the Inbox writer; only then delete the legacy method.

## D5 — `/api/events*` external consumers

- **Decision**: Safe to delete the endpoint group. The only cross-process caller was `IEventServiceClient` (Blueprint→Tenant POST `/api/events`), removed in D4. No CLI/MCP/external/test caller of the REST routes remains.
- **Rationale**: Edge case in spec ("External consumers of the legacy REST surface"). Grep across the solution found the client as the sole consumer; the admin GET surface (`/api/events/admin`) was consumed only by the orphaned UI service (D6), which has zero UI consumers.
- **Validation method**: Re-grep for `"/api/events"` and `IEventServiceClient` after edits → expect zero hits outside deleted files.

## D6 — Orphaned admin UI activity-log service

- **Decision**: Delete `IActivityLogService` + `ActivityLogService` and its DI registration (`ServiceCollectionExtensions.cs` l.350-358). Delete the now-unused `ActivityEventDto` (+ `EventsPagedResponse`/`UnreadCountResponse`/`MarkReadResponse`) and prune `EventAdminModels.cs` (`SystemEventViewModel`, `EventFilterModel`, `EventListResponse`) if no other consumer remains.
- **Rationale**: FR-005. Grep confirmed **zero** consumers of `IActivityLogService` (only its own file + its registration). DTOs are unreferenced after the service goes.
- **Verification**: Before deleting each DTO/model, grep its type name across `src/` to confirm zero remaining references (a model could be shared); delete only the truly orphaned ones.

## D7 — Feature-125 feed components: remove vs. retain

- **Decision**: **Retain** both `TransactionHistoryFeed.razor` and `RecentActivityFeed.razor`. They live in the shared `Sorcha.UI.Components.User` library and are actively rendered by the PWA (`Sorcha.Wallet.Pwa/Pages/Activity.razor` and `Pages/Index.razor` respectively).
- **Rationale**: FR-006 + the spec's "partially-shared feed component" edge case — a feed still referenced anywhere by the live UI must be kept. These are not superseded by F169 in this codebase (the durable inbox surface is `InboxPanel`, Feature 118; F169 adds the shared `ActivityFeed`, distinct from these PWA feeds). Removing them would break the PWA.
- **Alternatives considered**: Remove from web only — moot; they have no web references to remove, and the files are shared-library assets the PWA needs. No action required on them beyond confirming retention in tasks.

## D8 — Build-warning and test-hygiene posture

- **Decision**: After removals, remove all now-dead `using` directives in modified files; delete `EventServiceTests.cs` (covers removed `EventService`); edit `EncryptionBackgroundServiceTests.cs` + `EncryptionBackgroundServiceRecipientTests.cs` to drop the `IEventServiceClient` mock field/setup/verify (keep the rest of each test).
- **Rationale**: SC-003 / FR-008 — no failing or skipped tests left behind, no new warnings (Release builds are warning-free per Constitution V).
- **Validation method**: `dotnet build` (warning count unchanged) + `dotnet test` for the two affected suites green.

---

## Consolidated decision table

| ID | Topic | Decision |
|----|-------|----------|
| D1 | F169 prerequisite | Merge `origin/master` first (T-PREP); writers absent until then |
| D2 | Schema change | Squash out of initial migration + snapshot; no forward drop migration |
| D3 | Persona writes | Delete legacy writes; rely on F169 `PersonaInboxWriter` |
| D4 | Encryption writes | Delete `StoreActivityEventAsync` + call sites; rely on F169 `EncryptionInboxWriter` |
| D5 | `/api/events*` consumers | None remain after D4 → delete endpoints |
| D6 | Orphaned UI service | Delete `IActivityLogService` + DTOs (zero consumers) |
| D7 | F125 feeds | Retain both (PWA still uses them) |
| D8 | Tests/warnings | Delete dead tests, prune mocks, clear dead usings |
