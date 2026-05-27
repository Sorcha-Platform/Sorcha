---
description: "Task list for Cross-Device Citizen Presentation History (F114 US5 PR3)"
---

# Tasks: Cross-Device Citizen Presentation History

**Input**: Design documents from `/specs/134-presentation-history/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/presentation-history.openapi.yaml, quickstart.md

**Tests**: INCLUDED — the spec defines Independent Test criteria per story, the design doc carries a test plan (§8), and the constitution mandates >85% coverage for new code.

**Organization**: Tasks grouped by user story. Phase 2 (Foundational) is the durable-store plumbing every story depends on; the user-story phases add the read, delete, and merge surfaces.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 (user-story phases only)

## Path conventions

Web shape (Wallet Service backend + Blazor WASM PWA), matching the existing Feature 114 layout. All server code is in the Wallet Service + the shared `Sorcha.Wallet.Portable` domain project; the client method is in `Sorcha.ServiceClients.Http`; PWA changes are confined to the Activity surface.

---

## Phase 1: Setup

**Purpose**: Establish a clean baseline on the feature branch.

- [ ] T001 Confirm the branch builds clean before changes: `dotnet build src/Services/Sorcha.Wallet.Service/Sorcha.Wallet.Service.csproj` and `dotnet build src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj` (baseline for regression comparison).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The durable per-citizen store and the forwarder that fills it. Every user story reads/writes this store, so it MUST be complete first.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 [P] Create `CitizenPresentationRecord` entity in `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CitizenPresentationRecord.cs` per data-model.md (composite key `PlatformUserId`+`EntryId`; `CredentialId`, `VerifierLabel`, `VerifierDid`, `DisclosedClaims` string[], `PresentedAt`, `Outcome`, `ReportedAt`; **no register fields, no claim values**).
- [ ] T003 Map `CitizenPresentationRecord` on `WalletDbContext.OnModelCreating` (composite PK; `DisclosedClaims` as jsonb; index `(PlatformUserId, PresentedAt desc)`) in the `WalletDbContext` file under `src/Core/Sorcha.Wallet.Portable/`.
- [ ] T004 Create EF migration `AddCitizenPresentationRecord` (set `$env:ConnectionStrings__Sorcha__Postgres` first; do NOT pass `--no-build`) via `dotnet ef migrations add --project src/Core/Sorcha.Wallet.Portable --startup-project src/Services/Sorcha.Wallet.Service`.
- [ ] T005 [P] Define `ICitizenPresentationStore` in `src/Services/Sorcha.Wallet.Service/Services/Interfaces/ICitizenPresentationStore.cs` (`UpsertAsync(platformUserId, entry, ct)`, `ListAsync(platformUserId, ct)` newest-first, `DeleteAsync(platformUserId, entryId, ct) -> bool`).
- [ ] T006 Implement `EfCoreCitizenPresentationStore` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreCitizenPresentationStore.cs` (wire↔entity mapping per data-model.md; upsert idempotent on PK preserving `ReportedAt`; list newest-first; scoped delete).
- [ ] T007 [P] Implement `InMemoryCitizenPresentationStore` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/InMemoryCitizenPresentationStore.cs` (fallback + test double; same semantics).
- [ ] T008 Register the store in `src/Services/Sorcha.Wallet.Service/Program.cs` via `IStorageRegistrationLog` (`RegisterPersistent(...,"postgres")` when a connection string is present, else `RegisterInMemory(...)`; **NOT** added to the F113 fail-fast audited list) and add an OTel counter `sorcha_citizen_presentation_store_total{op=upsert|list|delete}` on the existing `Sorcha.Wallet.Service` meter.
- [ ] T009 Replace PR2's `LoggingPresentationLogForwarder` with `CitizenPresentationStoreForwarder : IPresentationLogForwarder` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenPresentationStoreForwarder.cs` (calls `store.UpsertAsync`) and swap the DI registration in `src/Services/Sorcha.Wallet.Service/Program.cs`.
- [ ] T010 [P] Store unit tests in `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenPresentationStoreTests.cs` (upsert idempotency on `(platformUserId, entryId)`; list newest-first; delete own row vs cross-user no-op; `ReportedAt` preserved on re-upsert) using the `TestCitizenWalletDbContext` InMemory pattern.
- [ ] T011 [P] Forwarder unit test in `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenPresentationStoreForwarderTests.cs` (`ForwardAsync` calls `UpsertAsync` with the mapped entry).

**Checkpoint**: Reported presentations now land durably and idempotently in the Wallet Service store. Cross-device read/delete/merge can be built on top.

---

## Phase 3: User Story 1 — See my presentation history on a new device (Priority: P1) 🎯 MVP

**Goal**: A freshly-paired device shows the citizen's past presentations from any device.

**Independent Test**: Present on device A → sync → pair device B → open Activity on B → A's presentation is listed.

### Tests for User Story 1

- [ ] T012 [P] [US1] Endpoint handler test for the list path in `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/PresentationHistoryEndpointTests.cs` (reflection-based static-handler invocation, per `CitizenWalletEnrolEndpointTests`): returns the caller's rows newest-first; empty history → empty list (not 404); missing `platform_user_id` claim → 401.
- [ ] T013 [P] [US1] PWA Activity merge test (server entries appear) in `tests/Sorcha.Wallet.Pwa.Tests/Services/ActivityMergeTests.cs` (mock `ICitizenWalletClient.ListPresentationsAsync`).

### Implementation for User Story 1

- [ ] T014 [US1] Add `GET /api/v1/wallet/presentations` handler to `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` (citizen JWT, `RateLimitPolicies.Strict`; resolves `platform_user_id`; `store.ListAsync`; returns `PresentationHistoryResponse`; `.WithSummary`/`.WithDescription`/`.Produces<>`).
- [ ] T015 [US1] Add `ListPresentationsAsync` to `ICitizenWalletClient` and `CitizenWalletClient` in `src/Common/Sorcha.ServiceClients.Http/CitizenWallet/` (GET `/presentations` → `PresentationHistoryResponse`).
- [ ] T016 [US1] Wire the PWA Activity page to fetch and display server history in `src/Apps/Sorcha.Wallet.Pwa/Pages/Activity.razor` (call `ListPresentationsAsync` on load/after-sync; surface server entries in the existing F125 feed alongside the local log).

**Checkpoint**: Cross-device history is visible — the MVP. Stop and validate SC-001 / SC-005.

---

## Phase 4: User Story 2 — Remove a presentation from my history everywhere (Priority: P2)

**Goal**: Server-authoritative delete — gone on all devices, stays gone.

**Independent Test**: Delete on device B → open Activity on A → entry absent and stays absent through syncs.

### Tests for User Story 2

- [ ] T017 [P] [US2] Delete endpoint handler test in `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/PresentationHistoryEndpointTests.cs` (204 deleting own entry; 204 cross-user/non-existent — indistinguishable; 401 missing claim).

### Implementation for User Story 2

- [ ] T018 [US2] Add `DELETE /api/v1/wallet/presentations/{id}` handler to `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` (citizen JWT, Strict; `store.DeleteAsync(platformUserId, id)`; always 204 — idempotent, cross-user-indistinguishable).
- [ ] T019 [US2] Add `DeletePresentationAsync` to `ICitizenWalletClient` and `CitizenWalletClient` in `src/Common/Sorcha.ServiceClients.Http/CitizenWallet/` (DELETE `/presentations/{id}`).
- [ ] T020 [US2] Wire PWA per-row delete to server + local in `src/Apps/Sorcha.Wallet.Pwa/Pages/Activity.razor` (`DeletePresentationAsync` then `IPresentationLog.DeleteAsync`) and reframe the FR-009 confirmation messaging ("removed from your history on all your devices; does not affect the verifier's own records").
- [ ] T021 [P] [US2] PWA test: per-row delete invokes both server and local delete; messaging reframed — in `tests/Sorcha.Wallet.Pwa.Tests/Services/ActivityMergeTests.cs`.

**Checkpoint**: Delete is coherent across devices (SC-003). US1 + US2 both work independently.

---

## Phase 5: User Story 3 — Immediate, consistent history without flicker (Priority: P3)

**Goal**: Instant local feedback, exactly-once after sync, no reappear-after-remote-delete.

**Independent Test**: Present offline → appears immediately; reconnect → after sync still appears exactly once; an entry deleted on another device does not resurrect from a stale local copy.

### Tests for User Story 3

- [ ] T022 [P] [US3] PWA merge-rule tests in `tests/Sorcha.Wallet.Pwa.Tests/Services/ActivityMergeTests.cs`: just-made (`!SyncedToServer`) shows immediately; after sync the entry appears exactly once (synced local copy suppressed); a server-absent entry with a lingering synced local copy is NOT displayed (no resurrection).

### Implementation for User Story 3

- [ ] T023 [US3] Refine the Activity merge in `src/Apps/Sorcha.Wallet.Pwa/Pages/Activity.razor` (and any merge helper) to the precise rule `display = server history ∪ {local entries where !SyncedToServer}` — synced local entries are display-suppressed in favour of the server list.

**Checkpoint**: All three stories functional and independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T024 [P] Update `specs/114-citizen-wallet-pwa/tasks.md`: mark T132–T135 (OfflinePresentationConsumer) **superseded by feature 134** (consumer model dropped); reframe T142–T143 to point at the cross-device Activity surface.
- [ ] T025 [P] Add a "Superseded by `specs/134-presentation-history/`" header to `specs/114-citizen-wallet-pwa/contracts/presentation-lifecycle-offline-extension.md` (do not delete — preserve the audit trail).
- [ ] T026 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` (Feature 114 US5 section): add `GET`/`DELETE /api/v1/wallet/presentations`, the `ICitizenPresentationStore`, and the merge rule; note the offline consumer model was dropped (no register write).
- [ ] T027 [P] Update `docs/reference/API-DOCUMENTATION.md` with the two new endpoints.
- [ ] T028 Run `specs/134-presentation-history/quickstart.md` end-to-end manual validation (SC-001 … SC-006), including confirming **no register transaction** is produced (SC-004).
- [ ] T029 Run the full affected suites green and check coverage on new code (>85%): `tests/Sorcha.Wallet.Service.Tests` + `tests/Sorcha.Wallet.Pwa.Tests`; verify the ~pre-existing Docker-dependent integration failures are unchanged by stash-baseline comparison.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)**: no dependencies.
- **Foundational (P2)**: depends on Setup. **Blocks all user stories** (they all use the store).
- **User stories (P3–P5)**: all depend on Foundational. US1 is the MVP. US2 is independent of US1 (different endpoint/client method, distinct test methods). US3 refines the merge US1 introduces, so US3 follows US1 on `Activity.razor`.
- **Polish (P6)**: after the desired stories.

### Within / across stories — file-sharing notes

- `CitizenWalletEndpoints.cs` is touched by T014 (US1 GET) and T018 (US2 DELETE) — sequential edits to the same file, not parallel.
- `ICitizenWalletClient` / `CitizenWalletClient` touched by T015 (US1) and T019 (US2) — sequential.
- `Activity.razor` touched by T016 (US1), T020 (US2), T023 (US3) — sequential; do US1 → US2 → US3 on this file.
- `PresentationHistoryEndpointTests.cs` shared by T012 (US1) and T017 (US2) — different test methods, sequential file edits.
- `ActivityMergeTests.cs` shared by T013/T021/T022 — different test methods.

### Parallel opportunities

- **Foundational**: T002, T005, T007 are `[P]` (distinct new files); T010, T011 `[P]` (distinct test files) once their SUTs exist. T003/T004/T006/T008/T009 are sequential (shared files / migration ordering).
- **Across stories** (if staffed): once Foundational completes, the *server* halves of US1 (T014) and US2 (T018) could be split between two devs, but they edit the same endpoints file — coordinate or sequence.
- **Polish**: T024–T027 are all `[P]` (distinct docs).

---

## Parallel Example: Foundational

```bash
# Distinct new files — safe to author together:
Task: T002 Create CitizenPresentationRecord entity
Task: T005 Define ICitizenPresentationStore interface
Task: T007 Implement InMemoryCitizenPresentationStore
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 Setup → 2. Phase 2 Foundational (durable store + forwarder swap + migration) → 3. Phase 3 US1 (read endpoint + client + PWA fetch) → **STOP & validate SC-001 cross-device read** → demo.

### Incremental delivery

- Foundational → store fills durably (no user-visible change yet).
- + US1 → cross-device history visible (MVP).
- + US2 → delete-everywhere.
- + US3 → instant + exactly-once merge hardening.
- + Polish → docs/skill/contract supersession, quickstart validation, full-suite green.

---

## Notes

- `[P]` = different files, no incomplete dependencies.
- The Redis SET-NX dedupe and `/presentations/log` endpoint from PR2 are **unchanged**; only the forwarder implementation is swapped (T009).
- Verify new tests fail before implementing where practical (TDD-encouraged per constitution).
- Commit after each task or logical group; keep the PR focused on feature 134.
- **No Blueprint Service changes and no register write** anywhere in this task list — that is the central reconciliation invariant (FR-010 / SC-004).
