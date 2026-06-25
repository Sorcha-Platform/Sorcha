# Tasks: Shared verify control + transport seam + config presets (PR B2)

**Branch**: `161-verify-shared-control` | **Plan**: `./plan.md`

## Phase 1 — Move shared types (keep Verifier compiling)
- [ ] **T001** — Move `QuestionPreset` (record + builtin list) → `Sorcha.UI.Components.User/Models/Verification/`; update `Sorcha.Verifier` references.
- [ ] **T002** — Move `VerdictViewModel` (+ `From` factory) → `Sorcha.UI.Components.User/Models/Verification/`; update references.
- [ ] **T003** — Move `IRegisterAnchorClient` + `RegisterAnchorClient` → `Sorcha.Verifier.Engine`; keep DI registration working in Verifier.
- [ ] **T004** — Add ProjectReference `Sorcha.UI.Components.User` → `Sorcha.Verifier.Engine`.

## Phase 2 — Seams + config presets (the user's "editable presets" ask)
- [ ] **T005** — `IVerificationPresetCatalogue` interface (`GetAll`/`GetByKey`/`ValidateCustomAsync`).
- [ ] **T006** — `VerifierPresetsOptions` + `DefaultPresetCatalogue` (config-bound, builtin fallback).
- [ ] **T007** — `IVerificationTransport` interface (StartSession/PollSession/GetVerdict) + records.

## Phase 3 — Shared components
- [ ] **T008** — `QuestionSelectionPanel.razor` (preset picker + custom form; reads catalogue).
- [ ] **T009** — `VerificationSessionQr.razor` (QR + poll state via transport).
- [ ] **T010** — `VerdictTrailPanel.razor` (4-layer trail + on-demand register-anchor layer 4).

## Phase 4 — DI + tests + build
- [ ] **T011** — Register `IVerificationPresetCatalogue` + `IRegisterAnchorClient` (no host rewiring yet).
- [ ] **T012** — Unit tests: `DefaultPresetCatalogue` (fallback/override/getbykey/custom), `VerdictViewModel.From`.
- [ ] **T013** — bUnit: `QuestionSelectionPanel`, `VerdictTrailPanel` (Loose JS).
- [ ] **T014** — `dotnet build` solution green + Verifier + UI.Core test suites green; XML doc summaries on new public members.

## Out of scope (→ B3)
- Rewiring PWA `/wallet/verify` + desk `Sorcha.Verifier` pages to the shared control; retiring old paths.
- The HAIP-backed `IVerificationTransport` impl wired into the hosts (B2 ships the interface + a
  test/desk adapter; the live HAIP wiring + tier auth lands in B3).
