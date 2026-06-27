# Implementation Plan: Wire both hosts onto the shared verify control + live HAIP transport (PR B3, relaunch)

**Branch**: `164-verify-host-rewire` | **Date**: 2026-06-26 | **Spec**: [`spec.md`](./spec.md)

**Input**: Feature specification from `specs/164-verify-host-rewire/spec.md`

**Parent design**: `docs/superpowers/specs/2026-06-25-verify-unification-design.md` (stage B3) — *not present in this worktree (see Worktree Staleness below); planned from the spec.*

## Summary

After B1 (#1044) and B2 (#1045 foundation, #1048 components), the platform has **one shared verify
control** in `Sorcha.UI.Components.User` (question selector → request-QR + poll → 4-layer verdict trail)
that computes a rich client-side verdict — but it is wired only to the `NotConfiguredVerificationTransport`
**stub** and is not mounted on any host route. The two real verify surfaces still run divergent legacy
machinery: the PWA's paste-based `VerifyFlow` and the desk app's bespoke
`PresentationRequestBuilder` / `InMemoryVerifierSessionStore` / `/r/{sessionId}/response`+`/status` callback
/ `Outcome.razor`.

This wave delivers a single load-bearing slice plus two host rewires and a retirement pass:

1. **A live `HaipVerificationTransport`** (in the shared user library) backed by the existing HAIP verifier
   endpoints — create presentation request, render QR deep-link, poll, and **return the raw `vp_token`**
   (B1's additive change) for client-side verdict computation. This single implementation serves both hosts.
2. **Both hosts register the live transport in place of the stub** — the headline, explicitly-asserted
   acceptance condition (the documented B2 failure mode was a host that mounted the control but never
   overrode the stub).
3. **PWA `/wallet/verify`** and the **desk `Sorcha.Verifier`** mount the same shared control; the only
   per-host difference is the injected verifier identity (PWA = ephemeral P-256; desk = stable org).
4. **The legacy paths are retired** once both hosts are live, leaving exactly one verify code path.

HAIP's server-side validation behaviour is untouched (it keeps serving blueprint automation via
`HaipPresentationConsumer`); the human-verifier verdict stays client-side in the shared control.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**:
- `Sorcha.UI.Components.User` (shared Razor library; B2 seams `IVerificationTransport`,
  `IVerificationPresetCatalogue`, `DefaultPresetCatalogue`, `VerificationPreset`,
  `NotConfiguredVerificationTransport`; B2 components `QuestionSelectionPanel`, `VerificationSessionQr`,
  `VerdictTrailPanel`; relocated `VerdictViewModel`, `IRegisterAnchorClient`)
- `IVerifiablePresentationValidator` (`Sorcha.Verifier.Engine`, WASM-safe) for the client-side 4-layer verdict
- HAIP verifier HTTP endpoints in `Sorcha.Haip.Service` (`/api/v1/verifier/requests*`, including B1's
  `vp_token`-returning result poll)
- MudBlazor (existing UI), `Sorcha.ServiceClients` for typed HTTP, `IEphemeralVerifierIdentityService`
  (PWA, WebCrypto P-256), desk stable-org identity (`did:sorcha:verifier:{orgId}`)

**Storage**: No new server-side storage. The verify session lives in HAIP's existing
`PresentationRequestStore`; the host-local `InMemoryVerifierSessionStore` is **removed**. The PWA's
per-device `IVerificationHistoryStore` is retained (out of B3 scope, keeps working).

**Testing**: xUnit + FluentAssertions + Moq; bUnit for component-mount/DI-resolution assertions where the
host test project supports it. Existing projects: `Sorcha.Wallet.Pwa.Tests`, `Sorcha.Verifier.Tests`,
`Sorcha.Haip.Service.Tests`. A new `Sorcha.UI.Components.User.Tests` project is referenced by B2's
`InternalsVisibleTo` — create/extend it for the transport + control tests.

**Target Platform**: Blazor WASM (Citizen Wallet PWA) + Blazor desk app (`Sorcha.Verifier`). All B3 code on
the shared-control path **MUST be WASM-safe** (no server-only dependencies).

**Project Type**: Web / Blazor application (two host apps + shared component library + one backing service).

**Performance Goals**: Poll cadence bounded and cancellable (target ≤2 s interval, configurable); QR render
on create with no perceptible blocking; verdict computed client-side without a server round trip beyond the
poll.

**Constraints**: Single shared transport for both hosts; per-host variation limited to the injected verifier
identity provider; client-side verdict only (no new server-side verdict surface); polling lifecycle must be
cancellable and self-disposing (no leaked timers); explicit recoverable states for waiting / expiry /
tier-error / validation-fail.

**Scale/Scope**: 2 host routes, 1 shared control, 1 transport, 1 backing service (consumed, not modified).
~1 new transport class + DI overrides in 2 hosts + 2 route rewires + ~6 legacy types/endpoints retired.

### Worktree Staleness (load-bearing planning note)

Per the spec's Assumptions: **B1/B2 are merged on `master`; B3 branches from that state.** This worktree's
local HEAD predates them — an exploration confirmed the B2 shared types
(`IVerificationTransport`, `IVerificationPresetCatalogue`, `DefaultPresetCatalogue`, `VerificationPreset`,
`NotConfiguredVerificationTransport`, the three panels, the relocated `VerdictViewModel`) and B1's
`vp_token`-returning HAIP poll are **absent locally**. This is a worktree-staleness artifact, **not** a scope
question. Implementation begins by rebasing/branching from the merged `master` so those surfaces are present;
the plan targets that merged state. If, when implementation starts, any B1/B2 surface is genuinely missing
from `master`, that is a blocker to raise — not work to re-do inside B3.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
|---|---|---|
| I. Microservices-First | B3 adds no upward service dependency. The new transport is an HTTP client from a UI library to the existing HAIP service; no new cross-service coupling. Retiring the desk-local callback endpoints **reduces** divergence. | PASS |
| II. Security First | Verifier token tier enforced per host (consumer/desk); audience binding preserved; verdict computed client-side from the returned `vp_token`; no secrets added. Tier allowance confirmed against a live node (FR-008). | PASS |
| III. API Documentation | B3 **consumes** existing HAIP endpoints (no new server endpoints) and **removes** desk-local endpoints. No new public REST surface; XML docs required on the new transport's public members. | PASS |
| IV. Testing (>85% new code) | New transport, DI-resolution assertion, control-mount, cancellation, and error-state tests planned across the four test projects. | PASS |
| V. Code Quality | Nullable enabled; async I/O; no new warnings; DI-driven. | PASS |
| VI. Blueprint Standards | N/A — no blueprint changes. | PASS |
| VII. Domain-Driven Design | Uses verify ubiquitous language (verification session, preset/question, verifier identity, verdict trail). | PASS |
| VIII. Observability | Transport surfaces terminal error states; emits structured logs on create/poll/fault (no string-interpolated logs). | PASS |

**Result**: No violations. Complexity Tracking left empty.

## Project Structure

### Documentation (this feature)

```text
specs/164-verify-host-rewire/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── IVerificationTransport.md   # transport seam contract (consumed from B2)
│   └── haip-verifier-endpoints.md  # HAIP endpoint contract the transport binds to
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/        # shared user component library (B2 home of the control)
├── Services/User/Verification/
│   ├── IVerificationTransport.cs                     # (B2) seam — consumed, not changed
│   ├── NotConfiguredVerificationTransport.cs         # (B2) stub — stays as the library default
│   └── HaipVerificationTransport.cs                  # NEW (US1): live HAIP-backed transport
├── Components/Verify/
│   ├── QuestionSelectionPanel.razor                  # (B2) consumed
│   ├── VerificationSessionQr.razor                   # (B2) consumed
│   ├── VerdictTrailPanel.razor                       # (B2) consumed
│   └── VerifyFlow.razor                              # LEGACY — removed in US4
├── Models/Verification/                              # VerificationPreset, VerdictViewModel (B2) consumed
└── Extensions/                                        # B2 library DI extension (registers the stub default)

src/Apps/Sorcha.Wallet.Pwa/                           # Citizen Wallet PWA host
├── Pages/Verify.razor                                # REWIRED (US2): mount shared control, drop VerifyFlow
├── Program.cs                                         # OVERRIDE DI: HaipVerificationTransport + ephemeral identity
└── Services/Signing/EphemeralVerifierIdentityService.cs  # existing ephemeral P-256 identity (reused)

src/Apps/Sorcha.Verifier/                             # desk Verifier host
├── Components/Pages/Verify*.razor                    # REWIRED (US3): mount shared control
├── Program.cs / Extensions/ServiceCollectionExtensions.cs  # OVERRIDE DI: HaipVerificationTransport + stable org identity
├── Services/PresentationRequestBuilder.cs            # LEGACY — removed in US4
├── Services/InMemoryVerifierSessionStore.cs          # LEGACY — removed in US4
├── Endpoints/PresentationResponseEndpoints.cs        # LEGACY (/r/{id}/response + /status) — removed in US4
├── Components/Pages/Outcome.razor                    # LEGACY — removed in US4
└── Services/VerdictViewModel.cs, IRegisterAnchorClient.cs  # de-duplicated against shared lib in US4

src/Services/Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs  # consumed (B1 vp_token poll); NOT modified

tests/
├── Sorcha.UI.Components.User.Tests/                  # NEW/extended: transport + DI-resolution + control tests
├── Sorcha.Wallet.Pwa.Tests/                          # US2 route/DI tests
├── Sorcha.Verifier.Tests/                            # US3 route/DI + retirement tests
└── Sorcha.Haip.Service.Tests/                        # US1 transport round-trip against HAIP (tier acceptance)
```

**Structure Decision**: Existing Blazor multi-host layout. The single new production type
(`HaipVerificationTransport`) lives in the shared library so both hosts share one implementation; the only
per-host code is the DI override (transport + identity provider) and the route rewire. Retirements are
deletions confined to the two host apps and the legacy component in the shared library.

## Complexity Tracking

> No constitution violations — no entries required.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
