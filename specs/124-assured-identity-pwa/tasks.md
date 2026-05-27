---
description: "Task list for Feature 124 — AssuredIdentity on the PWA"
---

# Tasks: AssuredIdentity on the PWA

**Input**: Design documents from `/specs/124-assured-identity-pwa/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md (all present)

**Tests**: Tests ARE included. Sorcha's constitution requires >85% coverage on new code (Principle IV) and the spec defines measurable success criteria (SC-002, SC-003, SC-004) that require automated verification. Each user story phase includes unit tests; end-to-end Playwright coverage is added in the polish phase to verify the SC-001…SC-008 outcomes.

**Organization**: Tasks are grouped by user story. Each story's checkpoint is a fully working, independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps to user stories from spec.md (US1–US6)
- All file paths are absolute or repo-rooted

## Path Conventions

Sorcha multi-project layout. Paths in this document are repo-rooted:
- PWA: `src/Apps/Sorcha.Citizen.Wallet/`
- Wallet Service: `src/Services/Sorcha.Wallet.Service/`
- Wallet Service tests: `tests/Sorcha.Wallet.Service.Tests/`
- PWA tests: `tests/Sorcha.Citizen.Wallet.Tests/`
- Walkthrough: `walkthroughs/AssuredIdentity/`
- Walkthrough PS module: `walkthroughs/modules/SorchaWalkthrough/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm prerequisites for the rest of the work. Sorcha's infrastructure is already in place — this phase is small.

- [ ] T001 Verify Redis is reachable from `Sorcha.Wallet.Service` in `docker-compose.yml` (existing — confirm config; no change expected)
- [ ] T002 [P] Create test project `tests/Sorcha.Citizen.Wallet.Tests/Sorcha.Citizen.Wallet.Tests.csproj` if absent, mirroring `tests/Sorcha.Wallet.Service.Tests/` project file structure (xUnit + FluentAssertions + Moq references)
- [ ] T003 [P] Add `tests/Sorcha.Citizen.Wallet.Tests/Sorcha.Citizen.Wallet.Tests.csproj` to `Sorcha.sln` if newly created

**Checkpoint**: Build clean. Ready for foundational work.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The new endpoint group, the two new stores, and DI wiring. Every user story below depends on at least one of these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Server-side: PendingApplicationNotice

- [ ] T004 [P] Create request/response DTOs `SetPendingApplicationRequest` and `PendingApplicationEnvelope` (with nested `PendingApplicationNotice`) in `src/Services/Sorcha.Wallet.Service/Models/PendingApplicationContracts.cs` per `contracts/pending-application-notice.openapi.yaml`
- [ ] T005 [P] Create `IPendingApplicationStore` interface in `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IPendingApplicationStore.cs` with `GetAsync(Guid platformUserId, CancellationToken)`, `SetAsync(Guid platformUserId, string label, CancellationToken)`, `ClearAsync(Guid platformUserId, CancellationToken)`
- [ ] T006 Create `RedisPendingApplicationStore` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/RedisPendingApplicationStore.cs` implementing `IPendingApplicationStore` over `IDistributedCache`; key format `sorcha:wallet:pending-app:{platformUserId:N}`; 24-hour TTL (depends on T004, T005)
- [ ] T007 [P] Create `SetPendingApplicationRequestValidator` in `src/Services/Sorcha.Wallet.Service/Validators/SetPendingApplicationRequestValidator.cs` (FluentValidation, `Label` non-empty, ≤ 80 chars)
- [ ] T008 Create endpoint group `PendingApplicationEndpoints` in `src/Services/Sorcha.Wallet.Service/Endpoints/PendingApplicationEndpoints.cs` — three routes (`GET`, `PUT`, `DELETE`) under `/api/v1/wallet/pending-applications`, matching the existing `CitizenWalletEndpoints` style: `.RequireAuthorization()`, `.RequireRateLimiting(RateLimitPolicies.Strict)`, `.WithName` / `.WithSummary` / `.WithDescription` / `.Produces` per contract (depends on T004–T007)
- [ ] T009 Register `IPendingApplicationStore` → `RedisPendingApplicationStore` (Scoped) and call `app.MapPendingApplicationEndpoints()` in `src/Services/Sorcha.Wallet.Service/Program.cs` (depends on T006, T008)
- [ ] T010 [P] Add OpenTelemetry counter `sorcha_pending_application_notice_total{op}` to the existing `Sorcha.Wallet.Service` meter in `src/Services/Sorcha.Wallet.Service/Services/Implementation/RedisPendingApplicationStore.cs` (op ∈ `set` / `clear` / `read`; emitted from store methods so callers don't need to instrument)

### PWA-side: WalletFlags

- [ ] T011 [P] Create `WalletFlagsRecord` type and `IWalletFlagsStore` interface in `src/Apps/Sorcha.Citizen.Wallet/Services/IWalletFlagsStore.cs` with `GetAsync(CancellationToken)` and `SetAsync(WalletFlagsRecord, CancellationToken)` (no Clear — flags are one-way per data-model.md state machine)
- [ ] T012 Add `InMemoryWalletFlagsStore` and `IndexedDbWalletFlagsStore` implementations to the same file as T011, mirroring the `IDeviceMetaStore` pattern (IndexedDB store `device`, key `flags`)
- [ ] T013 [P] Add `IPendingApplicationClient` interface + `HttpPendingApplicationClient` implementation in `src/Apps/Sorcha.Citizen.Wallet/Services/IPendingApplicationClient.cs` — methods `GetAsync(CancellationToken)`, `SetAsync(string label, CancellationToken)`, `ClearAsync(CancellationToken)`; uses the wallet's existing `BearerTokenHandler` chain
- [ ] T014 Register `IWalletFlagsStore`, `InMemoryWalletFlagsStore` (DEBUG/test), `IndexedDbWalletFlagsStore` (release), and `IPendingApplicationClient` in `src/Apps/Sorcha.Citizen.Wallet/Program.cs` DI block (depends on T011, T012, T013)

### Server-side tests for the foundational layer

- [ ] T015 [P] Unit tests `PendingApplicationStoreTests` in `tests/Sorcha.Wallet.Service.Tests/Services/PendingApplicationStoreTests.cs` covering set/clear/read happy paths, idempotency of clear, label replacement on set, TTL expiry via a fake `IDistributedCache` (xUnit + FluentAssertions + Moq)
- [ ] T016 [P] Validator tests `SetPendingApplicationRequestValidatorTests` in `tests/Sorcha.Wallet.Service.Tests/Validators/SetPendingApplicationRequestValidatorTests.cs` covering empty/too-long/whitespace labels

### PWA-side foundational tests

- [ ] T017 [P] Unit tests `WalletFlagsStoreTests` in `tests/Sorcha.Citizen.Wallet.Tests/Services/WalletFlagsStoreTests.cs` covering the `InMemoryWalletFlagsStore` variant (Get returns null when absent, Set persists, Get returns persisted record)

**Checkpoint**: Foundation ready. The new endpoint can be hit with curl; the PWA-side stores can be instantiated in tests. No UI changes yet.

---

## Phase 3: User Story 1 — Enrol Done copy (Priority: P1)

**Goal**: A first-time citizen completing enrolment sees forward-looking copy when zero credentials loaded.

**Independent Test**: Open the wallet on a fresh device, complete enrolment, confirm the Done step reads "Enrolled. Your wallet is ready — submit your council application to receive your first credential." Existing copy still renders when credentials loaded ≥ 1.

### Implementation for User Story 1

- [ ] T018 [US1] Modify the Done step in `src/Apps/Sorcha.Citizen.Wallet/Pages/Enrol.razor` so the copy is conditional on `_result.CredentialsLoaded == 0` — zero → new forward-looking copy; non-zero → existing "Loaded N credential(s)" copy. The Open-wallet button stays as-is in both branches.

### Tests for User Story 1

- [ ] T019 [P] [US1] Add bUnit (or Playwright) test verifying both copy branches render correctly given mocked `EnrolmentResult` in `tests/Sorcha.Citizen.Wallet.Tests/Pages/EnrolDoneCopyTests.cs`

**Checkpoint**: US1 functional. A presenter can hand-verify by clearing wallet state and re-running enrolment.

---

## Phase 4: User Story 2 — Waiting state (Priority: P1)

**Goal**: The wallet Home shows the waiting message + pulsing skeleton card when a pending-application notice is set for the signed-in citizen.

**Independent Test**: Issue a `PUT /api/v1/wallet/pending-applications { "label": "Assured Identity" }` for Sarah; open Home; confirm copy renders inline with the label and the pulsing skeleton card is visible; `DELETE` the notice; confirm Home reverts to standard empty state.

### Implementation for User Story 2

- [ ] T020 [P] [US2] Create `WaitingCard.razor` component in `src/Apps/Sorcha.Citizen.Wallet/Components/WaitingCard.razor` — pulsing skeleton card, takes a `Label` parameter, renders the inline message "Your {Label} application is being reviewed. You'll see it here when it's ready." plus the skeleton
- [ ] T021 [P] [US2] Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/css/welcome-takeover.css` with the `@keyframes pulse` (1.4s ease-in-out, opacity 0.4↔0.8) for the skeleton card; add a `<link>` reference in `wwwroot/index.html`
- [ ] T022 [US2] Modify `src/Apps/Sorcha.Citizen.Wallet/Pages/Index.razor` to inject `IPendingApplicationClient`, fetch the current notice in `OnInitializedAsync`, store it in a `_pendingNotice` field, and render `<WaitingCard Label="@_pendingNotice.Label" />` in the empty-credentials branch when `_pendingNotice` is non-null (depends on T020, T021; depends on T013 for the client)
- [ ] T023 [US2] On `SyncNowAsync` completion, re-fetch the notice — if the credential list now contains a matching credential, the notice should be cleared (display-side; server-side clearing is owned by US6's walkthrough scripts). Implementation: drop the waiting state from view as soon as `_credentials.Count > 0` regardless of notice state (FR-003)

### Tests for User Story 2

- [ ] T024 [P] [US2] Component test for `WaitingCard.razor` in `tests/Sorcha.Citizen.Wallet.Tests/Components/WaitingCardTests.cs` — verifies label interpolation, presence of skeleton element, accessibility attributes
- [ ] T025 [P] [US2] Integration-style test in `tests/Sorcha.Citizen.Wallet.Tests/Pages/IndexWaitingStateTests.cs` mocking `IPendingApplicationClient` to return a notice and `ICredentialCache` to return empty — verify `<WaitingCard>` renders; mock notice null → no waiting card
- [ ] T026 [P] [US2] Endpoint handler test `PendingApplicationEndpointTests` in `tests/Sorcha.Wallet.Service.Tests/Endpoints/PendingApplicationEndpointTests.cs` covering all three routes (GET present/absent, PUT idempotent replacement, DELETE idempotent) using the reflection-based static-handler invocation pattern from `PresentationEndpointTests`

**Checkpoint**: US2 functional. With a notice set out-of-band, the waiting state renders. Setting and clearing via curl works.

---

## Phase 5: User Story 3 — Foreground takeover (Priority: P1)

**Goal**: When Sarah is on the wallet Home and the SignalR `CredentialAvailable` push arrives, the welcome takeover renders with her id-card.

**Independent Test**: Open the wallet to Home (with or without waiting state); from another window, mint a credential to Sarah's account via the existing `/verify/demo/mint` path (or the walkthrough's issuance flow); confirm the takeover overlay appears with the id-card, "Welcome to your wallet" headline, and Open button; tap Open; confirm the id-card settles into Home as her first credential.

### Implementation for User Story 3

- [ ] T027 [P] [US3] Create `WelcomeTakeover.razor` component in `src/Apps/Sorcha.Citizen.Wallet/Components/WelcomeTakeover.razor` — full-screen overlay, takes a `CachedCredential Credential` parameter, renders the id-card (reusing the `ReviewSummaryRenderer` / `IdCardLayout` cross-cutting components — umbrella invariant FR-015), the "Welcome to your wallet" headline, and an Open button that fires an `OnDismiss` callback
- [ ] T028 [P] [US3] Extend `src/Apps/Sorcha.Citizen.Wallet/wwwroot/css/welcome-takeover.css` with `@keyframes fade-in` (200ms opacity 0→1) and `@keyframes dismiss-out` (180ms opacity 1→0 with slight downward translate)
- [ ] T029 [US3] Modify `src/Apps/Sorcha.Citizen.Wallet/Pages/Index.razor` to inject `IWalletFlagsStore`; on every `SyncNowAsync` completion, evaluate the eligibility check: `_credentials.Count > 0 && WalletFlags.WelcomedAt is null`; if eligible, render `<WelcomeTakeover Credential="@_credentials[0]" OnDismiss="HandleWelcomeDismissed" />` (depends on T011, T012, T027, T028)
- [ ] T030 [US3] Implement `HandleWelcomeDismissed` in `Index.razor` — persists `WelcomedAt = DateTimeOffset.UtcNow` via `IWalletFlagsStore`, dismisses the overlay (depends on T029)
- [ ] T031 [US3] Wire eligibility check into the existing `OnHubCredentialAvailable` callback — after the internal `SyncNowAsync`, re-evaluate (R-011: belt-and-braces for race ordering)

### Tests for User Story 3

- [ ] T032 [P] [US3] Component test for `WelcomeTakeover.razor` in `tests/Sorcha.Citizen.Wallet.Tests/Components/WelcomeTakeoverTests.cs` — renders id-card, headline, button; clicking Open invokes `OnDismiss`
- [ ] T033 [P] [US3] Integration-style test in `tests/Sorcha.Citizen.Wallet.Tests/Pages/IndexForegroundTakeoverTests.cs` — mock store + cache so `_credentials.Count` transitions 0→1 while `WelcomedAt is null`; verify the takeover renders; verify dismissal persists `WelcomedAt`

**Checkpoint**: US3 functional. The headline demo moment lands when Sarah is watching.

---

## Phase 6: User Story 4 — Cold-open takeover (Priority: P2)

**Goal**: If Sarah's wallet was closed when the credential was issued, opening the wallet fires the takeover on first paint after the cold-open sync completes.

**Independent Test**: Close the wallet; from outside, issue a credential to Sarah's account; reopen the wallet; confirm the takeover fires before the standard Home is visible; subsequent re-opens never re-fire.

### Implementation for User Story 4

- [ ] T034 [US4] Confirm `OnInitializedAsync` in `src/Apps/Sorcha.Citizen.Wallet/Pages/Index.razor` invokes `SyncNowAsync(silentSuccess: true)` (it does today). After that completes, the eligibility check from T029 fires automatically — no separate code path needed. This task is verification + a comment in the source documenting the cold-open path (depends on T029)

### Tests for User Story 4

- [ ] T035 [P] [US4] Integration-style test in `tests/Sorcha.Citizen.Wallet.Tests/Pages/IndexColdOpenTakeoverTests.cs` — simulate a fresh component init with `ICredentialCache` already returning one credential and `IWalletFlagsStore.WelcomedAt is null`; verify the takeover renders during init (not on a subsequent push event)

**Checkpoint**: US4 functional. Demo presenter can verify by closing the wallet between submission and approval.

---

## Phase 7: User Story 5 — Takeover idempotence (Priority: P2)

**Goal**: After Sarah dismisses the takeover, it never fires again on this device.

**Independent Test**: Trigger and dismiss the takeover; close and reopen the wallet five times; confirm the takeover never re-appears; trigger a credential-list change (status update on the existing credential, simulated); confirm no takeover.

### Implementation for User Story 5

- [ ] T036 [US5] Verify the eligibility check in `Index.razor` (from T029) reads `WelcomedAt is null` — once `HandleWelcomeDismissed` (T030) has persisted `WelcomedAt`, all subsequent eligibility checks return false. No new code; this task documents the invariant in the source as an inline comment plus the assertion in the test below

### Tests for User Story 5

- [ ] T037 [P] [US5] Test `IndexIdempotenceTests` in `tests/Sorcha.Citizen.Wallet.Tests/Pages/IndexIdempotenceTests.cs` — pre-populate `IWalletFlagsStore` with `WelcomedAt` set; verify the takeover never renders across init, push, and sync events
- [ ] T038 [P] [US5] Test `WalletFlagsStorePersistenceTests` in `tests/Sorcha.Citizen.Wallet.Tests/Services/WalletFlagsStorePersistenceTests.cs` — exercise the `InMemoryWalletFlagsStore` (Get → Set → Get returns set value); the IndexedDB variant is verified by Playwright E2E in the polish phase

**Checkpoint**: All three P1 stories plus both P2 stories are independently demoable. The wallet UX is feature-complete.

---

## Phase 8: User Story 6 — Walkthrough operator runs the demo (Priority: P3)

**Goal**: A presenter can drive the end-to-end demo with the existing walkthrough scripts. The legacy HAIP filesystem wallet path is gone.

**Independent Test**: From a clean `docker-compose up`, run `pwsh walkthroughs/AssuredIdentity/setup.ps1` then `pwsh walkthroughs/AssuredIdentity/run-phase1-identity.ps1 -UseAgents`; observe the wallet's enrol → wait → takeover → settled-Home sequence; run `pwsh walkthroughs/AssuredIdentity/run-multi-peer.ps1` and confirm it passes.

### Walkthrough — Blueprint change

- [ ] T039 [US6] Modify `walkthroughs/AssuredIdentity/blueprints/assured-identity.json` action 3 — change `credentialIssuanceConfig.targetAudience` from `"HaipExternalWallet"` to `"SorchaLocalWallet"`; update the action `description` and `claimUI.label`/`claimUI.description` copy to remove HAIP-external references (R-004)

### Walkthrough — Module helpers

- [ ] T040 [P] [US6] Add `Set-CitizenPendingApplication` and `Clear-CitizenPendingApplication` functions to `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` (or a new `Citizen.psm1` if module is segmented) — wrappers around the new wallet endpoint, expect a session object with `WalletApiBase` and `CitizenJwt`
- [ ] T041 [P] [US6] Add `Sign-CitizenInToWallet` helper to the same module — opens the wallet host with the demonstration citizen's session cookie pre-set so `setup.ps1` can leave Sarah signed in (FR-014)

### Walkthrough — Scripts

- [ ] T042 [US6] Modify `walkthroughs/AssuredIdentity/setup.ps1` to call `Sign-CitizenInToWallet` after creating Sarah's account; remove any reference to `walletDirectory` / HAIP filesystem wallet path (depends on T041)
- [ ] T043 [US6] Modify `walkthroughs/AssuredIdentity/run-phase1-identity.ps1` — call `Set-CitizenPendingApplication -Label "Assured Identity"` immediately after Sarah submits action 1; call `Clear-CitizenPendingApplication` after action 3 completes (the issuance). Remove HAIP filesystem wallet paths (`-WalletDirectory`, `holder-key.*`, etc.) (depends on T040)
- [ ] T044 [US6] Modify `walkthroughs/AssuredIdentity/run-phase2-licence.ps1` — same Set/Clear pattern with label `"Driving Licence"`. Remove HAIP filesystem references (depends on T040)
- [ ] T045 [US6] Modify `walkthroughs/AssuredIdentity/run-agents.ps1` — remove any HAIP filesystem wallet references; preserve the existing analyst-agent timing
- [ ] T046 [US6] Update `walkthroughs/AssuredIdentity/README.md` — describe the PWA-default path; remove the `wallet/` line from the file-tree section; reference Feature 124

### Walkthrough — Cleanup

- [ ] T047 [P] [US6] Delete the directory `walkthroughs/AssuredIdentity/wallet/` (contents: `credentials/`, `holder-key.jwk.json`, `holder-key.pem`). Confirm no remaining references in any walkthrough script via grep before commit

### Walkthrough — Verification

- [ ] T048 [US6] Run `pwsh walkthroughs/AssuredIdentity/run-multi-peer.ps1` and verify it still passes; check `multi-peer-findings/*.md` for the latest result (depends on T039–T047)

**Checkpoint**: The demo is reproducible end-to-end. All six user stories independently testable.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation propagation, E2E verification, regression checks, and the final quickstart pass.

### Documentation propagation

- [ ] T049 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` Citizen Wallet PWA section — add the pending-application notice endpoint trio, the new IndexedDB `flags` key, and the welcome takeover component to the surface map
- [ ] T050 [P] Update `docs/reference/API-DOCUMENTATION.md` with the three new endpoints, linking to `specs/124-assured-identity-pwa/contracts/pending-application-notice.openapi.yaml`
- [ ] T051 [P] Add a Feature 124 entry to `.specify/MASTER-TASKS.md` (or the active task tracking surface) so cross-feature visibility is preserved

### End-to-end verification

- [ ] T052 [P] Add Playwright E2E test `tests/Sorcha.UI.Tests/CitizenWallet/FirstCredentialTakeoverE2ETests.cs` (or the existing Playwright project home) covering the foreground takeover path: open wallet → set notice via API → trigger credential availability → assert takeover renders → dismiss → assert Home shows credential
- [ ] T053 [P] Add Playwright E2E test covering the cold-open path: close wallet → trigger issuance → reopen wallet → assert takeover renders on first paint
- [ ] T054 [P] Add Playwright E2E test covering idempotence: dismiss takeover → close + reopen wallet 3 times → assert no takeover

### Regression and performance

- [ ] T055 Run `dotnet test tests/Sorcha.Wallet.Service.Tests/` and `dotnet test tests/Sorcha.Citizen.Wallet.Tests/` from a clean build; verify zero regressions (SC-005)
- [ ] T056 Measure takeover render latency from `CredentialAvailable` event to overlay visible in browser devtools timeline; confirm under 200 ms p95 (plan performance goal)
- [ ] T057 Run `pwsh walkthroughs/AssuredIdentity/setup.ps1 -Force` and time `pwsh walkthroughs/AssuredIdentity/run-phase1-identity.ps1 -UseAgents` with `Measure-Command`; verify under 60s end-to-end (SC-001)

### Final pass

- [ ] T058 Execute the full `specs/124-assured-identity-pwa/quickstart.md` runbook against the merged feature; record any deviations as follow-up issues
- [ ] T059 [P] If `code-review` skill or `coderabbit-review` is run, address findings before merge

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No prerequisites — runs first.
- **Foundational (Phase 2)**: Depends on Setup. BLOCKS all user stories. Within Phase 2:
  - T004, T005, T007, T010, T011, T013, T015, T016, T017 can run in parallel ([P])
  - T006 depends on T004, T005
  - T008 depends on T004–T007
  - T009 depends on T006, T008
  - T012 depends on T011
  - T014 depends on T011, T012, T013
- **User Stories (Phase 3–8)**: All depend on Foundational. Within each story, parallel marks are correct ([P] tasks touch different files).
- **Polish (Phase 9)**: Depends on the user stories whose behaviour it verifies (US1–US6 for E2E + regression; US6 for the quickstart runbook).

### User Story Dependencies

- **US1**: independent. Only touches `Enrol.razor` and tests.
- **US2**: independent of US1; depends on Phase 2 foundational (`IPendingApplicationClient`).
- **US3**: independent of US1; uses Phase 2's `IWalletFlagsStore`; uses Phase 4's modifications to `Index.razor` (US2 + US3 both modify `Index.razor` — sequential within that file even though the stories themselves are conceptually independent).
- **US4**: builds on US3's eligibility check (T029). Verification only.
- **US5**: builds on US3's persistence (T030). Verification only.
- **US6**: independent of US1–US5 in terms of code paths but the demo it runs requires US1–US5 implemented. Test it last.

### Within Each User Story

- Tests SHOULD be written alongside implementation (constitution V "Write tests alongside code"). The tasks list them after implementation tasks but `[P]` markers indicate they can run concurrently with later tasks within the same story.
- Components before pages — `WaitingCard.razor` / `WelcomeTakeover.razor` before `Index.razor` consumes them.
- DI registration last in each layer.

### Parallel Opportunities

- **Phase 2 parallel batch** (after Setup): T004, T005, T007, T010, T011, T013, T015, T016, T017 — nine tasks across different files.
- **Within US2**: T020 + T021 + T026 in parallel (component, CSS, server tests).
- **Within US3**: T027 + T028 + T032 in parallel (component, CSS, component tests).
- **Within US6**: T040 + T041 + T047 in parallel (module helpers + cleanup).
- **Polish**: T049 + T050 + T051 + T052 + T053 + T054 + T059 in parallel.

---

## Parallel Example: Phase 2 Foundational

```bash
# Parallel batch one — DTOs, interfaces, validators:
Task: "T004 — Create DTOs in src/Services/Sorcha.Wallet.Service/Models/PendingApplicationContracts.cs"
Task: "T005 — Create IPendingApplicationStore in src/Services/Sorcha.Wallet.Service/Services/Interfaces/IPendingApplicationStore.cs"
Task: "T007 — Create SetPendingApplicationRequestValidator in src/Services/Sorcha.Wallet.Service/Validators/SetPendingApplicationRequestValidator.cs"
Task: "T011 — Create WalletFlagsRecord + IWalletFlagsStore in src/Apps/Sorcha.Citizen.Wallet/Services/IWalletFlagsStore.cs"
Task: "T013 — Create IPendingApplicationClient in src/Apps/Sorcha.Citizen.Wallet/Services/IPendingApplicationClient.cs"

# After T004, T005 land:
Task: "T006 — RedisPendingApplicationStore"

# After T004–T007 land:
Task: "T008 — PendingApplicationEndpoints"

# Server tests can begin as soon as their targets exist:
Task: "T015 — PendingApplicationStoreTests"
Task: "T016 — SetPendingApplicationRequestValidatorTests"
```

---

## Implementation Strategy

### MVP First (US1 + US2 + US3 = P1)

The Spec 1 demo headline is the three P1 stories together:

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3 (US1): Enrol Done copy
4. Complete Phase 4 (US2): Waiting state
5. Complete Phase 5 (US3): Foreground takeover
6. **STOP and VALIDATE**: The wallet UX is demo-ready for the foreground path. A presenter can drive Sarah live, the AI analyst approves, the takeover lands. Cold-open verification deferred.
7. Demo/share if ready.

### Incremental Delivery

1. After P1 milestone: add US4 (cold-open) — adds production correctness, no UI changes
2. Add US5 (idempotence) — same code path as US3, mostly verification
3. Add US6 (walkthrough rewrite) — makes the demo reproducible from a script, deletes HAIP filesystem
4. Polish phase — propagation, E2E, regression

### Parallel Team Strategy

With multiple developers:

1. One developer drives Phase 2 (Foundational) — many [P] tasks but they need to converge before any user story can start.
2. Once Phase 2 is done:
   - Developer A: US1 + US3 (both touch PWA components, related work)
   - Developer B: US2 (waiting state — its own component path)
   - Developer C: US6 (walkthrough rewrite — completely independent of the PWA code)
3. US4 and US5 fall out naturally as verification once US3 lands; either developer picks them up.
4. Polish phase is parallelisable across the team.

---

## Notes

- This is a small feature relative to the project — 59 tasks across nine phases is well within the size where TDD or test-alongside is sensible.
- All file paths are exact. No `src/[location]/[file]` placeholders remain.
- The blueprint targetAudience change (T039) is the single load-bearing config flip uncovered during research; without it the credential never lands in the PWA and the entire demo collapses. Sequenced into US6 so it lands alongside the script changes that depend on it.
- The `welcome-takeover.css` file is shared between US2 (skeleton pulse) and US3 (fade-in / dismiss-out). T021 creates it; T028 extends it. Tasks split this way to make ownership clear, not because the file is split.
- `Index.razor` is modified across Phase 4 (US2 T022, T023), Phase 5 (US3 T029, T030, T031), and Phase 6 (US4 T034). Sequential within that file even though the stories are conceptually independent — the eligibility check, the waiting render, and the takeover overlay all coexist in one component.
