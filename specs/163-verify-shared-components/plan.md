# Implementation Plan: Shared verify components — question panel, session QR, verdict trail (PR B2-components, relaunch)

**Branch**: `163-verify-shared-components` | **Date**: 2026-06-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/163-verify-shared-components/spec.md`

**Parent design**: `docs/superpowers/specs/2026-06-25-verify-unification-design.md` (stage B2)

## Summary

Build the **three shared Blazor components** that complete the verify-unification B2 foundation — a
preset `QuestionSelectionPanel`, a session-lifecycle `VerificationSessionQr`, and a four-layer
`VerdictTrailPanel` — in `Sorcha.UI.Components.User/Components/Verify/`, consuming the foundation seams
shipped by #1045 (`IVerificationTransport`, `IVerificationPresetCatalogue`, `DefaultPresetCatalogue`,
`VerificationPreset`). The relaunch's headline correction (the prior attempt parked because
`VerificationSessionQr` injected an unregistered `IVerificationTransport`) is a **self-contained,
resolvable DI story**: ship a default stub transport (`NotConfiguredVerificationTransport`) and a single
DI extension that registers a concrete implementation for *every* injected seam, so each component
activates — and is bUnit-testable — out of the box. Relocate `VerdictViewModel` into
`Sorcha.UI.Components.User` and `IRegisterAnchorClient`/`RegisterAnchorClient` into
`Sorcha.Verifier.Engine`, add the `Components.User → Verifier.Engine` project reference, and compute the
rich verdict **client-side** from `IVerifiablePresentationValidator`. **No host page is rewired and no
legacy paste/builder path is retired** — that is PR B3.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (`net10.0`)

**Primary Dependencies**: Blazor (Razor class library, `Microsoft.NET.Sdk.Razor`), MudBlazor (UI),
QRCoder (QR rendering — already referenced by `Sorcha.UI.Components.User`), `Sorcha.Verifier.Engine`
(validator + outcome/layer models, BouncyCastle-backed, WASM-safe), `Microsoft.Extensions.*` DI/Options/Http.

**Storage**: N/A (UI components; no persistence. Register-anchor check is a transient HTTP read against
the Register Service public endpoints via the relocated `RegisterAnchorClient`.)

**Testing**: xUnit.v3 + bUnit + Moq + FluentAssertions, in `tests/Sorcha.UI.Core.Tests`
(component/DI tests) and the existing `tests/Sorcha.Verifier.Tests` (relocation regression guard).

**Target Platform**: Browser — Blazor Server (`/app`) and Blazor WASM (`Sorcha.Wallet.Pwa`). The shared
library and `Sorcha.Verifier.Engine` are both WASM-safe; the verdict must compute client-side.

**Project Type**: Shared front-end component library (`Sorcha.UI.Components.User`) + a shared Common
engine library (`Sorcha.Verifier.Engine`).

**Performance Goals**: N/A (interactive UI). Poll loop must be cooperative and cancellable; no fixed
throughput target. Disposal must cancel the in-flight poll within one poll interval.

**Constraints**: No reference cycle from the new `Components.User → Verifier.Engine` reference
(`Verifier.Engine` is a Common library and must not reference `Components.User`). No host-page rewiring
and no legacy-path removal (scope boundary to B3). No new build warnings (XML docs on all new/relocated
public members). The default transport must be overridable by a host (`TryAdd*` semantics).

**Scale/Scope**: 3 new components, 1 stub transport, 1 DI extension, 2 type relocations
(`VerdictViewModel`, `IRegisterAnchorClient`+impl), 1 new project reference, and bUnit tests for all
three components plus a DI-resolution test. Estimated ~10–14 source files touched/added.

### ⚠️ Prerequisite — branch base predates the #1045 foundation

This branch (`prodexec/8befcb6857d8`, HEAD `00facdbd`) was cut from `8a75eb4b` (#1028), **before**
`b97088d5` (#1045, the B2-foundation) merged to `master`. The foundation seams the spec says "already
shipped" (`IVerificationTransport`, `IVerificationPresetCatalogue`, `DefaultPresetCatalogue`,
`VerificationPreset`) are **absent from the working tree** (verified by `grep` + `git merge-base
--is-ancestor`). **Before any component work, the #1045 foundation must be present** — merge/rebase
`origin/master` into this branch (preferred, matches the spec's "builds on #1045"). This is captured as
the first research decision (R-000) and the first task gate. See [research.md](./research.md).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First / dependency direction flows downward | ✅ PASS | New ref is `Components.User (Apps) → Verifier.Engine (Common)` — downward only. `Verifier.Engine` does not (and must not) reference `Components.User`; no cycle. `IRegisterAnchorClient` relocating *into* the Common engine removes an Apps→only coupling, improving layering. |
| II. Security First | ✅ PASS | No secrets. Verdict trail detail "never carries secrets" (existing `ValidationLayerResult` contract). Register-anchor re-verifies the inclusion proof (does not trust the read). Stub transport returns an explicit not-configured state, never a fake "pass". |
| III. API Documentation (XML docs on all public members) | ✅ PASS (gated by FR-015) | All new/relocated public members get `/// <summary>`. Verified by zero-new-warnings build. UI components, not REST — Scalar/OpenAPI N/A. |
| IV. Testing (>85% new code, xUnit, deterministic, isolated) | ✅ PASS (gated by FR-014/SC-001) | bUnit tests for all three components activate them through the shared DI registration; deterministic test transport drives the poll sequence; dispose-mid-poll asserted. |
| V. Code Quality (nullable, async/await, DI, no warnings) | ✅ PASS | Nullable enabled (inherited). Poll loop is async/cancellable; `IAsyncDisposable` on `VerificationSessionQr`. No new warnings. |
| VI. Blueprint Standards | ➖ N/A | No blueprints touched. |
| VII. Domain-Driven Design (ubiquitous language) | ✅ PASS | Uses existing terms (Disclosure/Participant/verdict layers); no renames. |
| VIII. Observability | ➖ N/A (light) | UI components; the relocated `RegisterAnchorClient` keeps its existing logging. No new telemetry required by scope. |

**No violations.** Complexity Tracking table below is intentionally empty.

## Project Structure

### Documentation (this feature)

```text
specs/163-verify-shared-components/
├── spec.md              # Feature specification (input)
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output — decisions R-000..R-00n
├── data-model.md        # Phase 1 output — relocated/new types + component params
├── quickstart.md        # Phase 1 output — build/test validation guide
├── contracts/           # Phase 1 output — component + DI contracts
│   ├── components.md     #   the 3 component parameter/event contracts
│   └── di-extension.md   #   the single shared registration contract
├── checklists/          # (pre-existing)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/         # shared user-facing component library (Razor SDK; WASM-capable)
├── Sorcha.UI.Components.User.csproj                  # ADD: ProjectReference → ../../Common/Sorcha.Verifier.Engine
├── Components/Verify/
│   ├── VerifyFlow.razor                              # (existing legacy paste flow — UNCHANGED, B3 retires)
│   ├── QuestionSelectionPanel.razor                  # NEW (FR-001)  reads IVerificationPresetCatalogue
│   ├── VerificationSessionQr.razor                   # NEW (FR-002/FR-007)  IVerificationTransport; IAsyncDisposable; CancellationToken
│   └── VerdictTrailPanel.razor                       # NEW (FR-003)  4-layer trail + on-demand layer-4
├── Services/User/Verification/
│   ├── IVerificationTransport.cs                     # (from #1045 — prerequisite)
│   ├── IVerificationPresetCatalogue.cs               # (from #1045 — prerequisite)
│   ├── DefaultPresetCatalogue.cs                     # (from #1045 — prerequisite)
│   └── NotConfiguredVerificationTransport.cs         # NEW (FR-004)  explicit not-yet-wired start/poll
├── Models/Verification/
│   ├── VerificationPreset.cs                         # (from #1045 — prerequisite)
│   └── VerdictViewModel.cs                           # RELOCATED here from Sorcha.Verifier (FR-008)
└── Extensions/Shared/
    └── ServiceCollectionExtensions.cs                # EXTEND AddSorchaUserComponents (FR-005/FR-006)

src/Common/Sorcha.Verifier.Engine/                    # shared validator engine (Common; WASM-safe)
├── IRegisterAnchorClient.cs                          # RELOCATED here from Sorcha.Verifier (FR-009)
├── RegisterAnchorClient.cs                            #   + RegisterAnchorResult record
└── Models/VerifierSession.cs                         # (existing VerificationOutcome / ValidationLayerResult / enums — UNCHANGED)

src/Apps/Sorcha.Verifier/                              # desk verifier app — consumes relocated types (FR-013)
└── Services/                                          #   VerdictViewModel.cs & IRegisterAnchorClient.cs DELETED here; usings updated

tests/Sorcha.UI.Core.Tests/Verification/              # bUnit + DI tests (FR-014)
│   QuestionSelectionPanelTests.cs                     # NEW
│   VerificationSessionQrTests.cs                      # NEW  (not-configured, QR, poll-to-complete, dispose-mid-poll)
│   VerdictTrailPanelTests.cs                          # NEW  (trail render + on-demand layer-4)
│   SharedVerifyRegistrationTests.cs                   # NEW  (US4 — all seams resolve from one extension)
tests/Sorcha.Verifier.Tests/                           # existing suite stays green after relocation (FR-013/SC-004)
```

**Structure Decision**: Two real libraries are touched, matching the spec's relocation targets:
the **Apps** Razor library `Sorcha.UI.Components.User` (gains the three components, the stub transport,
the relocated `VerdictViewModel`, the DI extension, and a new ProjectReference) and the **Common**
engine `Sorcha.Verifier.Engine` (gains the relocated `IRegisterAnchorClient`/`RegisterAnchorClient`).
The desk `Sorcha.Verifier` app is edited only to delete the relocated definitions and update `using`s.
Tests live in the existing `tests/Sorcha.UI.Core.Tests` (its `RootNamespace` is `Sorcha.UI.Core`, and it
already carries the #1045 `DefaultPresetCatalogueTests`) and the existing `tests/Sorcha.Verifier.Tests`.

## Complexity Tracking

> No constitution violations — table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
