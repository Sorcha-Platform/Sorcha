---

description: "Task list for Feature 159 — PWA Present Camera-First Intake"
---

# Tasks: PWA Present — Camera-First Intake

**Input**: Design documents from `/specs/159-pwa-present-camera-first/`

**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/device-profile.md ✓, quickstart.md ✓

**Tests**: Included — bUnit component tests and Playwright E2E tests are explicitly specified in plan.md.

**Organization**: Tasks are grouped by user story. US1 (CameraFirst on handheld) is the MVP. US2
(PasteWithScan on desktop) and US3 (PasteOnly) share the same Foundational phase and complete the
full three-layout surface.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in every task description

---

## Phase 1: Setup

**Purpose**: Orient to the existing `Present.razor` page — camera lifecycle hooks, paste flow,
`@inject` directives, and `ParseAsync()` entry point — before making changes.

- [X] T001 Read `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` in full to map existing state fields, `SorchaQrScanner` call sites (`StartScanAsync`, `CancelScanAsync`, `DisposeAsync`), the `_deepLink`/`ParseAsync()` convergence point, the `Phase` enum, and all `@inject` declarations

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Device-profile model + JS helper + `IDeviceProfileProbe` seam + DI registration.
These pieces are shared by all three user-story layouts and must be complete before any layout
branch is added to `Present.razor`.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T002 [P] Create `DeviceFormFactor` (`Handheld`, `Desktop`), `CameraAvailability` (`Usable`, `Unavailable`), and `IntakeMode` (`CameraFirst`, `PasteWithScan`, `PasteOnly`) enums plus `DeviceProfile` record (fields: `FormFactor`, `CameraAvailability`; derived property `Mode` implementing the total 4-row mapping from data-model.md) in `src/Apps/Sorcha.Wallet.Pwa/Models/Device/DeviceProfile.cs`
- [X] T003 [P] Create `SorchaDeviceProfile.detect()` JS helper in `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/device-profile.js` — returns `{ formFactor: "handheld"|"desktop", cameraApi: bool, hasVideoInput: bool|null }` using `pointer:coarse` + `navigator.maxTouchPoints > 0` heuristic for form factor (R1) and `navigator.mediaDevices.getUserMedia` presence + optional `enumerateDevices()` videoinput count for camera availability (R2); no `getUserMedia` call; pure capability detection, no side effects
- [X] T004 Create `IDeviceProfileProbe` interface with XML `/// <summary>` docs and `ValueTask<DeviceProfile> GetProfileAsync(CancellationToken ct = default)` in `src/Apps/Sorcha.Wallet.Pwa/Services/Device/IDeviceProfileProbe.cs` (depends T002)
- [X] T005 Implement `DeviceProfileProbe : IDeviceProfileProbe` backed by `IJSRuntime` calling `SorchaDeviceProfile.detect()`; wrap interop in try/catch so any `JSException` or failure degrades to `CameraAvailability.Unavailable` (paste-safe) per contract guarantee in contracts/device-profile.md in `src/Apps/Sorcha.Wallet.Pwa/Services/Device/DeviceProfileProbe.cs` (depends T002, T003, T004)
- [X] T006 Register `IDeviceProfileProbe → DeviceProfileProbe` as scoped in `src/Apps/Sorcha.Wallet.Pwa/Program.cs` and add `<script src="js/device-profile.js"></script>` to `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html` (depends T004, T005)
- [X] T007 [P] Write `DeviceProfileProbeTests` covering all four `FormFactor × CameraAvailability` → `IntakeMode` mappings (total mapping assertion), plus the JS-error-fallback case (probe returns paste-safe `CameraAvailability.Unavailable`) in `tests/Sorcha.Wallet.Pwa.Tests/Services/Device/DeviceProfileProbeTests.cs` (depends T002, T004, T005)

**Checkpoint**: Foundation ready — `DeviceProfile`, `IDeviceProfileProbe`, and DI wiring complete. User story implementation can now begin.

---

## Phase 3: User Story 1 — Scan straight away on a phone (Priority: P1) 🎯 MVP

**Goal**: On a handheld device with a usable, permitted camera, the Present page starts the QR
viewfinder automatically on load with zero taps, and a "Paste a link instead" control is visibly
available to switch to the paste field. Camera is stopped on every exit (scan, paste-switch, navigate
away). Camera-start failure falls back gracefully to paste with a plain-language note.

**Independent Test**: Open `/wallet/present` on a handheld (or browser device-emulation with
touch + coarse pointer, permitted camera). Confirm viewfinder is live on arrival — no taps required
(SC-001). Confirm "Paste a link instead" control is present. Point at a valid verifier QR → advances
to credential matching. Tap "Paste a link instead" → camera stops; paste field appears.

- [X] T008 [P] [US1] Write bUnit tests for the `CameraFirst` layout in `tests/Sorcha.Wallet.Pwa.Tests/Pages/PresentIntakeLayoutTests.cs`: inject a fake `IDeviceProfileProbe` returning `(Handheld, Usable)`, assert viewfinder element is rendered + "Paste a link instead" control exists + `StartScanAsync` is invoked on first render; assert tapping "Paste a link instead" calls `CancelScanAsync` and shows the paste field; assert camera-start exception triggers fallback to paste + `_intakeMessage` not null
- [X] T009 [US1] Add `_intakeMode`, `_showPaste`, `_cameraRefusedThisSession`, and `_intakeMessage` state fields; inject `IDeviceProfileProbe`; populate `_intakeMode` (and derive initial `_showPaste`) from `GetProfileAsync()` in `OnInitializedAsync`; add `_autoStartFired` guard bool in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` (depends T004, T005, T006)
- [X] T010 [US1] Add `CameraFirst` layout markup branch in the `AwaitingDeepLink` phase render block — shows the QR viewfinder video element when `_intakeMode == CameraFirst && !_showPaste`; includes a clearly visible "Paste a link instead" button; shows `_intakeMessage` as a plain-language inline alert when non-null in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` (depends T009)
- [X] T011 [US1] Add auto-start logic in `OnAfterRenderAsync` (first-render guard, `CameraFirst` check): call existing `StartScanAsync()` automatically; on success set `_deepLink` and call `ParseAsync()`; on any `JSException` or failure set `_showPaste=true`, `_cameraRefusedThisSession=true`, `_intakeMessage` to a plain-language note; wire "Paste a link instead" button handler to call `CancelScanAsync()` then `_showPaste=true` in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` (depends T010)
- [X] T012 [US1] Audit all `CameraFirst` exits in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` — confirm `CancelScanAsync()` / `SorchaQrScanner.stop()` is called exactly once on: successful scan, "Paste a link instead" tap, `DisposeAsync` (navigation away), and camera-start failure (FR-008) (depends T011)

**Checkpoint**: US1 fully functional — handheld users get zero-tap camera-first flow with graceful paste fallback.

---

## Phase 4: User Story 2 — Paste-led intake on a desktop (Priority: P2)

**Goal**: On a desktop-class device with a camera, the Present page defaults to the paste field;
a "Scan with camera" control is offered but the camera does not start until the holder activates it.

**Independent Test**: Open `/wallet/present` on a desktop browser. Confirm paste field is the
default; camera is not active. Activate "Scan with camera" → viewfinder starts. Alternatively,
paste a valid `openid4vp://` link + Continue → advances to matching.

- [X] T013 [P] [US2] Add bUnit tests for the `PasteWithScan` layout to `tests/Sorcha.Wallet.Pwa.Tests/Pages/PresentIntakeLayoutTests.cs`: inject a fake probe returning `(Desktop, Usable)`, assert paste field is rendered by default + "Scan with camera" control exists + camera is NOT active on load; assert activating "Scan with camera" invokes `StartScanAsync` and shows the viewfinder; assert paste + Continue still reaches `ParseAsync()`
- [X] T014 [US2] Add `PasteWithScan` layout markup branch in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` — paste field is shown when `_intakeMode == PasteWithScan` (which sets `_showPaste=true` on init); include a "Scan with camera" button; include the existing Continue action for paste; no camera activity on load (FR-004) (depends T009)
- [X] T015 [US2] Wire "Scan with camera" button handler to call `StartScanAsync()`; on success set `_deepLink` and call `ParseAsync()`; on failure set `_intakeMessage`; on cancel return to paste view; ensure camera stop on every exit in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` (depends T014)

**Checkpoint**: US1 + US2 functional — both handheld and desktop surfaces behave per spec.

---

## Phase 5: User Story 3 — Paste-only on a device with no camera (Priority: P2)

**Goal**: On a device with no usable camera, the Present page shows only the paste field; no scan
control is offered anywhere on the intake surface. Pasting a link proceeds normally.

**Independent Test**: Open `/wallet/present` on a device with no camera API (or browser emulation
with camera blocked at OS level). Confirm only the paste field + Continue are shown — no scan
control anywhere. Paste a valid link → advances to matching.

- [X] T016 [P] [US3] Add bUnit tests for the `PasteOnly` layout to `tests/Sorcha.Wallet.Pwa.Tests/Pages/PresentIntakeLayoutTests.cs`: inject a fake probe returning `(Handheld, Unavailable)` and `(Desktop, Unavailable)`, assert paste field is rendered + absolutely no scan control element exists in the DOM; assert paste + Continue still reaches `ParseAsync()` (FR-005)
- [X] T017 [US3] Add `PasteOnly` layout branch in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` — when `_intakeMode == PasteOnly` render only the paste field and Continue action; no "Scan with camera", no "Paste a link instead" scan affordance anywhere on the intake surface (FR-005) (depends T009)
- [X] T018 [US3] Verify downstream convergence: confirm all three layout branches (`CameraFirst` auto-scan, `PasteWithScan` manual-scan, all paste paths) set `_deepLink` and call the existing `ParseAsync()` entry point identically; run `dotnet build src/Apps/Sorcha.Wallet.Pwa` with zero warnings in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` (depends T015, T017)

**Checkpoint**: All three intake layouts implemented and independently testable.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Edge cases, E2E coverage, and documentation correctness across all three layouts.

- [X] T019 [P] Verify XML `/// <summary>` docs on all new public members — `IDeviceProfileProbe`, `DeviceProfileProbe`, `DeviceProfile`, `DeviceFormFactor`, `CameraAvailability`, `IntakeMode` — in `src/Apps/Sorcha.Wallet.Pwa/Services/Device/` and `src/Apps/Sorcha.Wallet.Pwa/Models/Device/DeviceProfile.cs`; run `dotnet build` with `TreatWarningsAsErrors` to confirm zero XML-doc warnings
- [X] T020 [P] Write Playwright E2E layout-variant tests in `tests/Sorcha.UI.E2E.Tests/Docker/PresentIntakeLayoutTests.cs`: handheld emulation (touch + coarse pointer) with camera API mocked present → CameraFirst surface rendered; desktop emulation with camera → PasteWithScan surface; no-camera emulation → PasteOnly surface; assert no scan control in paste-only variant
- [X] T021 Handle FR-009 (unreadable / non-verifier QR) in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor`: ensure that a scan result that fails `IPresentationEngine.Parse` or is rejected as non-verifier shows a recoverable inline `_intakeMessage` and keeps the intake usable (retry scan or switch to paste) — camera must not be left in a stuck state
- [X] T022 Handle the "returning after denied permission" edge case in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor`: confirm `_cameraRefusedThisSession` guard prevents re-triggering the auto-start on subsequent renders after a permission denial, so the paste field stays the default on re-entry without trapping the holder in a failing viewfinder (R3)
- [X] T023 Run full unit + component test suite: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~DeviceProfileProbeTests|FullyQualifiedName~PresentIntakeLayoutTests"` — all green; review coverage on new files targets >85% per constitution IV
- [X] T024 Run E2E layout-variant tests against Docker: `dotnet test tests/Sorcha.UI.E2E.Tests --filter "FullyQualifiedName~PresentIntakeLayoutTests"` — per-variant Playwright checks pass; execute quickstart.md manual validation scenarios for US1, US2, US3 and all six edge cases

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup (Phase 1) — **BLOCKS** all user stories. T002 and T003 are parallel. T004 depends on T002. T005 depends on T002, T003, T004. T006 and T007 can proceed in parallel once T004+T005 are done.
- **US1 (Phase 3)**: Depends on Foundational completion. T008 is parallel with T009 (different files). T009 → T010 → T011 → T012 are sequential in Present.razor.
- **US2 (Phase 4)**: Depends on Foundational completion. T013 is parallel with T014 (different files). T014 → T015 sequential. Can begin in parallel with US1 if T009 (state fields) is done first.
- **US3 (Phase 5)**: Depends on Foundational completion + T009 (state fields). T016 parallel with T017. T018 depends on T015 + T017.
- **Polish (Phase 6)**: Depends on all user story phases. T019 and T020 are parallel. T021 and T022 target Present.razor so are sequential. T023 and T024 are final validation gates.

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2. No dependency on US2 or US3.
- **US2 (P2)**: Can start after Phase 2 + T009 (state field additions). No dependency on US1 beyond T009.
- **US3 (P2)**: Can start after Phase 2 + T009. No dependency on US1 or US2 beyond T009.

### Within Each User Story

- bUnit tests ([P]) can be written in parallel with implementation tasks (different files)
- State-field addition (T009) must precede all layout markup tasks (T010, T014, T017)
- Markup precedes wiring (T011, T015)
- Wiring precedes lifecycle audit (T012)
- All three layout implementations must be complete before convergence check (T018)

### Parallel Opportunities

```bash
# Phase 2 — models and JS helper in parallel:
T002: Create DeviceProfile.cs (models)
T003: Create device-profile.js (JS helper)

# Phase 2 — DI registration and probe tests in parallel (once T005 done):
T006: Register in Program.cs + add <script> to index.html
T007: Write DeviceProfileProbeTests.cs

# Phase 3 — bUnit tests and state-field work in parallel:
T008: Write CameraFirst bUnit tests (test file)
T009: Add state fields + probe injection (Present.razor)

# Phase 6 — docs and E2E tests in parallel:
T019: XML docs verification
T020: Write Playwright E2E tests
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002–T007) — CRITICAL
3. Complete Phase 3: User Story 1 (T008–T012)
4. **STOP and VALIDATE**: handheld camera-first flow works end-to-end
5. Run `dotnet test tests/Sorcha.Wallet.Pwa.Tests` — all green

### Incremental Delivery

1. Setup + Foundational → seam ready
2. US1 → zero-tap camera flow on handheld (MVP — primary value)
3. US2 → paste-default + optional scan on desktop
4. US3 → paste-only on no-camera (correctness + trust)
5. Polish → E2E, edge cases, docs
6. Each story adds value without breaking the previous one

### Single-Developer Sequential Strategy

Because all three intake layouts modify `Present.razor`, the practical order is:
1. Foundational (T002–T007) in parallel where possible
2. US1 complete (T008–T012) — MVP shippable here
3. US2 (T013–T015) — desktop surface
4. US3 (T016–T018) — no-camera surface + convergence check
5. Polish (T019–T024)

---

## Notes

- `[P]` tasks are in different files with no incomplete dependencies — they can be worked concurrently
- `[US*]` label maps each task to its user story for traceability
- All three user stories converge at `_deepLink → ParseAsync()` — this is the FR-006/SC-005 invariant
- Camera MUST be stopped on every exit path — `SorchaQrScanner.stop()` is idempotent, so calling it defensively is safe
- `_cameraRefusedThisSession` prevents re-trapping the holder in a failing viewfinder on re-entry (R3 edge case)
- The `device-profile.js` helper must never call `getUserMedia` (capability check only, R2)
- Commit after each phase checkpoint to keep git history granular
