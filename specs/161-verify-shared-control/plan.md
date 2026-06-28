# Implementation Plan: Shared verify control + transport seam + config presets (PR B2)

**Branch**: `161-verify-shared-control` | **Spec**: `./spec.md`
**Parent design**: `docs/superpowers/specs/2026-06-25-verify-unification-design.md` (stage B2)
**Builds on**: PR B1 (#1044 — HAIP returns raw vp_token on poll).

## Goal
Extract the desk `Sorcha.Verifier`'s verify experience into a **shared** Blazor control set in
`Sorcha.UI.Components.User`, behind a transport seam + a **config-driven preset catalogue**, with the
rich verdict computed **client-side** (engine is WASM-safe). Lands the machinery + tests; both hosts
get rewired in PR B3.

## Move / Create / Keep (from the extraction map)
**Move → `Sorcha.UI.Components.User`:**
- `QuestionPreset` record + builtin list → `Models/Verification/QuestionPreset.cs`
- `VerdictViewModel` (+ `From(session, outcome)` factory) → `Models/Verification/VerdictViewModel.cs`

**Move → `Sorcha.Verifier.Engine` (shared, both hosts already ref it):**
- `IRegisterAnchorClient` + `RegisterAnchorClient` (pure HTTP, config base URL; no Verifier coupling).

**Create (shared components) in `Components/Verify/`:**
- `QuestionSelectionPanel.razor` — preset picker + custom form (reads `IVerificationPresetCatalogue`).
- `VerificationSessionQr.razor` — QR render + poll-for-result state.
- `VerdictTrailPanel.razor` — 4-layer trail + on-demand register-anchor (layer 4) affordance.

**Create (seams) in `Services/Verification/`:**
- `IVerificationTransport` — `StartSessionAsync(question) → {sessionId, qrDeepLink}`,
  `PollSessionAsync(sessionId) → {complete, vpToken?}`, `GetVerdictAsync(sessionId) → VerdictViewModel?`.
  (HAIP-backed impl wired in B3; B2 may ship a desk-side adapter for tests.)
- `IVerificationPresetCatalogue` — `GetAll()`, `GetByKey(key)`, `ValidateCustomAsync(...)`.
- `DefaultPresetCatalogue` — config-backed (binds a `VerifierPresets` options section / JSON), with the
  builtin presets as the bundled fallback. **This is the user's "edit presets without a rewrite" ask.**

**Keep (desk-only, until B3):** `IPresentationRequestBuilder`, `PresentationRequestBuilder`,
`IVerifierSessionStore`. Verifier pages stay as-is in B2 (rewired in B3).

## Project-reference / DI deltas
- `Sorcha.UI.Components.User.csproj` → add ProjectReference to `Sorcha.Verifier.Engine` (for
  `VerificationOutcome`/`ValidationLayer*` models + the moved `IRegisterAnchorClient`).
- DI: register `IVerificationPresetCatalogue → DefaultPresetCatalogue`; `IRegisterAnchorClient` via
  `AddHttpClient` (config `RegisterService:PublicBaseUrl`). No host rewiring yet (B3).

## Config-driven presets (detail)
- Options model `VerifierPresetsOptions { Presets: VerificationPreset[] }` bound from a
  `"VerifierPresets"` config section (appsettings / mounted JSON), with the builtin set as fallback
  when the section is absent/empty. Optional read-through serving endpoint can be added in B3 if both
  hosts need a single live catalogue; B2 ships the config-bound catalogue + bundled default.

## Testing (bUnit + unit)
- `DefaultPresetCatalogue`: builtin fallback, config override, `GetByKey`, custom validation.
- `VerdictViewModel.From`: layer mapping, disclosed/withheld split, register-anchor id extraction.
- bUnit for `QuestionSelectionPanel` (renders presets, custom form) and `VerdictTrailPanel` (renders
  the layers; layer-4 affordance) with `JSRuntimeMode.Loose`.

## Risks
- Moving shared types updates `Sorcha.Verifier` references in the same change to keep it compiling.
- Large blind Blazor refactor of a working app — verify with build + the existing Verifier test suite;
  the component UI is the part most warranting human/prodexec review.
