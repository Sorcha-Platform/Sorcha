# Tasks: Wire both hosts onto the shared verify control + live HAIP transport (PR B3, relaunch)

**Input**: Design documents from `specs/164-verify-host-rewire/`

**Branch**: `164-verify-host-rewire` | **Spec**: `spec.md` | **Plan**: `plan.md`

**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Tests**: Included — spec and plan explicitly reference xUnit + FluentAssertions + bUnit + Moq test projects and DI-resolution assertions as load-bearing acceptance criteria (FR-002 / SC-002).

**Critical constraint**: Branch from merged `master` (B1 #1044, B2 #1045 + #1048 present) before writing any code. The local worktree HEAD predates B1/B2; implementation begins from the merged state.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Maps to user story (US1–US4)
- Exact file paths are shown in every task

---

## Phase 1: Setup (Branch Verification + Test Project)

**Purpose**: Confirm the B1/B2 surfaces this wave depends on are present before writing any B3 code, and extend the shared test project.

- [ ] T001 Rebase/branch from merged `master`; confirm B2 seams present (`IVerificationTransport`, `NotConfiguredVerificationTransport`, `QuestionSelectionPanel`, `VerificationSessionQr`, `VerdictTrailPanel`, `DefaultPresetCatalogue`, `VerdictViewModel`, `IRegisterAnchorClient`) and B1 `vp_token`-returning poll present in `src/Services/Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs`
- [ ] T002 Create or extend `tests/Sorcha.UI.Components.User.Tests/` test project — add `<InternalsVisibleTo>` reference from `Sorcha.UI.Components.User` if not already present; confirm project builds

**Checkpoint**: B1+B2 surfaces confirmed present; test project builds.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The `IVerifierIdentityProvider` abstraction and `VerificationSession` DTO are shared seams the new transport and both host registrations depend on. No US1–US4 work can begin until these exist.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T003 Define `IVerifierIdentityProvider` abstraction in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/IVerifierIdentityProvider.cs` — single method returning the `client_id` string and optional signing key material for embedding in the create-request (WASM-safe; no server-only types); add `/// <summary>` XML docs on all public members
- [ ] T004 [P] Confirm `VerificationSession` DTO (with `SessionId`, `QrDeepLink`, `State` enum `Pending|Complete|Expired|Error`, `VpToken?`, `Delegation?`, `Error?`) is present in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Verification/` — if absent (B2 gap), create it there with WASM-safe nullable annotations and `/// <summary>` XML docs
- [ ] T005 [P] Confirm tier acceptance on a live HAIP node (FR-008): mint a consumer token and an org/desk token; call `POST /api/v1/verifier/requests` and `GET /api/v1/verifier/requests/{id}/result` with each; record observed HTTP status codes and any required allowance change in `specs/164-verify-host-rewire/quickstart.md` Validation 1 table

**Checkpoint**: `IVerifierIdentityProvider` exists; `VerificationSession` DTO confirmed; tier acceptance status codes recorded; solution builds.

---

## Phase 3: User Story 1 — Live HAIP transport replaces the stub (Priority: P1) 🎯 MVP

**Goal**: A real `HaipVerificationTransport` is registered in place of `NotConfiguredVerificationTransport` on a configured host; create→poll round trip returns `vp_token` on complete.

**Independent Test**: `dotnet test tests/Sorcha.UI.Components.User.Tests --filter "FullyQualifiedName~Transport"` and `dotnet test tests/Sorcha.Haip.Service.Tests --filter "FullyQualifiedName~Verifier"` pass; the DI resolution assertion explicitly confirms the resolved type is `HaipVerificationTransport` and not the stub.

### Tests for User Story 1

- [ ] T006 [P] [US1] Write DI-resolution assertion test in `tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipTransportDiResolutionTests.cs` — build a `ServiceCollection` with the library's DI extension then override with `HaipVerificationTransport`; assert resolved `IVerificationTransport` is `HaipVerificationTransport`, never `NotConfiguredVerificationTransport` (contract C1 / SC-002) — write first, confirm it **fails** before T008
- [ ] T007 [P] [US1] Write transport round-trip test in `tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipVerificationTransportTests.cs` — mock `HttpClient`/`IHaipVerifierClient`; assert `StartAsync` returns non-empty `SessionId` + `QrDeepLink`; `PollAsync` before holder returns `State == Pending`, `VpToken == null`; `PollAsync` after holder `direct-post` returns `State == Complete`, non-null `VpToken` (contracts C2–C4); assert `State == Expired` on TTL response (C7); assert `State == Error` on fault (C6) — write first, confirm they **fail**
- [ ] T008 [P] [US1] Write cancellation test in `tests/Sorcha.UI.Components.User.Tests/Services/Verification/HaipVerificationTransportCancellationTests.cs` — assert that a cancelled `CancellationToken` causes in-flight `PollAsync` / `StartAsync` to observe cancellation and not leak (contract C8 / FR-012 / SC-006) — write first, confirm it **fails**

### Implementation for User Story 1

- [ ] T009 [US1] Implement `HaipVerificationTransport` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/HaipVerificationTransport.cs` — constructor-inject `IHaipVerifierClient` (or typed `HttpClient`) and `IVerifierIdentityProvider`; `StartAsync` calls `POST /api/v1/verifier/requests` with the preset question + injected identity; `PollAsync` calls `GET /api/v1/verifier/requests/{id}/result` and maps the response to `VerificationSession` state (`Pending`/`Complete`/`Expired`/`Error`); returns raw `vp_token` on Complete; honours `CancellationToken` throughout; WASM-safe (no server-only types); structured logs on create/poll/fault (no string-interpolated log messages); `/// <summary>` XML docs on all public members
- [ ] T010 [US1] Add `IHaipVerifierClient` typed HTTP client registration in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Extensions/` (or confirm it is already registered by `Sorcha.ServiceClients`) — WASM-safe `HttpClient` wrapper over the HAIP verifier endpoints; confirm `POST /api/v1/verifier/requests` and `GET /api/v1/verifier/requests/{id}/result` are reachable from the shared library with the caller's token

**Checkpoint**: US1 tests green; resolved `IVerificationTransport` is `HaipVerificationTransport` on any configured host; round-trip, expiry, error, and cancellation tests pass.

---

## Phase 4: User Story 2 — Citizen Wallet PWA verify runs on the shared control (Priority: P1)

**Goal**: `/wallet/verify` in the PWA renders the shared control (question selection → QR → 4-layer verdict) with the ephemeral P-256 identity; the paste box is gone.

**Independent Test**: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~Verify"` passes; manual run confirms no paste field, QR renders, verdict completes.

### Tests for User Story 2

- [ ] T011 [P] [US2] Write DI-resolution assertion test in `tests/Sorcha.Wallet.Pwa.Tests/DI/PwaVerifyTransportDiTests.cs` — build PWA's `ServiceCollection`; assert resolved `IVerificationTransport` is `HaipVerificationTransport`; assert resolved `IVerifierIdentityProvider` is the ephemeral P-256 adapter (not a stable-org provider) — write first, confirm it **fails** before T013
- [ ] T012 [P] [US2] Write route/component-mount test in `tests/Sorcha.Wallet.Pwa.Tests/Pages/VerifyPageTests.cs` (bUnit) — render `/wallet/verify`; assert shared control components render (`QuestionSelectionPanel`, `VerificationSessionQr`); assert no free-text paste field; assert the page composes the shared control, not a legacy `VerifyFlow` — write first, confirm it **fails**

### Implementation for User Story 2

- [ ] T013 [US2] Implement `EphemeralVerifierIdentityAdapter` in `src/Apps/Sorcha.Wallet.Pwa/Services/Signing/EphemeralVerifierIdentityAdapter.cs` — wraps the existing `IEphemeralVerifierIdentityService` to implement `IVerifierIdentityProvider`; returns a fresh ephemeral P-256 `client_id` (JWK thumbprint) per session; WASM-safe
- [ ] T014 [US2] Override DI in `src/Apps/Sorcha.Wallet.Pwa/Program.cs` — after library extension call, add `services.AddScoped<IVerifierIdentityProvider, EphemeralVerifierIdentityAdapter>()` and `services.AddScoped<IVerificationTransport, HaipVerificationTransport>()`; confirm these registrations appear **after** the library defaults so they override the stub (R4)
- [ ] T015 [US2] Rewire `src/Apps/Sorcha.Wallet.Pwa/Pages/Verify.razor` — replace the paste-based legacy content with the shared `<VerificationSessionQr>` / `<QuestionSelectionPanel>` / `<VerdictTrailPanel>` composition; remove any direct reference to the legacy `VerifyFlow` component; page markup references only shared control components from `Sorcha.UI.Components.User`

**Checkpoint**: PWA DI resolution and component-mount tests green; `/wallet/verify` renders shared control with ephemeral identity; no paste box present; build passes.

---

## Phase 5: User Story 3 — Desk Verifier runs on the shared control with its stable org identity (Priority: P2)

**Goal**: `Sorcha.Verifier` renders the same shared control as the PWA, with the desk app's stable org identity in the create-request; the bespoke flow is replaced (not run in parallel).

**Independent Test**: `dotnet test tests/Sorcha.Verifier.Tests --filter "FullyQualifiedName~Verify"` passes; manual run shows shared control with stable org identity in the holder-facing request.

### Tests for User Story 3

- [ ] T016 [P] [US3] Write DI-resolution assertion test in `tests/Sorcha.Verifier.Tests/DI/DeskVerifyTransportDiTests.cs` — build desk's `ServiceCollection`; assert resolved `IVerificationTransport` is `HaipVerificationTransport`; assert resolved `IVerifierIdentityProvider` is the stable-org provider (not the ephemeral adapter) — write first, confirm it **fails** before T018
- [ ] T017 [P] [US3] Write shared-control mount test in `tests/Sorcha.Verifier.Tests/Pages/VerifyPageTests.cs` (bUnit if available, else unit assertion) — assert the desk verify page renders `QuestionSelectionPanel` / `VerificationSessionQr` / `VerdictTrailPanel`; assert stable org identity is injected — write first, confirm it **fails**

### Implementation for User Story 3

- [ ] T018 [US3] Implement `StableOrgVerifierIdentityProvider` in `src/Apps/Sorcha.Verifier/Services/StableOrgVerifierIdentityProvider.cs` — implements `IVerifierIdentityProvider`; returns `did:sorcha:verifier:{orgId:N}` as the `client_id`; reads `orgId` from configuration or the desk app's existing org-identity service; `/// <summary>` XML docs on all public members
- [ ] T019 [US3] Override DI in `src/Apps/Sorcha.Verifier/Program.cs` or `src/Apps/Sorcha.Verifier/Extensions/ServiceCollectionExtensions.cs` — add `services.AddScoped<IVerifierIdentityProvider, StableOrgVerifierIdentityProvider>()` and `services.AddScoped<IVerificationTransport, HaipVerificationTransport>()` after the library defaults to override the stub
- [ ] T020 [US3] Rewire desk verify pages in `src/Apps/Sorcha.Verifier/Components/Pages/` — replace the bespoke `PresentationRequestBuilder`/`InMemoryVerifierSessionStore`/`Outcome.razor` composition with the shared `<QuestionSelectionPanel>` / `<VerificationSessionQr>` / `<VerdictTrailPanel>` composition; page markup references only the shared control components; do NOT delete the legacy files yet (that is US4)

**Checkpoint**: Desk DI resolution and component-mount tests green; shared control renders with stable org identity; build passes.

---

## Phase 6: User Story 4 — Legacy verify paths are retired (Priority: P2)

**Goal**: All divergent legacy verify machinery is removed; the solution builds; the verify flow works end-to-end on both hosts with no dead references or orphaned DI registrations.

**Independent Test**: The `grep` commands in `quickstart.md` Validation 4 return no matches; `dotnet build` succeeds with no warnings; `dotnet test` passes.

### Tests for User Story 4

- [ ] T021 [US4] Write retirement assertion tests in `tests/Sorcha.Verifier.Tests/Legacy/LegacyVerifyRetirementTests.cs` and `tests/Sorcha.Wallet.Pwa.Tests/Legacy/LegacyVerifyRetirementTests.cs` — compile-time checks (e.g. `typeof(...)` references that will fail to compile once the types are removed) or file-existence assertions confirming the legacy types are gone — write first, confirm they **fail** (compile error or file-found) before deletions

### Retirement

- [ ] T022 [P] [US4] Delete the PWA paste-based `VerifyFlow` component: remove `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerifyFlow.razor` (and its `.cs` code-behind if present); remove any `using` / `@using` / service registration referencing `VerifyFlow`
- [ ] T023 [P] [US4] Delete desk `PresentationRequestBuilder` and `InMemoryVerifierSessionStore`: remove `src/Apps/Sorcha.Verifier/Services/PresentationRequestBuilder.cs` and `src/Apps/Sorcha.Verifier/Services/InMemoryVerifierSessionStore.cs`; remove their DI registrations from `Program.cs` / `ServiceCollectionExtensions.cs`
- [ ] T024 [P] [US4] Delete desk local callback endpoints: remove `src/Apps/Sorcha.Verifier/Endpoints/PresentationResponseEndpoints.cs` (the `POST /r/{sessionId}/response` and `GET /r/{sessionId}/status` handlers); remove their `MapX` call from `Program.cs`
- [ ] T025 [P] [US4] Delete desk bespoke verdict page: remove `src/Apps/Sorcha.Verifier/Components/Pages/Outcome.razor` (and its `.cs` code-behind if present); remove any route or navigation reference to `Outcome`
- [ ] T026 [US4] Remove host-local `VerdictViewModel` and `IRegisterAnchorClient` duplicates from `src/Apps/Sorcha.Verifier/Services/` if still present — update all desk references to use the shared library versions from `Sorcha.UI.Components.User`
- [ ] T027 [US4] Run `dotnet build` — confirm zero errors, zero warnings; run `grep -rn "VerifyFlow\|PresentationRequestBuilder\|InMemoryVerifierSessionStore\|Outcome.razor" src/Apps/Sorcha.Wallet.Pwa src/Apps/Sorcha.Verifier` — confirm no matches; run `dotnet test` — confirm all tests pass (FR-014 / SC-005)

**Checkpoint**: All greps return no matches; `dotnet build` clean; `dotnet test` green; verify flow works end-to-end on both hosts.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, observability, and final quickstart validation.

- [ ] T028 [P] Add `/// <summary>` XML docs to all public members of `HaipVerificationTransport`, `StableOrgVerifierIdentityProvider`, `EphemeralVerifierIdentityAdapter`, and `IVerifierIdentityProvider` — required by the project XML-doc policy to avoid build warnings
- [ ] T029 [P] Record FR-008 tier status codes in `specs/164-verify-host-rewire/quickstart.md` Validation 1 table (consumer and org/desk tiers on create-request and result-poll) — fill the `___` placeholders with the observed HTTP status codes and any allowance applied
- [ ] T030 Run the full quickstart.md Validation 1–5 checklist; confirm all Done-When items pass and update the `[ ]` checkboxes in `specs/164-verify-host-rewire/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately after branching from merged `master`
- **Foundational (Phase 2)**: Depends on Phase 1 — **BLOCKS all user stories**
- **US1 (Phase 3)**: Depends on Phase 2 — the transport cannot be written without `IVerifierIdentityProvider` + `VerificationSession`
- **US2 (Phase 4)**: Depends on Phase 3 (transport must exist to override the stub)
- **US3 (Phase 5)**: Depends on Phase 3; is independent of US2 (different host, different files)
- **US4 (Phase 6)**: Depends on both US2 (Phase 4) AND US3 (Phase 5) — retire only after both rewires are live
- **Polish (Phase 7)**: Depends on Phase 6 completion

### User Story Dependencies

- **US1 (P1)**: No story dependencies — first to implement after foundational
- **US2 (P1)**: Depends on US1 (transport must be present to override); independent of US3
- **US3 (P2)**: Depends on US1; independent of US2 — can be parallelised with US2 after Phase 3 completes
- **US4 (P2)**: Depends on BOTH US2 and US3

### Within Each User Story

- Tests MUST be written and **FAIL** before the corresponding implementation task
- DI resolution assertions are written and confirmed failing before the DI overrides are added
- Retirement deletions (US4) happen only after both host rewire checkpoints are green

### Parallel Opportunities

- T003 and T004 in Phase 2 can run in parallel
- T006, T007, T008 (US1 tests) can be written in parallel before T009/T010 implement the transport
- T011 and T012 (US2 tests) can be written in parallel
- T016 and T017 (US3 tests) can be written in parallel
- US2 (Phase 4) and US3 (Phase 5) can be worked in parallel once Phase 3 is complete
- T022, T023, T024, T025 (US4 retirements) can execute in parallel once both rewires are live
- T028, T029 (Polish) can run in parallel

---

## Parallel Examples

### Phase 3 — US1 tests written in parallel:

```
T006 DI-resolution assertion test (HaipTransportDiResolutionTests.cs)
T007 Transport round-trip test (HaipVerificationTransportTests.cs)
T008 Cancellation test (HaipVerificationTransportCancellationTests.cs)
```

### Phase 4 + Phase 5 — host rewires in parallel (after Phase 3 checkpoint):

```
Developer A → T011 → T012 → T013 → T014 → T015  (PWA / US2)
Developer B → T016 → T017 → T018 → T019 → T020  (Desk / US3)
```

### Phase 6 — US4 retirements in parallel (after both rewire checkpoints):

```
T022 Delete VerifyFlow (PWA legacy)
T023 Delete PresentationRequestBuilder + InMemoryVerifierSessionStore (desk)
T024 Delete desk callback endpoints
T025 Delete Outcome.razor
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1: Setup (branch from merged `master`)
2. Complete Phase 2: Foundational (`IVerifierIdentityProvider`, `VerificationSession`, tier confirmation)
3. Complete Phase 3: US1 (live transport + tests)
4. **STOP and VALIDATE**: DI resolution assertion passes; round-trip test passes
5. Proceed to US2/US3 in parallel

### Incremental Delivery

1. Setup + Foundational → prerequisites ready
2. US1 → live transport independently verified → transport MVP
3. US2 → PWA on shared control → first host rewire (most user-visible payoff)
4. US3 → desk on shared control → second host rewire
5. US4 → legacy retired → unification complete
6. Polish → docs + quickstart validation

### Parallel Team Strategy

With two developers after Phase 3:
- Developer A: US2 (PWA rewire, T011–T015)
- Developer B: US3 (Desk rewire, T016–T020)
- Both: converge on US4 retirements together

---

## Notes

- `[P]` tasks touch different files with no intra-task dependencies — safe to run in parallel
- `[USn]` label maps each task to its acceptance criteria in `spec.md`
- DI resolution assertions (T006, T011, T016) are the **headline load-bearing tests** — the spec names "host never overrides the stub" as the single most common B3 failure mode (R4)
- The worktree local HEAD predates B1/B2; **T001 must complete before any other task** — writing code against a pre-B2 world would duplicate merged work
- Tier status codes (T005/T029) must be confirmed against a **live node**, not assumed
- Retirement tasks (US4) must follow both rewire checkpoints — deleting before rewiring removes a live surface
- All new production types must be WASM-safe (no server-only dependencies) — required for the PWA path
- Every new public member requires `/// <summary>` XML docs to avoid build warnings (per project policy)
