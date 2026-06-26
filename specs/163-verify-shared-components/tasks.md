# Tasks: Shared verify components — question panel, session QR, verdict trail (PR B2-components, relaunch)

**Input**: Design documents from `specs/163-verify-shared-components/`

**Feature branch**: `163-verify-shared-components`

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data model**: [data-model.md](./data-model.md)

**Tests**: Included — FR-014 mandates bUnit tests for all three components as a functional requirement.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Exact file paths are included in each description

---

## Phase 1: Setup

**Purpose**: Merge the #1045 foundation (R-000 BLOCKING gate) and wire the new project reference.

> **⚠️ GATE**: T001 and T002 MUST pass before any other task can start. The #1045 seams
> (`IVerificationTransport`, `IVerificationPresetCatalogue`, `DefaultPresetCatalogue`,
> `VerificationPreset`) are absent from this branch — the entire feature depends on them.

- [ ] T001 Merge `origin/master` into this branch to bring in PR #1045 (B2-foundation): run `git merge origin/master` and resolve any conflicts (prerequisite gate — R-000)
- [ ] T002 Verify the #1045 seams are now present: run `grep -rl "interface IVerificationTransport" src/` and `grep -rl "class DefaultPresetCatalogue" src/` — both must return files; run `dotnet build` to confirm base builds
- [ ] T003 Add `<ProjectReference Include="../../Common/Sorcha.Verifier.Engine/Sorcha.Verifier.Engine.csproj" />` to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj` (FR-010)

**Checkpoint**: Branch includes #1045 seams, solution builds with new project reference, no reference cycles.

---

## Phase 2: Foundational — Type Relocations (US5)

**Purpose**: Relocate `VerdictViewModel` and `IRegisterAnchorClient`/`RegisterAnchorClient`/`RegisterAnchorResult` into shared libraries. Must complete before US1–US4 component work begins. Constitutes the US5 story implementation and its regression guard.

**⚠️ CRITICAL**: No component task can begin until T007 (relocation complete + build green) is verified.

- [ ] T004 [P] [US5] Relocate `VerdictViewModel.cs` from `src/Apps/Sorcha.Verifier/Services/` to `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Verification/VerdictViewModel.cs` — copy the file, preserving the class and all properties (`OverallPass`, `Headline`, `IssuerDid`, `IssuerDisplayName`, `PortraitBase64`, `AgeOver18`, `Disclosed`, `Withheld`, `Layers`, `Errors`, `RegisterAnchorId`, `CredentialId`) (FR-008)
- [ ] T005 [US5] Update `VerdictViewModel.From(...)` factory in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/Verification/VerdictViewModel.cs` — replace the `VerifierSession` input parameter with `VerificationPreset question` so it reads `RequiredVct`, `RequiredClaims`, and `KnownCredentialClaims` from the preset rather than a desk-only session store (R-001, FR-011)
- [ ] T006 [P] [US5] Relocate `IRegisterAnchorClient.cs`, `RegisterAnchorClient.cs`, and `RegisterAnchorResult` from `src/Apps/Sorcha.Verifier/Services/` to `src/Common/Sorcha.Verifier.Engine/` — copy with correct namespace, keeping `CheckAsync(string registerId, string credentialId, CancellationToken)` contract and `RegisterService:PublicBaseUrl` config read unchanged (FR-009, R-004)
- [ ] T007 [US5] Remove the now-relocated source files from `src/Apps/Sorcha.Verifier/Services/` (delete `VerdictViewModel.cs`, `IRegisterAnchorClient.cs`, `RegisterAnchorClient.cs`) and update all `using` directives in `src/Apps/Sorcha.Verifier/` to reference the types from their new shared namespaces so no duplicates remain (FR-013)
- [ ] T008 [US5] Build the solution (`dotnet build`) and run `dotnet test tests/Sorcha.Verifier.Tests` — all must pass with zero duplicate-type errors and zero broken references; this is the SC-004 regression gate (US5 independent test)

**Checkpoint**: `VerdictViewModel` lives only in `Sorcha.UI.Components.User`; `IRegisterAnchorClient` + impl live only in `Sorcha.Verifier.Engine`; `Sorcha.Verifier` app consumes both from shared homes and existing tests pass.

---

## Phase 3: Foundational — DI Extension + Stub Transport (US4)

**Purpose**: Ship `NotConfiguredVerificationTransport` (FR-004) and register all three seams in the shared DI extension (FR-005/FR-006), making every component activatable out of the box. This is the central fix that unblocks the parked relaunch.

**⚠️ CRITICAL**: US1–US3 component tests all depend on the DI extension being wired (T010); the extension depends on the stub transport (T009).

- [ ] T009 [US4] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/User/Verification/NotConfiguredVerificationTransport.cs` — implements `IVerificationTransport`; `StartSessionAsync` returns `new VerificationSessionStarted(SessionId: "", QrDeepLink: "", Purpose: question.Purpose, RequiredVct: question.RequiredVct)`; `PollSessionAsync` returns `new VerificationSessionPoll(IsComplete: false, VpToken: null, PresentationSubmission: null)`; never throws (FR-004, R-002)
- [ ] T010 [US4] Extend `AddSorchaUserComponents(IServiceCollection, IConfiguration)` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Extensions/Shared/ServiceCollectionExtensions.cs` — add `services.TryAddSingleton<IVerificationPresetCatalogue, DefaultPresetCatalogue>()` + `services.Configure<VerifierPresetsOptions>(config.GetSection("VerifierPresets"))`, `services.TryAddSingleton<IVerificationTransport, NotConfiguredVerificationTransport>()`, and `services.AddHttpClient<IRegisterAnchorClient, RegisterAnchorClient>()` guarded so a host override wins (FR-005/FR-006, R-005)
- [ ] T011 [US4] Write `tests/Sorcha.UI.Core.Tests/Verification/SharedVerifyRegistrationTests.cs` — build a `ServiceCollection`, call real `AddSorchaUserComponents(services, config)`; assert `IVerificationPresetCatalogue` → `DefaultPresetCatalogue`, `IVerificationTransport` → `NotConfiguredVerificationTransport`, `IRegisterAnchorClient` → `RegisterAnchorClient` all resolve; assert a host-registered transport override wins over the default stub (US4 scenarios 1–3, SC-002, R-006)

**Checkpoint**: `dotnet test tests/Sorcha.UI.Core.Tests --filter "SharedVerifyRegistrationTests"` passes; all three seams resolve from a single `AddSorchaUserComponents` call; host override is proven.

---

## Phase 4: User Story 1 — Shared question-selection panel (Priority: P1) 🎯 MVP

**Goal**: A `QuestionSelectionPanel` component that reads `IVerificationPresetCatalogue` and raises `OnQuestionSelected` — the smallest independently testable slice proving the shared-component approach works.

**Independent Test**: `dotnet test tests/Sorcha.UI.Core.Tests --filter "QuestionSelectionPanelTests"` — presets render as selectable options, custom affordance renders, selecting a preset raises `OnQuestionSelected` with the chosen `VerificationPreset`.

### Implementation for User Story 1

- [ ] T012 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/QuestionSelectionPanel.razor` — injects `IVerificationPresetCatalogue`; renders each `catalogue.GetAll()` preset as a MudBlazor selectable option (label = `preset.Label`); renders a custom-question affordance; raises `[Parameter] EventCallback<VerificationPreset> OnQuestionSelected` with the built/looked-up preset when operator picks a preset or confirms a valid custom question (FR-001, C1 contract)

### Tests for User Story 1

- [ ] T013 [US1] Write `tests/Sorcha.UI.Core.Tests/Verification/QuestionSelectionPanelTests.cs` — mount `QuestionSelectionPanel` via `AddSorchaUserComponents` DI; assert three presets from a three-preset catalogue render as selectable options; assert the custom-question affordance renders; assert selecting a preset raises `OnQuestionSelected` with the correct `VerificationPreset` (US1 scenarios 1–3, FR-014, R-006)

**Checkpoint**: `QuestionSelectionPanel` activates under shared DI and all US1 scenarios pass.

---

## Phase 5: User Story 2 — Shared session QR + polling with resolvable transport (Priority: P1)

**Goal**: `VerificationSessionQr` that starts a session, renders the OID4VP QR, polls for completion through `IVerificationTransport`, and implements clean `IAsyncDisposable` — activatable under the default stub transport.

**Independent Test**: `dotnet test tests/Sorcha.UI.Core.Tests --filter "VerificationSessionQrTests"` — mounts under default DI without throwing (not-configured state), renders QR + polls to completion with fake transport, and disposes mid-poll without unobserved exception.

### Implementation for User Story 2

- [ ] T014 [US2] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerificationSessionQr.razor` — injects `IVerificationTransport`; accepts `[Parameter, EditorRequired] VerificationPreset Question` and `[Parameter] CancellationToken CancellationToken = default`; calls `StartSessionAsync` on initialisation; if `SessionId`/`QrDeepLink` is empty (stub sentinel) renders the not-configured state and does not poll; otherwise renders QR via QRCoder + deep-link, runs an async poll loop (linked CTS to the host token) until `IsComplete`; raises `[Parameter] EventCallback<string> OnCompleted` with `VpToken`; on terminal transport error shows error/retry state; implements `IAsyncDisposable` — cancels linked CTS, awaits the poll task swallowing `OperationCanceledException`, disposes CTS, guards post-disposal renders with a disposed flag (FR-002, FR-004, FR-007, C2 contract, R-003)

### Tests for User Story 2

- [ ] T015 [US2] Write `tests/Sorcha.UI.Core.Tests/Verification/VerificationSessionQrTests.cs` — four bUnit scenarios via `AddSorchaUserComponents` DI: (1) mount under default DI, assert activates without throwing and renders not-configured state; (2) override transport with a fake returning a known session + QR deep-link, assert QR/deep-link renders; (3) fake transport returns pending then complete, assert `OnCompleted` fires with `VpToken`; (4) dispose component mid-poll, assert loop is cancelled, `DisposeAsync` completes, no post-disposal render and no unobserved exception (US2 scenarios 1–5, FR-014, R-003/R-006)

**Checkpoint**: `VerificationSessionQr` activates under default DI, all four bUnit scenarios pass, dispose-mid-poll is deterministically proven.

---

## Phase 6: User Story 3 — Shared verdict trail with on-demand register anchor (Priority: P1)

**Goal**: `VerdictTrailPanel` that renders the four-layer verdict trail from a `VerdictViewModel` (built client-side from preset + outcome) with an on-demand layer-4 register-anchor check via the relocated `IRegisterAnchorClient`.

**Independent Test**: `dotnet test tests/Sorcha.UI.Core.Tests --filter "VerdictTrailPanelTests"` — headline + disclosed/withheld split + first three layers render with no network call; layer-4 affordance triggers `IRegisterAnchorClient.CheckAsync` and renders returned anchor status.

### Implementation for User Story 3

- [ ] T016 [US3] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Verify/VerdictTrailPanel.razor` — injects `IRegisterAnchorClient`; accepts `[Parameter, EditorRequired] VerdictViewModel Verdict` and optional `[Parameter] EventCallback OnAnchorChecked`; renders headline (`Verdict.Headline`, `Verdict.OverallPass`), issuer identity, portrait/age chips when present, disclosed vs withheld claim split (`Verdict.Disclosed`/`Verdict.Withheld`), and the three offline layers (LivePresentation, IssuerSignature, Revocation) from `Verdict.Layers` with no network call on first display; provides a layer-4 affordance button that calls `IRegisterAnchorClient.CheckAsync(Verdict.RegisterAnchorId, Verdict.CredentialId, ct)`, appends/replaces the RegisterAnchor `ValidationLayerResult` in `Verdict.Layers`, re-renders, and raises `OnAnchorChecked`; an `Unverified` layer-4 never flips `OverallPass` (FR-003, C3 contract, data-model layer state transition)

### Tests for User Story 3

- [ ] T017 [US3] Write `tests/Sorcha.UI.Core.Tests/Verification/VerdictTrailPanelTests.cs` — build a `VerdictViewModel` from a representative `VerificationOutcome` (three offline layers); mount `VerdictTrailPanel` via `AddSorchaUserComponents` DI; assert headline, disclosed/withheld split, and first three layers render; trigger the layer-4 affordance and assert the registered mock `IRegisterAnchorClient.CheckAsync` is called and the trail re-renders with the returned anchor status (US3 scenarios 1–3, FR-014, R-006)

**Checkpoint**: All three components and all four bUnit test files (SharedVerifyRegistration, QuestionSelectionPanel, VerificationSessionQr, VerdictTrailPanel) are green.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: XML doc pass, zero-warning build, full test gate, scope guard, and doc updates.

- [ ] T018 [P] Add `/// <summary>` XML documentation to all new and relocated public members in `Sorcha.UI.Components.User` (`NotConfiguredVerificationTransport`, `VerdictViewModel` relocated factory, `QuestionSelectionPanel`, `VerificationSessionQr`, `VerdictTrailPanel`, DI extension additions) and `Sorcha.Verifier.Engine` (`IRegisterAnchorClient`, `RegisterAnchorClient`, `RegisterAnchorResult`) (FR-015, build-warning convention)
- [ ] T019 Run `dotnet build -warnaserror` targeting at minimum `Sorcha.UI.Components.User` and `Sorcha.Verifier.Engine` — zero new XML-doc warnings (FR-015 gate, quickstart.md Build step)
- [ ] T020 Run the full verification suite: `dotnet test tests/Sorcha.UI.Core.Tests --filter "FullyQualifiedName~Verification"` and `dotnet test tests/Sorcha.Verifier.Tests` — all pass (SC-001/SC-002/SC-004)
- [ ] T021 [P] Run quickstart.md scope guard: `git diff --name-only origin/master...HEAD | grep -E "wallet/[Vv]erify|Sorcha.Verifier/(Pages|Components)"` — must return empty (SC-005; no host pages rewired, no legacy paths removed)
- [ ] T022 [P] Update `docs/reference/development-status.md` and `.specify/MASTER-TASKS.md` to mark feature 163 complete; update feature notes to confirm B2-components wave shipped the three shared components and the DI fix

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately; T001 is the absolute gate
- **Phase 2 (Foundational / US5)**: Requires Phase 1 complete (T002 green); T004 and T006 can run in parallel
- **Phase 3 (Foundational / US4)**: Requires Phase 2 complete (T008 green); T009 must precede T010
- **Phase 4 (US1)**: Requires Phase 3 complete (T010 wired)
- **Phase 5 (US2)**: Requires Phase 3 complete (T009 + T010); independent of US1
- **Phase 6 (US3)**: Requires Phase 2 (T005 — relocated VerdictViewModel) and Phase 3 complete; independent of US1/US2
- **Phase 7 (Polish)**: Requires all preceding phases complete

### User Story Dependencies

- **US5 (Phases 1–2)**: Must complete before US1, US2, US3 — the relocation is the blocking prerequisite
- **US4 (Phase 3)**: Must complete before US1, US2, US3 — the DI extension is what makes components activatable
- **US1, US2, US3 (Phases 4–6)**: All unblock after Phase 3; can proceed in parallel if staffed
- **Polish (Phase 7)**: After all US phases

### Parallel Opportunities

- T004 [P] and T006 [P] — both are relocation tasks targeting different libraries
- T012 (US1 implementation) and T014 (US2 implementation) and T016 (US3 implementation) — different `.razor` files, unblocked together after Phase 3
- T013 (US1 tests) and T015 (US2 tests) and T017 (US3 tests) — different test files
- T018 (XML docs), T021 (scope guard), T022 (docs) — independent polish tasks

---

## Parallel Example: Phases 4–6 (after Phase 3 complete)

```bash
# All three component + test pairs can be done in parallel:
# Developer A / Agent A:
T012 Create QuestionSelectionPanel.razor
T013 Write QuestionSelectionPanelTests.cs

# Developer B / Agent B:
T014 Create VerificationSessionQr.razor
T015 Write VerificationSessionQrTests.cs

# Developer C / Agent C (also needs T005 done):
T016 Create VerdictTrailPanel.razor
T017 Write VerdictTrailPanelTests.cs
```

---

## Implementation Strategy

### MVP First (US5 → US4 → US1)

1. Complete Phase 1 (Setup — merge + project ref)
2. Complete Phase 2 (US5 relocations — T004–T008)
3. Complete Phase 3 (US4 DI extension — T009–T011)
4. Complete Phase 4 (US1 QuestionSelectionPanel — T012–T013)
5. **STOP and VALIDATE**: `dotnet test tests/Sorcha.UI.Core.Tests --filter "QuestionSelectionPanelTests"` passes
6. Proceed to Phase 5 (US2) and Phase 6 (US3)

### Incremental Delivery

1. Phases 1–2: Branch ready, relocations clean, desk verifier green (US5 done)
2. Phase 3: DI activatable — shared library self-contained (US4 done)
3. Phase 4: Question picker ships (US1) — smallest usable component
4. Phase 5: Session QR ships (US2) — the core flow, the central relaunch fix
5. Phase 6: Verdict trail ships (US3) — rich verdict client-side
6. Phase 7: Polish → ready for B3 host rewiring

---

## Notes

- **[P]** = different files, no dependency on an incomplete sibling task — safe to parallelize
- **[Story]** label maps each task to its user story for traceability (US1–US5)
- Tests are mandatory (FR-014) and use the real `AddSorchaUserComponents` DI extension (R-006) — not hand-built collections
- Scope boundary is strictly B2: no host pages (PWA `/wallet/verify`, desk verifier pages) are touched; no legacy `VerifyFlow`/`PresentationRequestBuilder`/`InMemoryVerifierSessionStore` is removed (FR-012/SC-005)
- `VerdictViewModel.Layers` is mutable (`List<ValidationLayerResult>`) by design — the component appends the layer-4 result on-demand; an `Unverified` layer-4 never vetoes `OverallPass`
- The stub transport sentinel is an empty `SessionId`/`QrDeepLink` from `StartSessionAsync` — no interface change needed (R-002)
- `IAsyncDisposable` on `VerificationSessionQr` is non-negotiable — the dispose-mid-poll bUnit assertion (T015) is what makes the cancellation claim verifiable (R-003)
