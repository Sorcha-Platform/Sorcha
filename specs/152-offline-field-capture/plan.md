# Implementation Plan: PWA Offline / Field Capture

**Branch**: `152-offline-field-capture` | **Date**: 2026-06-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/152-offline-field-capture/spec.md`

**Source design**: `docs/superpowers/specs/2026-06-13-pwa-offline-field-capture-design.md`

**Depends on**: sub-project A (`151-citizen-workflow-inbox`) — reuses `IMyActionsClient`,
`Actions.razor`, `ApplicationInstance`, and the shared `SorchaFormRenderer`.

## Summary

Make Citizen Wallet PWA workflow participation field-first: a consumer-tier citizen can open any of
their pending actions **offline**, fill them, **capture photos**, save an **encrypted local draft**
(autosave/resume), and have the PWA **submit automatically on reconnect** with a **detect/hold/ask**
conflict path. Reuses the proven XChaCha20-Poly1305/IndexedDB device-bound encryption, the capture
controls (`FileRenderer`/`PortraitCaptureControl`), `ISyncService` as a drain trigger, and the
existing platform attachment mechanism (inline `Files` → `BuildFileTransactionsAsync` / `/api/file-
chunks`). New PWA infrastructure: a draft store, an action-context pre-cache, a submit-queue outbox,
a conflict classifier, and offline/queued status UI. The only backend-touching slice is US5 (routing
the citizen submit path through the existing attachment mechanism).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blazor WebAssembly PWA); JS interop for IndexedDB + AEAD.

**Primary Dependencies**: `Sorcha.Wallet.Pwa`; shared `Sorcha.UI.Components.User` (form renderer,
capture controls); existing `indexeddb-bridge.js` + `xchacha-bridge.js` (XChaCha20-Poly1305,
device-bound key); `ISyncService` (drain trigger); A's `IMyActionsClient` / `ApplicationInstance`;
the Blueprint Service attachment mechanism (`Files` / `BuildFileTransactionsAsync` / `/api/file-
chunks`) for US5.

**Storage**: Device-local **IndexedDB** — new `drafts` and `submitQueue` stores + an
action-context cache store, all encrypted at rest (XChaCha20-Poly1305, device key). No server-side
draft storage.

**Testing**: xUnit + bUnit (`JSRuntimeMode.Loose`) in `tests/Sorcha.Wallet.Pwa.Tests`; mock the
IndexedDB JS-interop seam and the submit delegate. US5 attachment wiring, if it extends the execute
path, gets a Blueprint Service test in `tests/Sorcha.Blueprint.Service.Tests`.

**Target Platform**: Blazor WASM PWA at `/wallet/` (consumer tier).

**Project Type**: PWA front-end + one backend-touching slice (US5 attachment submission).

**Performance Goals**: Open a prepared action and autosave a draft with no perceptible lag offline;
queue flush completes promptly on reconnect; capture/persist a photo without blocking the form.

**Constraints**: Consumer-tier only; drafts encrypted at rest (constitution §II); device-bound (lost
on device loss — messaged honestly); base-relative navigation under `/wallet/`; no `ISnackbar`
(Pattern #12); foreground-only drain (no Background Sync API); reuse the existing attachment
mechanism rather than inventing one.

**Scale/Scope**: ~3 new PWA services (`IDraftStore`, `ISubmitQueue`, `IActionContextCache`) + a
conflict classifier + a connectivity signal + status UI + draft/queue integration into A's pages;
one IndexedDB schema bump; one backend-touching slice (US5).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies? | Status |
|-----------|----------|--------|
| I. Microservices-First | US5 may touch Blueprint Service | ✅ PASS — reuses existing attachment seam; no new service or upward dependency |
| II. Security First | **Yes — at-rest encryption** | ✅ PASS — drafts/media/queue encrypted with XChaCha20-Poly1305 device key (reuses credential-cache pattern); consumer-tier; no secrets committed; idempotency prevents double-submit |
| III. API Documentation | US5 only if execute path changes | ✅ — if `/execute` is extended to honor `Files`, update its OpenAPI summary/description + XML docs; otherwise N/A |
| IV. Testing (>85% new code) | Yes | ✅ PLANNED — store/queue/classifier/cache unit-tested; bUnit for page integration; TDD ordering |
| V. Code Quality | Yes | ✅ PLANNED — nullable, async I/O, DI, no warnings; follow existing PWA service patterns |
| VI. Blueprint Standards | No | ✅ N/A |
| VII. Domain-Driven Design | Yes | ✅ PASS — Action/Participant/Instance terms; "draft/queue/prepared action" are client concepts over them |
| VIII. Observability | Front-end + US5 | ✅ PASS — structured logging (no string interpolation); US5 backend changes keep existing telemetry |

**Result**: PASS. No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/152-offline-field-capture/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/consumed-and-touched-endpoints.md
├── checklists/requirements.md
└── tasks.md   # /speckit.tasks output
```

### Source Code (repository root)

```text
src/Apps/Sorcha.Wallet.Pwa/
├── Services/Drafts/                 # NEW — IDraftStore + Indexed-DB-backed impl + models
├── Services/Drafts/ISubmitQueue.cs  # NEW — outbox queue + drainer
├── Services/Drafts/IActionContextCache.cs  # NEW — pre-cache of pending action contexts
├── Services/Drafts/SubmitConflictClassifier.cs  # NEW — pure server-outcome → conflict mapping
├── Services/IConnectivity.cs        # NEW — online/offline signal (navigator.onLine + events)
├── Pages/Actions.razor              # MODIFY (A) — draft/queue/needs-attention badges per row
├── Pages/ApplicationInstance.razor  # MODIFY (A) — load-from/save-to draft; offline open; capture persist
├── wwwroot/js/indexeddb-bridge.js   # MODIFY — add drafts / submitQueue / actionContext stores (schema bump)
├── Services/ISyncService.cs         # MODIFY — drain the submit queue on sync/reconnect
└── Program.cs / Extensions/ServiceCollectionExtensions.cs  # MODIFY — DI registrations

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/
├── Forms/Controls/FileRenderer.razor, Capture/PortraitCaptureControl.razor  # REUSE (capture)
└── Forms/SorchaFormRenderer.razor   # REUSE (unchanged)

src/Services/Sorcha.Blueprint.Service/   # US5 ONLY — route the citizen submit path through the
                                          # existing Files/BuildFileTransactionsAsync mechanism
tests/
├── Sorcha.Wallet.Pwa.Tests/Drafts/ … bUnit + service tests
└── Sorcha.Blueprint.Service.Tests/ …   # US5 attachment-on-execute test (if execute path extended)
```

**Structure Decision**: PWA-contained for US1-US4 (drafts, pre-cache, queue, conflict), reusing A's
pages and the shared capture controls. US5 is the single backend-touching slice, reusing the existing
attachment mechanism. The exact attachment-wiring choice (extend `/execute` to honor `Files` vs.
submit through the Files-aware endpoint) is resolved in research.md.

## Complexity Tracking

> No Constitution Check violations. Section intentionally empty.
