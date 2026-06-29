# Implementation Plan: Fix "Verification Not Configured" False Error

**Branch**: `174-fix-haip-verify-auth` | **Date**: 2026-06-29 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/174-fix-haip-verify-auth/spec.md`

## Summary

A user-facing verification surface fails because it calls a **service-to-service** verifier
endpoint with a non-service credential, and the failure is swallowed into a state that looks
like "verification isn't here." The fix routes the UI's verification calls through the
already-correct **user-authenticated `/api/v1/presentations/*` BFF surface** on Blueprint
Service (which holds the service identity to call the HAIP verifier downstream), and makes the
transport surface real failures into the existing error/retry presentation path instead of
collapsing them to a silent `null`/empty state.

> **Important — spec/code divergence (resolved in [research.md](./research.md)).** The spec's
> Input names artifacts that do **not** exist in this codebase (`IHaipVerifierClient`,
> `HaipVerificationTransport`, a `NotConfigured` stub transport, the literal string
> "Verification is not yet configured here", and an `AddSorchaUserComponents` that wires the
> verification client). `AddSorchaUserComponents` is an empty stub. The spec's Story 3
> mechanism — attach the **user/holder** token to the verifier request — is **technically
> unsatisfiable** because the verifier endpoints are `RequireService` (SEC-013). This plan
> preserves the spec's **intent** (Stories 1/2/3, all FRs, all success criteria) and maps it
> onto the real architecture. The user declined to override these defaults when asked, so the
> recommended defaults stand: **(1) route the UI through the `/presentations` BFF rather than
> relax endpoint auth**, and **(2) plan against the real code while documenting the naming
> divergence**.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: Blazor WebAssembly (Sorcha.UI.Web.Client), MudBlazor; Blazor/Capacitor
PWA (Sorcha.Wallet.Pwa); ASP.NET Core Minimal APIs (Blueprint Service, HAIP Service);
`Sorcha.ServiceClients.Http` (typed clients, `ServiceAuthClient` client-credentials);
`AuthenticatedHttpMessageHandler` (web), `BearerTokenHandler` + `ServerClockHandler` (PWA).

**Storage**: N/A for this fix (no schema/persistence change). Presentation requests are stored
server-side by `PresentationRequestStore` in HAIP Service (unchanged).

**Testing**: xUnit + FluentAssertions + Moq (component/service tests); existing HAIP/Blueprint
endpoint integration tests. Playwright optional for the QR surface (see quickstart).

**Target Platform**: Browser WASM (web client), Capacitor/PWA (wallet), Linux service hosts.

**Project Type**: Web application (multiple front-end hosts + backend services). No new project.

**Performance Goals**: No new perf target. Preserve the existing HAIP polling cadence
(`HaipPollingDefaults.PollInterval` / `MaxPollTicks`); do not introduce retry storms (Edge Case
+ FR — bounded retry, no UI lock).

**Constraints**: Do **not** relax verifier endpoint authorization (spec assumption #105 / SEC-013).
Do **not** place a service credential in a public (browser/PWA) client. Reuse existing per-host
credential mechanisms — invent no new auth. No build warnings (Constitution V).

**Scale/Scope**: A bounded UI + transport fix. Primary locus: the web QR verification surface
(`PresentationRequestQrCard.razor` + `HaipOfferService`/`IHaipOfferService`) and the error/retry
surfacing in `Sorcha.UI.Components.User`. Service endpoints and the HAIP protocol are unchanged.

### Resolved unknowns

| Spec assumption / term | Reality (see research.md) | Resolution |
|---|---|---|
| `IHaipVerifierClient` typed client | Not present. Closest: `IHaipServiceClient` (service-token, backend) and `IHaipOfferService` (web, user-token, **already** has `AuthenticatedHttpMessageHandler`). | Treat `IHaipOfferService`/QR card as the real surface; re-point it at the BFF. |
| `HaipVerificationTransport` real transport + `NotConfigured` stub | Neither exists. `HaipOfferService` swallows non-success → `null`; the QR card then polls forever (no error state). | Add explicit error/retry state to the existing surface; no stub transport needed. |
| Attach user/holder token to verifier request (Story 3) | Verifier endpoints are `RequireService`; a user token is rejected. Blueprint `/api/v1/presentations/*` is the user-authenticated wrapper that calls the verifier with a **service** token. | Web/PWA attach their existing user/holder creds to the **`/presentations` BFF**; the service identity stays server-side (FR-004 satisfied by Blueprint→HAIP, already working). |
| `AddSorchaUserComponents` wires the client | It is an empty stub, called nowhere. | Out of scope to repurpose; FR-010 satisfied by fixing the existing `AddCoreServices` registrations + the QR card. (If the team later adopts the shared-components path, the same registration applies there.) |
| "Verification is not yet configured here" string | Not in the codebase. | Treat as the *symptom class* (silent no-progress / empty state), not a literal string to find. |

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| II. Security First (zero-trust, don't weaken boundaries) | **PASS** | Fix keeps verifier endpoints `RequireService`; routes UI through the user-authenticated BFF. No service credential leaks to a public client. Reuses existing handlers. |
| III. API Documentation | **PASS (mostly N/A)** | No new endpoints. Any touched endpoint keeps `.WithSummary()`/`.WithDescription()`; touched public members keep `/// <summary>`. |
| IV. Testing (>85% new code, deterministic) | **PASS** | Plan adds component/service tests for the success path, the error/retry path, and the genuine-not-configured/legitimate path (SC-005). |
| V. Code Quality (nullable, no warnings, async) | **PASS** | Existing async patterns retained; no new warnings. |
| VIII. Observability | **PASS** | Replace silent `LogWarning→null` swallow with a surfaced error state + structured log; no string-interpolated logs. |
| I. Microservices-first | **PASS** | No new cross-service coupling; uses the existing Blueprint→HAIP service boundary. |

No violations → **Complexity Tracking** section omitted (nothing to justify).

## Project Structure

### Documentation (this feature)

```text
specs/174-fix-haip-verify-auth/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output — divergence analysis + decisions
├── data-model.md        # Phase 1 output — verification state model (no DB change)
├── quickstart.md        # Phase 1 output — manual + automated validation guide
├── contracts/           # Phase 1 output — the UI⇄BFF⇄verifier contract this fix relies on
│   └── verification-transport.md
├── checklists/          # Pre-existing
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

Real paths this fix touches or depends on:

```text
src/Apps/Sorcha.UI/
├── Sorcha.UI.Web.Client/
│   ├── Program.cs                                   # AddCoreServices entry; transport selection lives upstream
│   └── Components/Credentials/
│       └── PresentationRequestQrCard.razor          # PRIMARY FIX: polls service-only endpoint; add error/retry state
├── Sorcha.UI.Core/
│   └── Extensions/ServiceCollectionExtensions.cs    # AddCoreServices — IHaipOfferService/IPresentationAdminService typed-client registration (already auth-handlered)
└── Sorcha.UI.Components.User/
    └── Services/User/Credentials/
        ├── HaipOfferService.cs / IHaipOfferService.cs        # swallows failures → null; re-point + surface errors
        └── PresentationAdminService.cs / IPresentationAdminService.cs   # already targets /api/v1/presentations/* (reference for correct pattern)

src/Apps/Sorcha.Wallet.Pwa/
├── Program.cs                                        # BearerTokenHandler + ServerClockHandler typed-client chain
├── Services/BearerTokenHandler.cs / ServerClockHandler.cs
└── Pages/Verify.razor                               # Doorstep VerifyFlow (local IVerifierEngine) — confirm NOT on the broken path

src/Services/Sorcha.Blueprint.Service/
├── Endpoints/PresentationEndpoints.cs               # user-facing /api/presentations/* surface (BFF)
└── Services/Implementation/PresentationLifecycleService.cs  # calls IHaipServiceClient (service token) downstream

src/Services/Sorcha.Haip.Service/
└── Endpoints/VerifierEndpoints.cs                   # /api/v1/verifier/* = RequireService (UNCHANGED — do not relax)

src/Common/Sorcha.ServiceClients.Http/
├── Haip/HaipServiceClient.cs                        # backend service-token client (Blueprint→HAIP)
└── Auth/ServiceAuthClient.cs                        # client-credentials service identity (FR-004 path)
```

**Structure Decision**: Web application with three front-end hosts (web client, wallet PWA,
service-to-service). No new project or layer. The fix is concentrated in the web verification
surface and its transport service inside `Sorcha.UI.Components.User` / `Sorcha.UI.Core`, with the
PWA and service paths verified-correct rather than re-plumbed. The verifier service boundary
(`RequireService`) is held fixed.

## Phase 0 → research.md

All NEEDS CLARIFICATION items (the spec-vs-code divergences above) are resolved in
[research.md](./research.md) with decision/rationale/alternatives.

## Phase 1 → design artifacts

- [data-model.md](./data-model.md) — verification state model (Pending/Submitted/terminal +
  the new **Error/Retry** state) and how an empty result is distinguished from a transport
  failure and from genuinely-not-configured. No persistence change.
- [contracts/verification-transport.md](./contracts/verification-transport.md) — the
  UI ⇄ `/presentations` BFF ⇄ verifier contract the fix depends on, including which calls carry
  which credential and which status codes map to which UI state.
- [quickstart.md](./quickstart.md) — runnable validation for SC-001…SC-005.

## Complexity Tracking

> Constitution Check passed with no violations — no entries required.
