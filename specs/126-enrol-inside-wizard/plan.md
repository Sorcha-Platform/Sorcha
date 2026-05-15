# Implementation Plan: Sorcha Wallet enrolment inside a council application wizard

**Branch**: `126-enrol-inside-wizard` | **Date**: 2026-05-15 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/126-enrol-inside-wizard/spec.md`

## Summary

Take a citizen from "click Apply on a council page" to "form ready, account + wallet ready to receive credentials" — in one continuous experience, with no separate "set up a wallet first" detour. Three citizen tiers (cold-start, mini-gate, fast-path) detected transparently from `/whoami` + `/me/devices` probes; the cold-start tier walks the citizen through Feature 116 signup, then a hybrid QR/link/paste affordance opens a one-time-use enrolment URL on the wallet device, which runs the existing Feature 114 device-pairing ceremony with a PWA-side confirmation dialog as the friend-scans-by-mistake mitigation. Cross-device coordination uses the existing TenantHub (Feature 118) with `DeviceEnrolled` as a new event; polling on `/me/devices` is the fallback when the hub doesn't connect.

Technical approach is **library-first**: a single new `EnrolGateComponent` in `Sorcha.UI.Components.User` handles all three tiers internally, so any council page can host the gate with one element. Server-side surface is small — two new endpoints + one new hub event + a `?returnTo=` extension on existing F116 signup. Reuses everything Spec 1 + Spec 2 already shipped.

## Technical Context

**Language/Version**: .NET 10 / C# 14 (per project constitution + active Sorcha CLAUDE.md; constitution table is slightly stale at C# 13, not a violation).
**Primary Dependencies**: ASP.NET Core Minimal APIs (server), Blazor WebAssembly (PWA shell), Blazor Server-shell (council page in `Sorcha.UI.Web`), MudBlazor (UI primitives), Microsoft.AspNetCore.SignalR (`TenantHub` from Feature 118), Microsoft.AspNetCore.SignalR.Client (council page + PWA subscriptions), `Sorcha.UI.Components.User` (existing shared component library), QRCoder (server-side QR rendering — already used by `Sorcha.Verifier`).
**Storage**: Redis via `IDistributedCache` for the one-time session-token JTI registry (single-use enforcement; ≤10 min TTL aligns with the existing atomic-distributed-cache pattern from Feature 113). No new EF entities — `PlatformUserDevice` and `PlatformUser` already exist (F114 + F116).
**Testing**: xUnit + FluentAssertions + Moq for unit tests across `Sorcha.Tenant.Service.Tests`, `Sorcha.Wallet.Pwa.Tests`, `Sorcha.UI.Core.Tests`. Playwright under `tests/Sorcha.UI.E2E.Tests/Docker/Enrolment/` for the cold-start cross-device walk and the friend-scans-by-mistake confirmation.
**Target Platform**: Linux containers for Tenant Service + dependants; Mobile Safari + Chrome PWA for the wallet; desktop browsers for the council page.
**Project Type**: Web — multi-service backend + Blazor WASM PWA + Blazor Server-shell council page. Existing Sorcha multi-project layout.
**Performance Goals**:
- Tier-detection probes (`/whoami` + `/me/devices`) complete in under 200 ms p95.
- Pairing-completion signal reaches the council page within 2 s of pairing in 95% of attempts (SC-004 / FR-014).
- Cold-start journey from "click Apply" to "form ready" completes in under 90 s in 95% of attempts (SC-001).
- Session-token mint + redeem each under 300 ms p95.
**Constraints**:
- Zero regression in Feature 124 + Feature 125 test suites (SC-009 baseline).
- No new microservice — surface fits in `Sorcha.Tenant.Service` + `Sorcha.UI.Components.User` + small additions to `Sorcha.UI.Web` + `Sorcha.Wallet.Pwa`.
- Session token MUST be one-time-use; replay MUST fail closed.
- Return-to redirect MUST validate against an allowlist; open redirects MUST fail closed.
**Scale/Scope**: One council (Strathcarron) in v1. Concurrent enrolments expected in the dozens — well within existing platform limits. Polling fallback uses existing `/me/devices` endpoint with the existing rate limits.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new service. Tenant Service gets two endpoints + one hub event; existing dependency chain unchanged. EnrolGateComponent lives in the shared library, consumed by the web shell — dependencies flow downward only. |
| II. Security First | PASS | Session token is a signed JWT (Tenant Service signing key, same as existing auth JWTs). One-time-use via Redis `SET NX` on JTI. Confirmation dialog before redeem mitigates friend-scans-by-mistake. Return-to allowlist prevents open redirects. All new inputs validated via FluentValidation. Token never logged. |
| III. API Documentation | PASS | Two new endpoints use Scalar + `.WithName` / `.WithSummary` / `.WithDescription` / `.Produces` per existing pattern. OpenAPI contract committed to `contracts/enrol-session.openapi.yaml`. |
| IV. Testing Requirements | PASS | Target >85% on new code. Unit tests for `EnrolSessionService.MintAsync` + `RedeemAsync`, integration tests for the two endpoints, component tests for `EnrolGateComponent`, Playwright E2E for the three citizen tiers + the friend-scans-by-mistake mitigation. xUnit + FluentAssertions + Moq throughout. All deterministic. |
| V. Code Quality | PASS | C# 14, async/await, DI, nullable enabled. No new compiler warnings. |
| VI. Blueprint Creation Standards | N/A | No new blueprints. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: Citizen, Wallet, Device, Pairing. New terms — "enrolment session token", "citizen tier" — consistent with existing PlatformUser / PlatformUserDevice vocabulary. |
| VIII. Observability by Default | PASS | OpenTelemetry counters: `sorcha_enrol_session_minted_total`, `sorcha_enrol_session_redeemed_total{outcome ∈ {success, expired, replay, scope_mismatch}}`, `sorcha_enrol_pairing_signal_latency_seconds` histogram. Structured logs on all new server-side surfaces. Health checks unaffected. |

**Verdict**: No violations. Complexity Tracking section omitted.

## Project Structure

### Documentation (this feature)

```text
specs/126-enrol-inside-wizard/
├── plan.md              # This file (/speckit.plan command output)
├── spec.md              # Feature spec (already in place)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── enrol-session.openapi.yaml      # Phase 1 output — new server-side contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (already in place)
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

Multi-project Sorcha layout. Feature touches four areas:

```text
src/
├── Services/
│   └── Sorcha.Tenant.Service/
│       ├── Endpoints/
│       │   └── EnrolSessionEndpoints.cs            # NEW — POST /api/auth/enrol-session, POST /api/auth/enrol-session/redeem
│       ├── Services/
│       │   ├── IEnrolSessionService.cs             # NEW — interface
│       │   ├── EnrolSessionService.cs              # NEW — mint + redeem implementation
│       │   └── EnrolSessionMetrics.cs              # NEW — OpenTelemetry meters
│       ├── Models/
│       │   ├── EnrolSessionDtos.cs                 # NEW — request/response shapes
│       │   └── ReturnToAllowlistOptions.cs         # NEW — configuration shape
│       ├── Hubs/
│       │   ├── TenantHub.cs                        # EXTEND — add DeviceEnrolled event method
│       │   ├── ITenantHubClient.cs                 # EXTEND — add DeviceEnrolled client method
│       │   └── TenantHubGroups.cs                  # CHECK — User(platformUserId) group already exists per F118 conventions
│       ├── Services/PlatformUserDeviceService.cs   # EXTEND — fire DeviceEnrolled on RegisterAsync success
│       └── Endpoints/AuthEndpoints.cs              # EXTEND — ?returnTo= query param + allowlist validation on signup
│
├── Apps/
│   ├── Sorcha.UI/
│   │   └── Sorcha.UI.Components.User/
│   │       ├── Components/
│   │       │   └── EnrolGate/
│   │       │       ├── EnrolGateComponent.razor          # NEW — top-level gate; tier detection + branching
│   │       │       ├── PreflightSignupSurface.razor      # NEW — Tier 3 signup explainer
│   │       │       ├── WalletPairingSurface.razor        # NEW — Tier 2 / Tier 3 post-signup
│   │       │       └── HybridQrAffordance.razor          # NEW — QR + tap link + copy link, shared across surfaces
│   │       └── Services/
│   │           └── User/
│   │               └── Enrolment/
│   │                   ├── ITierProbeService.cs          # NEW — wraps /whoami + /me/devices probes
│   │                   ├── HttpTierProbeService.cs       # NEW — production impl
│   │                   ├── IEnrolPairingSignal.cs        # NEW — abstracts SignalR + polling fallback
│   │                   └── EnrolPairingSignal.cs         # NEW — uses TenantHubConnection + IPollingFallback
│   │
│   ├── Sorcha.UI/Sorcha.UI.Web.Client/
│   │   └── Pages/
│   │       └── CouncilApplication.razor             # NEW (or extension) — example council application page that hosts EnrolGateComponent
│   │
│   └── Sorcha.Wallet.Pwa/
│       ├── Pages/
│       │   └── Enrol.razor                          # EXTEND — accept ?session= query, swap for full JWT via redeem endpoint
│       ├── Components/
│       │   └── EnrolmentRedeemConfirmDialog.razor   # NEW — confirmation surface before redeem
│       └── Services/
│           └── Enrolment/
│               ├── IEnrolSessionRedeemer.cs         # NEW — interface for the redeem HTTP call
│               └── EnrolSessionRedeemer.cs          # NEW — implementation

tests/
├── Sorcha.Tenant.Service.Tests/
│   ├── Services/
│   │   └── EnrolSessionServiceTests.cs                  # NEW — mint claims, redeem single-use, expired/scope-mismatch rejection
│   ├── Endpoints/
│   │   └── EnrolSessionEndpointsTests.cs                # NEW — happy path, replay 409, expired 410
│   └── Hubs/
│       └── TenantHubDeviceEnrolledTests.cs              # NEW — group filtering + payload shape
├── Sorcha.UI.Core.Tests/
│   ├── Components/
│   │   └── EnrolGate/
│   │       ├── EnrolGateComponentTests.cs               # NEW — tier rendering, hub-event transition, polling fallback
│   │       └── HybridQrAffordanceTests.cs               # NEW — mobile prominence, three resolution paths
│   └── Services/
│       └── User/
│           └── Enrolment/
│               └── EnrolPairingSignalTests.cs           # NEW — SignalR-or-polling, manual recovery after 60 s
├── Sorcha.Wallet.Pwa.Tests/
│   ├── Components/
│   │   └── EnrolmentRedeemConfirmDialogTests.cs         # NEW — renders bound email + name, cancel leaves no side-effects
│   └── Services/
│       └── Enrolment/
│           └── EnrolSessionRedeemerTests.cs             # NEW — happy path, already-used handling, expired handling
└── Sorcha.UI.E2E.Tests/Docker/Enrolment/
    ├── ColdStartEnrolmentTests.cs                       # NEW — [Demo("cold-start-enrolment")] — full Tier 3 walk
    ├── ReturningCitizenFastPathTests.cs                 # NEW — Tier 1 verification
    ├── MiniGateEnrolmentTests.cs                        # NEW — Tier 2 verification
    └── StrangerScansQrTests.cs                          # NEW — friend-scans-by-mistake mitigation

walkthroughs/Strathcarron/
└── setup-cold-start-demo.ps1                            # NEW — pre-creates Strathcarron council org + a reset-able fresh-citizen email
```

**Structure Decision**: Existing Sorcha multi-project layout, web-application shape. Library-first growth: `EnrolGateComponent` in `Sorcha.UI.Components.User` is the contract any council page consumes. Server surface lives in `Sorcha.Tenant.Service` per locked decision #2 from the brainstorm. No new project required.

## Complexity Tracking

No constitution violations. Section omitted.
