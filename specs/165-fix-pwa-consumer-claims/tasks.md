# Tasks: Fix PWA Consumer-Token Claims (Feature 165)

**Input**: Design documents from `/specs/165-fix-pwa-consumer-claims/`

**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Centralise the `platform_user_id` claim name and confirm the identity-registry seam is injectable in the Wallet Service.

- [X] T001 Add `PlatformUserId = "platform_user_id"` constant to `src/Common/Sorcha.ServiceClients.Http/Auth/TokenClaimConstants.cs` and replace the bare string literals at `TokenService.cs:110`, `CitizenWalletEndpoints.cs:590`, and `PlatformUserDeviceEndpoints.cs:220` with the constant

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core behavioural fix — hardens the Wallet Service identity-resolution seam. All three user stories depend on this being in place and green before any surface can be verified.

**⚠️ CRITICAL**: No user-story verification can begin until this phase is complete and tests are green.

- [X] T002 Read `PlatformUserDeviceEndpoints.ResolvePlatformUserIdAsync` (`src/Services/Sorcha.Tenant.Service/Endpoints/PlatformUserDeviceEndpoints.cs:215-234`) to understand the exact registry-lookup shape to mirror
- [X] T003 Inject the citizen identity repository (or equivalent `IUserIdentityRepository` / `ITenantServiceClient`) into `CitizenWalletEndpoints` in `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` so the fallback lookup can be made
- [X] T004 Rewrite `ResolveCitizenContext` (`CitizenWalletEndpoints.cs:587-599`) to implement the three-step precedence defined in `contracts/citizen-identity-resolution.md`: (1) read `platform_user_id` claim → parse GUID → use; (2) else read `sub` → query identity registry → use `UserIdentity.PlatformUserId`; (3) else return empty/guidance state (not 401) for an identifiable but wallet-less citizen
- [X] T005 Emit a structured-log breadcrumb on the precedence-2 (legacy-token recovery) branch in `CitizenWalletEndpoints.cs`, mirroring the pattern at `TokenService.cs:313-324` (observability O-1)
- [X] T006 [P] Write unit tests for `ResolveCitizenContext` in `tests/Sorcha.Wallet.Service.Tests/` covering: (a) token with valid `platform_user_id` — resolves unchanged; (b) legacy token, no claim, valid `sub` → registry lookup → correct `PlatformUserId` used; (c) neither resolvable → empty/guidance, not 500; (d) citizen with no wallet → empty/guidance, not 401; (e) platform-audience token → rejected by audience guard (contracts M-1…M-4, MN-1, MN-2)
- [X] T007 [P] Write minting-coverage regression tests in `tests/Sorcha.Tenant.Service.Tests/` asserting INV-1 (every consumer path carries `platform_user_id`) and INV-2 (no `role` / `wallet_address`) for every issuance path in the test matrix from `contracts/consumer-token-claims.md`: password login, org-selection, 2FA Razor, 2FA API, social callback, passkey assertion, org switch, refresh (claim present), and refresh (legacy — claim absent, recovered from `UserIdentity.PlatformUserId`)
- [X] T008 Run `dotnet build` and `dotnet test` for both `Sorcha.Wallet.Service.Tests` and `Sorcha.Tenant.Service.Tests` and confirm all regression tests are green

**Checkpoint**: Foundation green — identity resolution is hardened, regression suite passes, all three surfaces unblocked.

---

## Phase 3: User Story 1 — Security page loads for a signed-in citizen (Priority: P1) 🎯 MVP

**Goal**: A signed-in citizen opens the Security page on the PWA and sees their account-security state instead of a blank/error.

**Independent Test**: Sign in as a citizen on `n1`, navigate to Security, confirm the page loads the citizen's security state with no unauthorized error on the first attempt.

### Implementation for User Story 1

- [X] T009 [US1] Confirm that `Security.razor` (`src/Apps/Sorcha.UI/Sorcha.Wallet.Pwa/Pages/Security.razor`) and the shared `SecurityHome` component invoke a citizen-identity-scoped backend call and that the call goes through the hardened `ResolveCitizenContext` (or the already-correct `ResolveCitizenContextAsync`). No page rewrite — assert-and-verify only.
- [ ] T010 [US1] Deploy the current build to `n1` (record the image tag `2.<run>.<attempt>` per CLAUDE.md §14)
- [ ] T011 [US1] Perform the B1 interactive verification from `quickstart.md`: sign in as a citizen, open Security, confirm it loads (SC-001). Record the backend call that `SecurityHome` issues to close the open research item in `research.md` Finding 3.

**Checkpoint**: SC-001 met — Security loads for a real citizen on `n1`. Relaunch backlog item 3 closed.

---

## Phase 4: User Story 2 — Devices page loads and device management works (Priority: P1)

**Goal**: A signed-in citizen opens the Devices page and sees their device list (empty is success); relabelling and revoking a device succeed.

**Independent Test**: Sign in as a citizen on `n1`, open Devices, confirm the citizen's own device list returns without an unauthorized error. If ≥1 device, relabel or revoke it and confirm the action persists.

### Implementation for User Story 2

- [X] T012 [US2] Verify `GET /api/v1/wallet/devices` (`CitizenWalletEndpoints.cs:266`), `PUT /api/v1/wallet/devices/{id}/label` (`:300`), and `DELETE /api/v1/wallet/devices/{id}` (`:319`) all go through the hardened `ResolveCitizenContext` — confirm no separate citizen-context call exists that still uses the old logic
- [ ] T013 [US2] Perform the B2 interactive verification from `quickstart.md` on `n1`: open Devices, confirm citizen-scoped list loads; if a device is present, relabel or revoke it and verify it persists on reload (SC-002)
- [ ] T014 [US2] Perform the B4 renewal regression check from `quickstart.md`: keep the session alive past a token renewal (or force a refresh), re-open Devices, confirm it still loads (SC-004)

**Checkpoint**: SC-002 and SC-004 met — Devices loads and works across token renewal on `n1`. Relaunch backlog item 4 closed.

---

## Phase 5: User Story 3 — Add a phone (device pairing) flow loads (Priority: P2)

**Goal**: A signed-in citizen starts the Add-a-phone flow and obtains a pairing artefact bound to their wallet. Completing pairing on a second device shows the new device in the list.

**Independent Test**: Sign in as a citizen on `n1`, start Add a phone, confirm the flow loads and issues a pairing artefact (not a load failure). Optionally complete pairing and confirm the device appears in the list.

### Implementation for User Story 3

- [X] T015 [US3] Verify `POST /api/v1/wallet/devices/enrol` (`CitizenWalletEndpoints.cs:495`) uses `ResolveCitizenContextAsync` (already has wallet-by-owner fallback per plan.md) and that the hardened platform-user-id resolution is used for device registration (not the raw `sub`)
- [ ] T016 [US3] Perform the B3 interactive verification from `quickstart.md` on `n1`: start Add a phone from `Enrol.razor`, confirm the flow loads and produces a pairing artefact (SC-003)
- [ ] T017 [US3] Complete the pairing on a second device, return to Devices, and confirm the newly paired device appears (AC-2 from User Story 3)

**Checkpoint**: SC-003 met — Add-a-phone flow loads on `n1`. Relaunch backlog item 5 closed.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Tier-boundary verification, documentation, and evidence collection.

- [ ] T018 [P] Perform the B5 tier-boundary check from `quickstart.md`: take the citizen's consumer token, call one representative platform/admin-only endpoint, confirm 401/403 (SC-005)
- [ ] T019 [P] If a pre-fix legacy token is obtainable, perform the B6 legacy-token degrade check from `quickstart.md`: confirm Devices loads (or degrades cleanly) via backend recovery, not blank/foreign (FR-007)
- [X] T020 Update XML doc comments on `ResolveCitizenContext` / any renamed helpers in `CitizenWalletEndpoints.cs` to reflect the new resolution precedence (CLAUDE.md: all public API members must have `/// <summary>`)
- [X] T021 Update `.specify/MASTER-TASKS.md` — mark relaunch backlog items 3, 4, 5 as complete; record the `n1` image tag and evidence summary per SC-006

---

## Dependencies

```
T001 (setup)
  └─▶ T002 (read reference implementation)
        └─▶ T003 (inject identity repo)
              └─▶ T004 (harden ResolveCitizenContext)
                    ├─▶ T005 (structured log)
                    ├─▶ T006 (wallet service tests) [P with T007]
                    └─▶ T007 (tenant service tests) [P with T006]
                          └─▶ T008 (build + test green)
                                ├─▶ T009 → T010 → T011 (US1 deploy + verify)
                                ├─▶ T012 → T013 → T014 (US2 verify)
                                └─▶ T015 → T016 → T017 (US3 verify)
                                        └─▶ T018, T019 [P] (polish)
                                              └─▶ T020, T021 [P] (docs)
```

## Parallel Execution

Within Phase 2 (foundational), once T004 is done:
- **T006** (wallet tests) and **T007** (tenant tests) can run in parallel — they are in separate test projects.

Within Phase 6 (polish):
- **T018** and **T019** can run in parallel.
- **T020** and **T021** can run in parallel once T018/T019 are done.

Phases 3, 4, 5 are sequential (each builds on the same `n1` deploy from T010) but within each phase the verification steps are short.

## Implementation Strategy

**MVP**: Phase 1 + Phase 2 (T001–T008). This is the smallest testable increment: the identity-resolution defect is fixed and locked by regression tests. Stories 1–3 then become deployment-and-verify steps rather than coding steps, since the page logic already exists.

**Incremental delivery**:
1. T001–T008: Code fix + tests green (CI gate)
2. T009–T011: Security loads on `n1` (relaunch item 3)
3. T012–T014: Devices loads and device management works on `n1` (relaunch item 4)
4. T015–T017: Add-a-phone flow loads on `n1` (relaunch item 5)
5. T018–T021: Tier-boundary check, legacy-token check, docs, MASTER-TASKS update
