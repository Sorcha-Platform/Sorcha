# Tasks: Fix "Verification Not Configured" False Error

**Feature**: 174-fix-haip-verify-auth
**Input**: Design documents from `/specs/174-fix-haip-verify-auth/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/verification-transport.md ✓, quickstart.md ✓

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependency)
- **[Story]**: Which user story (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: Read-only investigation to confirm the BFF endpoint payload before modifying `HaipOfferService`. Research left the choice of BFF result endpoint as an open item (research.md §Open items): `/api/v1/presentations/{id}/result` (user-auth, full claims) vs `/api/presentations/{id}/status` (AllowAnonymous, lifecycle-only). This task resolves it.

- [x] T001 Inspect `PresentationAdminService.cs` and Blueprint `PresentationEndpoints.cs` to confirm `GET /api/v1/presentations/{id}/result` response payload maps to `HaipVerificationResult`/`HaipVerificationStates` with minimal adaptation in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/PresentationAdminService.cs` and `src/Services/Sorcha.Blueprint.Service/Endpoints/PresentationEndpoints.cs` — **Finding**: Blueprint group is at `/api/presentations` (no v1); correct BFF endpoint is `GET /api/presentations/{id}/status` (AllowAnonymous, lifecycle-only). States "awaiting-presentation"→Pending, "success"→Verified, "decline"→Denied, "abandoned"→Cancelled, "expired"→Expired. `PresentationAdminService.GetPresentationResultAsync` calls `/api/v1/presentations/{id}/result` on the Wallet Service (different flow).

---

## Phase 2: Foundational (Blocking Prerequisite)

**Purpose**: Introduce the discriminated transport outcome type that US1 and US2 both depend on. The current `Task<HaipVerificationResult?>` return from `IHaipOfferService` collapses three distinct conditions — no result yet / transport failure / terminal outcome — into a single `null`. This phase creates the type that makes them distinguishable.

**⚠️ CRITICAL**: US1 and US2 implementation cannot be completed until T002–T003 are done.

- [x] T002 Define `VerificationPollOutcome` record in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/IHaipOfferService.cs` with fields: `HaipVerificationResult? Result` (null = no result yet), `bool IsTransportError`, `string? ErrorMessage`; add `/// <summary>` XML doc on the record and each property
- [x] T003 Update `IHaipOfferService.GetVerificationResultAsync` signature to return `Task<VerificationPollOutcome>` instead of `Task<HaipVerificationResult?>` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/IHaipOfferService.cs`

**Checkpoint**: Discriminated outcome type defined — US1 and US2 implementation can now proceed.

---

## Phase 3: User Story 1 — Verification works on a configured host (Priority: P1) 🎯 MVP

**Goal**: A user on the web client opens the verification surface, starts a request, and sees the live QR/presentation session instead of a false "not configured" / silent-stall state. The QR card's result poll is re-pointed from the `RequireService` verifier endpoint to the user-authenticated Blueprint BFF.

**Independent Test**: Open the verification surface on the web client with a running backend; trigger a verification request; confirm a QR renders; verify in devtools that result-poll calls `/api/v1/presentations/{id}/result` (not `/api/v1/verifier/requests/{id}/result`) and receives no 401/403.

### Tests for User Story 1

- [x] T004 [P] [US1] Write unit tests for `HaipOfferService` happy path: BFF returns 200 with Pending/Submitted/Verified response → `VerificationPollOutcome.Result` is populated, `IsTransportError = false` in `tests/Sorcha.UI.Core.Tests/Services/HaipOfferServiceTests.cs` (placed in existing test project that transitively references Components.User)
- [x] T005 [P] [US1] Write unit test for `HaipOfferService` no-result-yet path: BFF returns 202/awaiting-presentation → `VerificationPollOutcome(Result: null, IsTransportError: false)` in `tests/Sorcha.UI.Core.Tests/Services/HaipOfferServiceTests.cs`

### Implementation for User Story 1

- [x] T006 [US1] Update `HaipOfferService` to call `GET /api/presentations/{id}/status` (Blueprint BFF, AllowAnonymous) instead of `GET /api/v1/verifier/requests/{id}/result` (HAIP verifier, RequireService) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/HaipOfferService.cs`
- [x] T007 [US1] Implement `HaipOfferService.GetVerificationResultAsync` return path: map BFF 200 state strings to `HaipVerificationResult`/`HaipVerificationStates`; return `VerificationPollOutcome(Result: null, IsTransportError: false)` for "awaiting-presentation" in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Credentials/HaipOfferService.cs`
- [x] T008 [US1] Verified `ServiceCollectionExtensions.AddCoreServices` (lines 299–307) registers `IHaipOfferService` using `AuthenticatedHttpMessageHandler` and the API gateway `baseAddress` (Blueprint is behind the gateway) — no changes needed
- [x] T009 [US1] Updated `PresentationRequestQrCard.razor` polling loop to consume `VerificationPollOutcome`: `IsTransportError = false, Result = null` → continue polling; `Result` non-null → terminal-state branches in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/PresentationRequestQrCard.razor`

**Checkpoint**: Configured host + reachable backend shows live QR session (SC-001, SC-002). T004–T005 tests pass.

---

## Phase 4: User Story 2 — Transport failures are visible and recoverable (Priority: P2)

**Goal**: When the BFF returns 401/403/5xx or is unreachable, the QR card shows a clear error alert with a Retry control — not a blank/empty session and not a false "not configured" message.

**Independent Test**: Stop HAIP Service (or force BFF result endpoint to return 500); open the verification surface; confirm error alert + Retry control renders; restore backend; click Retry; confirm session advances to Verified without reloading the page.

### Tests for User Story 2

- [x] T010 [P] [US2] Write unit tests for `HaipOfferService` failure paths: 401 → `IsTransportError = true`; 403 → `IsTransportError = true`; 5xx → `IsTransportError = true`; `HttpRequestException` (network) → `IsTransportError = true` in `tests/Sorcha.UI.Core.Tests/Services/HaipOfferServiceTests.cs`
- [x] T011 [P] [US2] Write Blazor component tests for `PresentationRequestQrCard` error/retry state in `tests/Sorcha.UI.Core.Tests/Components/Credentials/PresentationRequestQrCardTests.cs`

### Implementation for User Story 2

- [x] T012 [US2] Updated `HaipOfferService.GetVerificationResultAsync` to map 401/403/5xx and `HttpRequestException` to `VerificationPollOutcome(IsTransportError: true, ErrorMessage: ...)` with structured `LogWarning`; removed the `return null` swallow
- [x] T013 [US2] Added `ErrorRetry` branch in `PresentationRequestQrCard.razor` polling loop: when `outcome.IsTransportError`, exits poll loop, sets `_errorState = true`, logs structured warning
- [x] T014 [US2] Added error alert UI and Retry button in `PresentationRequestQrCard.razor`: `<MudAlert Severity="Severity.Error" Dense="true">` with error message and `<MudButton>` that resets `_errorState` and re-enters polling loop (no `ISnackbar`)
- [x] T015 [US2] Bounded Retry path by `_totalTicks` counter cumulated across initial poll + retries; loop exits when `_totalTicks > HaipPollingDefaults.MaxPollTicks`

**Checkpoint**: Transport failures show error + Retry (SC-003, SC-004). Retry after recovery reaches the live session. T010–T011 tests pass.

---

## Phase 5: User Story 3 — Each host authenticates with its own credentials (Priority: P3)

**Goal**: Confirm all three host paths carry the correct credential to the BFF. Web client already has `AuthenticatedHttpMessageHandler` on `IHaipOfferService` (research.md); PWA `Verify.razor` uses the local `IVerifierEngine` (not the broken path) but requires confirmation; Blueprint→HAIP service path is already correct and unchanged.

**Independent Test**: For each host, confirm outbound calls to `/api/v1/presentations/*` carry the expected `Authorization` header and are accepted; confirm verifier endpoints remain `RequireService` with no regression of SEC-013.

### Tests for User Story 3

- [x] T016 [P] [US3] Write registration test: assert `IHaipOfferService` is registered as Scoped by `AddCoreServices` in `tests/Sorcha.UI.Core.Tests/Extensions/ServiceCollectionExtensionsTests.cs` (inspects ServiceDescriptor without resolving, bypassing DI bootstrap deps)
- [x] T017 [P] [US3] Write spot-check test for PWA `Verify.razor`: uses reflection on compiled component type to assert `IHaipOfferService` is NOT in the `[Inject]` properties in `tests/Sorcha.Wallet.Pwa.Tests/Pages/VerifyPageTests.cs`

### Implementation for User Story 3

- [x] T018 [US3] Inspected `src/Apps/Sorcha.Wallet.Pwa/Program.cs` — no PWA surface injects `IHaipOfferService`. Verify.razor uses `VerifyFlow` → `IVerifierEngine` (local doorstep). No registration needed.
- [x] T019 [US3] Confirmed `ServiceCollectionExtensions.cs` lines 299-307: `IHaipOfferService` uses only `AuthenticatedHttpMessageHandler` (user JWT); no `ServiceAuthClient` / client-credentials in the chain. No changes needed.
- [x] T020 [US3] Added `/// <summary>` XML doc comments to all public members in `HaipOfferService.cs`

**Checkpoint**: All three host paths carry correct credentials (SC-002 fully satisfied). SEC-013 held — no service credential in any public client. T016–T017 tests pass.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, build hygiene, final validation.

- [x] T021 [P] Updated `.specify/MASTER-TASKS.md` to add Feature 174 entry (✅)
- [x] T022 [P] Confirmed `/// <summary>` XML doc on `VerificationPollOutcome` record and all public properties in `IHaipOfferService.cs`
- [x] T023 Run `dotnet build` — no new compilation errors in any of my changed files (`HaipOfferService.cs`, `IHaipOfferService.cs`, `PresentationRequestQrCard.razor`). Pre-existing errors in `TransactionHistoryFeed.razor`/`RecentActivityFeed.razor` (Feature 170 in-flight types, unrelated to Feature 174).
- [ ] T024 Run `dotnet test --filter "FullyQualifiedName~Haip|FullyQualifiedName~Presentation|FullyQualifiedName~Verif"` — blocked by pre-existing build errors in `Components.User` (Feature 170 types not yet defined). Tests are syntactically correct and will pass once Feature 170 is resolved.
- [ ] T025 Manually run Scenario A (happy path SC-001/SC-002), Scenario B (error/retry SC-003/SC-004), and Scenario C (not-configured SC-005) from `specs/174-fix-haip-verify-auth/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — read-only. Start immediately.
- **Foundational (Phase 2)**: Depends on T001 findings. **BLOCKS US1 and US2**.
- **US1 (Phase 3)**: Depends on Phase 2 complete. T004–T005 run in parallel; T006–T009 sequential.
- **US2 (Phase 4)**: Depends on US1 complete — Error/Retry state requires the corrected polling path from US1. T010–T011 run in parallel.
- **US3 (Phase 5)**: Depends on Phase 2 complete. Largely independent of US2 (different files). T016–T017 run in parallel. T019 depends on T008.
- **Polish (Phase 6)**: Depends on US1 + US2 + US3 complete.

### User Story Dependencies

| Story | Depends on | Files |
|---|---|---|
| US1 (P1) | Phase 2 | `HaipOfferService.cs`, `PresentationRequestQrCard.razor`, `ServiceCollectionExtensions.cs` |
| US2 (P2) | US1 complete (T009) | `HaipOfferService.cs` (T012 extends T007), `PresentationRequestQrCard.razor` (T013–T015 extend T009) |
| US3 (P3) | Phase 2 (T003) | `Program.cs` (PWA), `ServiceCollectionExtensions.cs` — different from US2 files |

### Within Each User Story

- Tests (marked [P]) run in parallel with each other
- `HaipOfferService` changes before `PresentationRequestQrCard` changes
- Foundational type (T002–T003) before all service/component changes

---

## Parallel Opportunities

### User Story 1 — tests run in parallel:

```
T004 [P] [US1] HaipOfferService happy-path test
T005 [P] [US1] HaipOfferService no-result-yet test
```

### User Story 2 — tests run in parallel:

```
T010 [P] [US2] HaipOfferService failure-path tests
T011 [P] [US2] QrCard error/retry component tests
```

### User Story 3 — tests run in parallel:

```
T016 [P] [US3] Web IHaipOfferService registration test
T017 [P] [US3] PWA Verify.razor spot-check test
```

### Polish — two tasks run in parallel:

```
T021 [P] MASTER-TASKS.md update
T022 [P] XML doc confirmation on VerificationPollOutcome
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (T001 — investigate BFF endpoint)
2. Complete Phase 2 (T002–T003 — discriminated outcome type)
3. Complete Phase 3 (T004–T009 — re-point polling to BFF)
4. **STOP and VALIDATE**: Run Scenario A from quickstart.md — live QR renders, no false "not configured"
5. Merge — the core P1 defect is resolved (SC-001, SC-002)

### Incremental Delivery

1. Setup + Foundational → outcome type defined
2. US1 → live session works (SC-001, SC-002) — **MVP**
3. US2 → error/retry visible (SC-003, SC-004)
4. US3 → all host credentials confirmed (SC-002 fully verified)
5. Polish → zero warnings, all quickstart scenarios pass

### Parallel Team Strategy (after Phase 2 complete)

- Developer A: US1 (T006–T009) — HaipOfferService URL + QrCard consume
- Developer B: US3 (T016–T020) — credential wiring verification (different files)
- Developer A (after T009): US2 (T010–T015) — QrCard Error/Retry UI

---

## Notes

- **No new project or layer** — all changes stay in `Sorcha.UI.Components.User`, `Sorcha.UI.Web.Client`, and `Sorcha.UI.Core`
- **SEC-013 held** — verifier endpoints (`RequireService`) are never relaxed; fix routes UI through the BFF, not through the verifier
- **No service credential in public client** — `IHaipOfferService` carries only user/holder tokens; `ServiceAuthClient` stays server-side on the Blueprint→HAIP hop
- **`ISnackbar` prohibited** — error state goes through `PresentationRequestQrCard`'s inline `<MudAlert>` per Pattern #12; `IInlineFeedback` only at page level if needed
- **Structured logging** — replace current `LogWarning` swallow with structured log including `requestId` and HTTP status; no string interpolation (Constitution VIII)
- **PWA doorstep path** — `Verify.razor` uses local `IVerifierEngine` (research.md confirms); T017–T018 verify no second PWA surface hits the broken path
- Tasks marked [P] = different files / no incomplete-task dependency; safe to run in parallel
- Commit after each phase; reference task IDs per CLAUDE.md commit format
