---

description: "Tasks for Feature 114 US4 (Receive a newly-issued credential automatically)"
---

# Tasks: Feature 114 US4 — Receive a newly-issued credential automatically

**Input**: Design documents from `/specs/114-citizen-wallet-pwa/`
**Plan**: `specs/114-citizen-wallet-pwa/us4-plan.md`
**Spec**: `specs/114-citizen-wallet-pwa/spec.md` § US4

**Tests**: Requested by the plan (§ 5). Tests precede implementation per TDD.

**Organization**: Single user story — US4 — with foundational scaffolding extracted into Phase 2 because the EF migrations block every implementation task. Setup is empty (the project is mature; nothing new to scaffold at the solution level).

**Numbering note**: Task IDs in this file (T001–T020) are local to US4. The master `specs/114-citizen-wallet-pwa/tasks.md` retains the original placeholder slots T123–T131 for US4; this file supersedes them. After landing, mark the placeholder slots in `tasks.md` as "see us4-tasks.md".

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US4 for all implementation tasks; foundational/polish tasks have no story label
- File paths are absolute repo-relative

## Path Conventions

- Server code: `src/Services/Sorcha.Wallet.Service/`, `src/Core/Sorcha.Wallet.Core/`, `src/Core/Sorcha.Wallet.Portable/`, `src/Common/Sorcha.ServiceDefaults/`
- PWA code: `src/Apps/Sorcha.Citizen.Wallet/`
- Tests: `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/`, `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialisation and shared scaffolding.

*No tasks.* The Sorcha solution, the Citizen Wallet PWA project, the Wallet Service, the SignalR backplane, and the test infrastructure all exist. US4 is a focused addition; nothing at the solution level needs creating.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: EF schema additions and the holder-address index that every US4 implementation task depends on.

**⚠️ CRITICAL**: All US4 tasks below depend on Phase 2 completion.

- [ ] T001 Add `CitizenHolderIndex` EF entity in `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CitizenHolderIndex.cs` — columns `WalletAddress` (PK string), `PlatformUserId` (Guid indexed), `CreatedAt` (DateTimeOffset). Configure in `WalletDbContext` (Postgres only — `TestCitizenWalletDbContext` ignores).
- [ ] T002 [P] Add `CitizenCredentialEventLog` EF entity in `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CitizenCredentialEventLog.cs` — columns `Id` (Guid PK), `PlatformUserId` (Guid), `Seq` (long), `Kind` (int), `CredentialId` (string), `CreatedAt` (DateTimeOffset). Composite index `(PlatformUserId, Seq)`. Configure in `WalletDbContext`.
- [ ] T003 Generate EF migration `AddCitizenInboxProjection` covering both entities at `src/Core/Sorcha.Wallet.Core/Data/Migrations/{date}_AddCitizenInboxProjection.cs`. Verify `dotnet ef migrations add` runs cleanly (per MEMORY: needs `$env:ConnectionStrings__Sorcha__Postgres` set; do not pass `--no-build`).
- [ ] T004 Register `ICitizenCredentialEventStream` on the audited storage interface list in `src/Common/Sorcha.ServiceDefaults/Storage/AuditedStorageInterfaces.cs` so Production / Staging fail-fast applies.

**Checkpoint**: Phase 2 complete — DB schema is in place and the audit catches any in-memory regression. US4 implementation can begin.

---

## Phase 3: User Story 4 — Receive a newly-issued credential automatically (Priority: P3)

**Goal**: When a blueprint action with `targetAudience: "SorchaLocalWallet"` issues a credential whose recipient is a citizen's holder wallet, the citizen's PWA learns within seconds via `WalletHub.CredentialAvailable`, syncs the credential delta, and renders the new credential card on Home.

**Independent Test**: Run the worked-example blueprint from `us4-plan.md` § 4 — a verifier issues an `AssuredIdentityCredential` to a late-bound citizen applicant. With the citizen PWA open on Home, the credential card appears within 5 seconds without manual refresh. Closing the PWA before issuance and reopening after still surfaces the credential through the standard `/sync` pull (push is optimisation, not authority).

### Tests for User Story 4 (TDD — write first, ensure they FAIL)

- [ ] T005 [P] [US4] Unit test `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/HolderAddressLookupTests.cs` — covers hit (citizen address resolves to PlatformUserId), miss (org-credential address returns null), idempotent enrolment write, Redis cache hit/miss behaviour.
- [ ] T006 [P] [US4] Unit test `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenInboxProjectorTests.cs` — `OnCredentialAddedAsync` for citizen recipient (writes event log row, emits `CredentialAvailable` to correct group), org recipient (no-op), `OnCredentialStatusChangedAsync` for `Revoked` / `Declined` transitions.
- [ ] T007 [P] [US4] Unit test `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/EfCoreCitizenCredentialEventStreamTests.cs` — `ReadAsync(afterSeq)` ordering, joined `CredentialEntity` payload mapping, `Active`/`PendingAcceptance` → `Added`, `Revoked`/`Declined` → `Revoked`, `GetHighestSeqAsync` correctness. Use `TestCitizenWalletDbContext`.
- [ ] T008 [US4] Integration test `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenInboxProjectionIntegrationTests.cs` — `WebApplicationFactory` drives `InboundCredentialDetector` → `CredentialStore.AddAsync` → projector → in-memory `IHubContext<WalletHub>` test double captures `CredentialAvailable(credentialId)` on the citizen's group. Asserts a `CitizenCredentialEventLog` row was inserted with monotonic `Seq`.
- [ ] T009 [US4] Playwright E2E fixture `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/CitizenWalletPushTests.cs` — verifier UI submits the worked-example blueprint, citizen PWA at `/wallet/` receives the credential within 5 s. Requires new `AuthenticatedCitizenWalletTestBase` (T015) and `CitizenWalletPage` page object.

### Implementation for User Story 4

- [ ] T010 [P] [US4] `IHolderAddressLookup` interface in `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IHolderAddressLookup.cs` — single method `Task<Guid?> ResolvePlatformUserIdAsync(string walletAddress, CancellationToken ct = default)`.
- [ ] T011 [US4] `EfCoreHolderAddressLookup` implementation in `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreHolderAddressLookup.cs` — reads `CitizenHolderIndex`, Redis cache (24 h TTL, key `sorcha:citizen:holder-index:{addr}`), null on miss. Register as scoped in `Sorcha.Wallet.Service.Program.cs`.
- [ ] T012 [US4] Modify `src/Services/Sorcha.Wallet.Service/Services/Implementation/HolderKeyService.cs` `GetOrCreateAsync` to upsert the `(WalletAddress, PlatformUserId)` row in `CitizenHolderIndex` after first derivation. Idempotent on `WalletAddress` PK conflict.
- [ ] T013 [P] [US4] `ICitizenInboxProjector` interface in `src/Services/Sorcha.Wallet.Service/Services/Interfaces/ICitizenInboxProjector.cs` — methods `OnCredentialAddedAsync(CredentialEntity, CancellationToken)` and `OnCredentialStatusChangedAsync(CredentialEntity, CredentialStatus oldStatus, CancellationToken)`.
- [ ] T014 [US4] `CitizenInboxProjector` implementation in `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenInboxProjector.cs` — resolve via `IHolderAddressLookup`; on hit, allocate next `Seq` (under `SERIALIZABLE` transaction with row lock on a sentinel), insert `CitizenCredentialEventLog`, emit `_walletHub.Clients.Group(WalletHub.GroupNameFor(pid)).CredentialAvailable(credentialId)`. Resilient to hub disconnects (log and continue).
- [ ] T015 [US4] Hook the projector into `src/Services/Sorcha.Wallet.Service/Services/Implementation/InboundCredentialDetector.cs` immediately after `CredentialStore.AddAsync(...)` (line ~205 per current state). Also hook `src/Services/Sorcha.Wallet.Service/Credentials/CredentialStore.cs` `PatchStatusAsync` to call `OnCredentialStatusChangedAsync` after status mutation succeeds.
- [ ] T016 [US4] `EfCoreCitizenCredentialEventStream` implementation in `src/Services/Sorcha.Wallet.Service/Services/Implementation/EfCoreCitizenCredentialEventStream.cs` — implements `ICitizenCredentialEventStream` (existing interface in `Services/Interfaces/ICitizenSyncService.cs`); reads `CitizenCredentialEventLog` joined to `CredentialEntity` for payload composition. Maps statuses per the kind table in plan § 2.3.
- [ ] T017 [US4] DI rewire in `src/Services/Sorcha.Wallet.Service/Program.cs` — replace `EmptyCitizenCredentialEventStream` registration (lines 159-160) with `EfCoreCitizenCredentialEventStream`. Register `ICitizenInboxProjector` as scoped.
- [ ] T018 [P] [US4] `CitizenWalletHubConnection` in `src/Apps/Sorcha.Citizen.Wallet/Services/CitizenWalletHubConnection.cs` — modelled on `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/WalletHubConnection.cs`, scoped to `DeviceRevoked(Guid)` and `CredentialAvailable(string)` only. Auth via `IAuthService` (citizen audience). Reconnect-with-jitter (0/2/5/10/30 s). Hub URL `{gateway}/hubs/wallet`. Register as scoped in `src/Apps/Sorcha.Citizen.Wallet/Extensions/ServiceCollectionExtensions.cs`.
- [ ] T019 [US4] Wire `OnCredentialAvailable` subscription in `src/Apps/Sorcha.Citizen.Wallet/Pages/Index.razor` — on event, call `_syncService.SyncAsync()` then `InvokeAsync(StateHasChanged)`. Start the connection from `Program.cs` after the auth service is ready.
- [ ] T020 [US4] Service-worker sync handler in `src/Apps/Sorcha.Citizen.Wallet/wwwroot/service-worker.published.js` — register a `sync` event listener for tag `citizen-credential-sync`, replay the `/sync` call when the event fires. The hub-connection client calls `registration.sync.register('citizen-credential-sync')` on `CredentialAvailable` when document is hidden.

### E2E test infrastructure (used by T009)

- [ ] T021 [P] [US4] `AuthenticatedCitizenWalletTestBase` in `tests/Sorcha.UI.E2E.Tests/Infrastructure/AuthenticatedCitizenWalletTestBase.cs` — copies `AuthenticatedDockerTestBase`, swaps target URL to `{gateway}/wallet/`, swaps auth scope to citizen audience (`sorcha:citizen-wallet`).
- [ ] T022 [P] [US4] `CitizenWalletPage` page object in `tests/Sorcha.UI.E2E.Tests/PageObjects/CitizenWalletPage.cs` — locators for the credentials list (`data-testid="credential-card-{id}"`), Home empty state, sync indicator. `WaitForCredentialAsync(string credentialId, TimeSpan timeout)` helper.

**Checkpoint**: User Story 4 fully functional. The worked-example blueprint issues a credential to a citizen and the PWA renders it within 5 seconds. Closing/reopening the PWA still surfaces the credential through `/sync`.

---

## Phase 4: Polish & Cross-Cutting Concerns

**Purpose**: Documentation propagation and validation against the live stack.

- [ ] T023 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` § "Citizen Wallet PWA (Feature 114)" with the new endpoints, projector, holder index, event log, and the SorchaLocalWallet citizen-PWA worked example.
- [ ] T024 [P] Update `.claude/skills/verifiable-credentials/SKILL.md` with the citizen-PWA delivery flow (alongside the existing org-credential pattern).
- [ ] T025 [P] Update `.claude/skills/blueprint-builder/SKILL.md` with the worked example showing `targetAudience: "SorchaLocalWallet"` + late-bound citizen applicant.
- [ ] T026 Update `MEMORY.md` § "Feature 114" to mark US4 as shipped, list the new server-side surface, and remove the "deferred — replace `EmptyCitizenCredentialEventStream`" note.
- [ ] T027 Manual quickstart validation per `specs/114-citizen-wallet-pwa/quickstart.md` — clean Docker, citizen enrolment, run the worked-example blueprint, verify push + sync + render path end-to-end.
- [ ] T028 Update `specs/114-citizen-wallet-pwa/tasks.md` US4 section to point to this `us4-tasks.md` file as the authoritative task list.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Empty.
- **Foundational (Phase 2)**: T001/T002/T003/T004 — must complete before any US4 task. T001 and T002 are parallel; T003 depends on both; T004 is parallel with all three.
- **User Story 4 (Phase 3)**: All tasks depend on Phase 2.
- **Polish (Phase 4)**: Depends on Phase 3 completion.

### Within US4

- **Tests first** (T005–T009): Write and ensure they FAIL before any T010+ task lands.
- **Lookup before projector** (T010, T011, T012 → T013, T014).
- **Projector before hook** (T014 → T015).
- **Stream before DI rewire** (T016 → T017).
- **Server before PWA** (T011, T014 → T018, T019, T020) — strictly sequential at the top level so the hub emit exists when the PWA subscribes; in practice the PWA tasks (T018, T021, T022) can be drafted in parallel against a stub server.
- **E2E last** (T009 needs T015 + T017 + T019 + T020 + T021 + T022 all in place).

### Parallel Opportunities

- T001 ∥ T002 ∥ T004 (different files, no dependency between them).
- T005 ∥ T006 ∥ T007 (separate test files).
- T010 ∥ T013 ∥ T018 ∥ T021 ∥ T022 (different files, no incomplete dependencies once Phase 2 is done).
- T023 ∥ T024 ∥ T025 (skill files, independent).

---

## Parallel Example: User Story 4

```bash
# Phase 2 foundation (after T001 and T002 land):
Task: "T003 Generate EF migration AddCitizenInboxProjection"
Task: "T004 Register ICitizenCredentialEventStream on the audited list"

# Tests for User Story 4 (TDD — together, must fail):
Task: "T005 HolderAddressLookupTests in tests/Sorcha.Wallet.Service.Tests/CitizenWallet/"
Task: "T006 CitizenInboxProjectorTests in tests/Sorcha.Wallet.Service.Tests/CitizenWallet/"
Task: "T007 EfCoreCitizenCredentialEventStreamTests in tests/Sorcha.Wallet.Service.Tests/CitizenWallet/"

# US4 server skeleton (after Phase 2):
Task: "T010 IHolderAddressLookup interface"
Task: "T013 ICitizenInboxProjector interface"
Task: "T018 CitizenWalletHubConnection in Sorcha.Citizen.Wallet/Services/"
Task: "T021 AuthenticatedCitizenWalletTestBase in tests/Sorcha.UI.E2E.Tests/Infrastructure/"
Task: "T022 CitizenWalletPage page object in tests/Sorcha.UI.E2E.Tests/PageObjects/"
```

---

## Implementation Strategy

### MVP First (US4 only — this whole document)

1. Phase 2 (T001–T004) — DB schema and audit registration.
2. Phase 3 tests (T005–T009) — write failing tests.
3. Phase 3 server (T010–T017) — make unit/integration tests pass.
4. Phase 3 PWA (T018–T020) — wire the client.
5. Phase 3 E2E infra + run (T021, T022, then re-run T009) — end-to-end green.
6. Phase 4 polish — docs and quickstart.

### Stop-the-line checkpoints

- After **T008** passing: server-side projection is correct end-to-end without UI.
- After **T017**: `EmptyCitizenCredentialEventStream` retired; `/sync` now returns real deltas. Worth a manual smoke check via `curl`.
- After **T020**: PWA renders push-driven credentials. Manual demo runnable.
- After **T009** passing: E2E green. Ready to PR.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks.
- All US4 tasks carry the [US4] story label per the speckit format.
- Verify each test in T005–T009 fails before its corresponding implementation lands (TDD discipline).
- Commit after each task or logical group; one PR for the foundational schema, one PR for the server projection, one PR for the PWA wiring + E2E is the suggested split.
- Stop at any checkpoint above to validate; the hub push is genuinely an optimisation, so the system remains correct after each milestone even before the E2E binds the loop.
- Avoid: writing the projector before the entities (T001/T002) exist; landing the DI rewire (T017) before `EfCoreCitizenCredentialEventStream` is tested (T007); shipping the PWA hub client (T018) before the server emits anything (T015).
