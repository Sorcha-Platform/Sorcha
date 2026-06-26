# Implementation Plan: PWA Present — Camera-First Intake

**Branch**: `159-pwa-present-camera-first` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/159-pwa-present-camera-first/spec.md`

## Summary

Make the Citizen Wallet PWA "Present a credential" page (`Present.razor`) choose its intake
layout from the holder's device on load, so the fastest path is the default one:

- **Camera-first** — handheld with a usable camera: the QR viewfinder auto-starts with zero taps,
  plus a "Paste a link instead" escape control.
- **Paste-with-scan** — desktop-class device with a usable camera: paste field is the default,
  with a "Scan with camera" control that starts the viewfinder on demand.
- **Paste-only** — no usable camera: only the paste field, no scan affordance at all.

All three layouts feed the **same** existing `_deepLink → ParseAsync()` integration point, so the
downstream parse → match → pick → consent → send → done pipeline (Feature 114) is reused unchanged.
The technical approach is a small client-side device-classification probe (JS interop for camera
availability + form-factor signals) plus a layout-selection state added to the existing page, with
graceful fallback to paste whenever camera start fails.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blazor WebAssembly)

**Primary Dependencies**: Blazor WASM, MudBlazor, `IJSRuntime` interop, vendored `nimiq/qr-scanner`
(`wwwroot/js/vendor/qr-scanner/`) via `wwwroot/js/qr-scanner-bridge.js`

**Storage**: N/A — no new persisted state. Per-session, in-memory layout/permission state only;
existing credential cache (`ICredentialCache`, IndexedDB) is untouched.

**Testing**: xUnit + bUnit for page/component tests (`tests/Sorcha.Wallet.Pwa.Tests/`); Playwright
E2E (`tests/Sorcha.UI.E2E.Tests/`) for layout-variant behaviour against Docker.

**Target Platform**: Citizen Wallet PWA (`Sorcha.Wallet.Pwa`), served at `/wallet/`, running in
modern mobile + desktop browsers (WASM).

**Project Type**: Web — Blazor WASM front-end (single app project) with shared user-facing
component library (`Sorcha.UI.Components.User`).

**Performance Goals**: Layout decision resolves before/at first interactive render — no visible
flash of the wrong layout; camera auto-start on handheld within the platform's normal getUserMedia
latency (SC-001: zero taps after load).

**Constraints**: Must obey the PWA CSP (`script-src 'self'`, `worker-src 'self' blob:`) already
satisfied by the qr-scanner bridge; camera permission prompting follows platform defaults; camera
MUST always be stopped/released on navigation, switch-to-paste, cancel, or scan completion (FR-008);
no dead-end screens — paste is always reachable (FR-007/FR-010).

**Scale/Scope**: One page (`Present.razor`), one new small device-probe seam (service + JS helper),
three intake layout branches. No backend, API, or schema changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
|-----------|------------|--------|
| I. Microservices-First | Front-end-only change; no new service, no cross-service coupling, no upward dependency. | PASS |
| II. Security First | No secrets, no new data at rest. Camera access via existing bridge under existing CSP; permission denial falls back to paste (no silent failure). Pasted/scanned request still validated by the existing `IPresentationEngine.Parse`. | PASS |
| III. API Documentation | No public REST/gRPC API surface added. New C# public members (device-profile service interface) get `/// <summary>` XML docs per project convention. | PASS |
| IV. Testing | bUnit tests for each of the three layout branches + fallback paths; Playwright E2E per variant; reuse existing `PresentationEngineTests`. Target >85% on new code. | PASS |
| V. Code Quality | Nullable enabled, async/await for JS interop, DI for the device-profile probe, no new compiler warnings. | PASS |
| VI. Blueprint Standards | N/A — no blueprints. | PASS |
| VII. Domain-Driven Design | Uses established ubiquitous language (Present, credential, verifier request); no domain-model changes. | PASS |
| VIII. Observability | Front-end UI branch; no new service telemetry required. Camera-fallback events surface to the holder via inline plain-language messaging, not server logs. | PASS |

**Result**: PASS — no violations. Complexity Tracking section intentionally omitted.

## Project Structure

### Documentation (this feature)

```text
specs/159-pwa-present-camera-first/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── device-profile.md   # Client-side device-profile contract (JS interop + C# seam)
├── checklists/          # Pre-existing
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.Wallet.Pwa/
├── Pages/
│   └── Present.razor                     # MODIFY — add IntakeMode selection + three layout branches
├── Services/
│   └── Device/
│       ├── IDeviceProfileProbe.cs        # NEW — async probe returning camera availability + form factor
│       └── DeviceProfileProbe.cs         # NEW — JS-interop-backed implementation
├── Models/
│   └── Device/
│       └── DeviceProfile.cs              # NEW — record: FormFactor + CameraAvailability + derived IntakeMode
├── wwwroot/js/
│   ├── qr-scanner-bridge.js              # REUSE — isSupported()/start()/stop()/dispose (unchanged)
│   └── device-profile.js                 # NEW — form-factor + camera-presence detection helper
└── Program.cs                            # MODIFY — register IDeviceProfileProbe

tests/Sorcha.Wallet.Pwa.Tests/
├── Pages/
│   └── PresentIntakeLayoutTests.cs       # NEW — bUnit: three layouts + fallback branches
└── Services/Device/
    └── DeviceProfileProbeTests.cs        # NEW — classification + IntakeMode derivation

tests/Sorcha.UI.E2E.Tests/
└── Docker/
    └── PresentIntakeLayoutTests.cs       # NEW — Playwright: per-variant intake behaviour
```

**Structure Decision**: Single Blazor WASM app project (`Sorcha.Wallet.Pwa`). The change is
localised to the Present page plus a new, narrowly-scoped device-profile seam (service interface +
JS helper + model) so the classification logic is unit-testable in isolation from the page and the
camera bridge. Presentation flow components (`ConsentSheet`, `CredentialPickerDialog`,
`NoMatchingCredentialDialog`) in `Sorcha.UI.Components.User` and the engine/models
(`IPresentationEngine`, `ICredentialCache`, `ParsedPresentationRequest`) are reused unchanged — this
feature only adds the intake-layout branch in front of the existing `ParseAsync()` entry point.

## Complexity Tracking

*No constitution violations — section intentionally left empty.*
