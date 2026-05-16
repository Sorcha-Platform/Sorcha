# Implementation Plan: Cold-start onboarding and device pairing UX

**Branch**: `128-cold-start-onboarding` | **Date**: 2026-05-16 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/128-cold-start-onboarding/spec.md`

## Summary

Adds four citizen onboarding routes outside the F126 council-page gate by extending the existing `enrol-session` token primitive with a `mode` discriminator (`gated` vs `standalone`), introducing a 6-digit short-code transport for cross-device-to-this-device handoff, and shipping three new UX surfaces:

1. A full-page **PairingTakeover** on the wallet PWA (Story 1, P1) — blocks the wallet until the signed-in citizen pairs this device.
2. A tier-detected **/setup/add-device handoff page** on Sorcha Web (Stories 2 + 3, P1/P2) — auto-routed after first signup, also reachable from a persistent banner + "Add my phone" menu entry.
3. A **/get marketing landing** on sorcha.dev (Story 4, P3) — graceful entry for app-store-discovered visitors.

All four routes share the single token primitive; no parallel pairing-token shapes are introduced. The F126 council-page flow is preserved back-compatibly (`mode=gated` is the default and existing callers do not pass `mode`).

Cryptographic device-pairing (F114 holder-key + per-device delegation) and the account model (F116) are unchanged — this feature adds *entry points* into existing ceremonies.

## Technical Context

**Language/Version**: C# 14 on .NET 10
**Primary Dependencies**: ASP.NET Core Minimal APIs, Blazor WASM 10, MudBlazor (existing UI library), SignalR (F126 `TenantHub.DeviceEnrolled` event), `Sorcha.AtomicCache` (`IAtomicDistributedCache` for nonces + short-code store), `Sorcha.ServiceDefaults.Hubs.AddSorchaHub<>` for the hub backplane, `Sorcha.Tenant.Service.Emails` (F112 transactional email facade), QR generation via QRCoder (already in the solution per existing F126 enrol-session pages)
**Storage**: Redis (short-code → enrol-session-token mapping, ~5 min TTL, single-use via NonceStore pattern); PostgreSQL (existing `PlatformUserDevice` table — no schema change, this feature only reads via a new aggregate endpoint)
**Testing**: xUnit + FluentAssertions + Moq for service-level tests; bunit for Blazor component tests; Playwright (existing Sorcha.UI test infrastructure) for E2E coverage of all four routes
**Target Platform**: Blazor WASM clients (`Sorcha.UI.Web.Client`, `Sorcha.Wallet.Pwa`) running in modern mobile + desktop browsers; .NET 10 server hosts (`Sorcha.Tenant.Service`, `Sorcha.UI.Web` host); deployed to n1.sorcha.dev under existing docker-compose orchestration
**Project Type**: Microservices web (extends Tenant Service + two Blazor hosts + shared component library)
**Performance Goals**: SC-001 — paired in under 30s on the happy path from PWA launch (no perceptible wait beyond network round-trip for the device-pairing ceremony itself, which is F114-bound). SC-002 — paired phone within 2 min of desktop signup including phone-pickup time
**Constraints**: PWA-installable detection MUST be reliable on iOS Safari 17+/18+ and Chrome on Android (the seamless-install path is the SC-006 50% threshold). Short-code TTL MUST be short enough to keep brute-force search infeasible at the configured rate limit. The `mode` discriminator MUST be enforced server-side at redeem time (no client-trusted coercion). Telemetry MUST distinguish all four routes (FR-053, SC-005)
**Scale/Scope**: Same scale as existing Sorcha platform (single-tenant n1 dev environment today; the auth surfaces are already rate-limited via `RateLimitPolicies.PlatformAuth`). New surface area: 1 endpoint extension (mode field on enrol-session mint), 2 new endpoints (short-code mint, short-code redeem, has-any-device aggregate), 3 new Blazor components (PairingTakeover, HandoffSurface, NagBanner), 1 new Razor page (Sorcha Web /setup/add-device + /get landing), 1 new email template

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Microservices-First | PASS | No new services. Endpoint additions to Tenant Service; component additions to existing Blazor hosts. No upward dependencies. |
| II. Security First | PASS | Single-use + bounded-TTL tokens (FR-050, FR-051). Mode/context mismatch rejected server-side, not coerced (FR-003). Short codes rate-limited via `RateLimitPolicies.PlatformAuth`. Email handoff rate-limited per account + IP (edge case). |
| III. API Documentation | PASS | New endpoints get `.WithSummary()` + `.WithDescription()` for Scalar UI; XML docs on public surfaces. No Swagger. |
| IV. Testing Requirements | PASS | Target >85% on new code: bunit tests for each new component (PairingTakeover, HandoffSurface, NagBanner, etc.), xUnit for service-level (mode/context enforcement, short-code mint/redeem, has-any-device aggregate), Playwright E2E for the four route happy paths + 3 edge cases (mode misuse, replay, expiry). |
| V. Code Quality | PASS | .NET 10 + C# 14 + nullable enabled is current solution baseline. |
| VI. Blueprint Standards | N/A | This feature does not author or modify blueprints. |
| VII. Domain-Driven Design | PASS | Reuses established terminology: "device", "pairing", "enrol session". New term: "pairing short code" — narrow-scope, no domain conflict. |
| VIII. Observability | PASS | FR-053 makes per-route telemetry a first-class requirement (OTel counters + structured-log dimensions on the existing F126 audit). Health checks unaffected. |

**No gate violations. No complexity tracking entries required.**

## Project Structure

### Documentation (this feature)

```text
specs/128-cold-start-onboarding/
├── plan.md              # This file
├── research.md          # Phase 0 output — resolves clarifications around iOS start_url, PWA-installable detection, short-code shape
├── data-model.md        # Phase 1 output — token mode field, short-code entity, paired-device aggregate read
├── contracts/           # Phase 1 output — REST endpoint contracts (OpenAPI fragments) + telemetry event contracts
├── quickstart.md        # Phase 1 output — operator walkthrough of the four routes against n1
├── checklists/
│   └── requirements.md  # /speckit.specify output (already complete)
└── tasks.md             # /speckit.tasks output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Tenant.Service/
├── Endpoints/
│   ├── EnrolSessionEndpoints.cs              # EXTEND: add mode field to mint, mode echo on redeem, mode/context enforcement
│   ├── PairingShortCodeEndpoints.cs          # NEW: POST /api/auth/enrol-session/short-code (mint), POST /api/auth/enrol-session/redeem-short-code
│   └── DeviceAggregateEndpoints.cs           # NEW: GET /api/devices/has-any (signed-in caller, returns { hasAnyDevice, latestEnrolledAt? })
├── Services/
│   ├── EnrolSessionService.cs                # EXTEND: persist mode on session record, enforce at redeem
│   └── PairingShortCodeService.cs            # NEW: 6-digit mint + IAtomicDistributedCache nonce-pattern store + redeem
├── Models/
│   ├── EnrolSessionDtos.cs                   # EXTEND: EnrolSessionMintRequest gets optional Mode field; redeem response gets Mode echo
│   └── PairingShortCodeDtos.cs               # NEW: mint request/response, redeem request/response
└── Emails/Templates/
    └── pairing-resumption.html.scriban       # NEW: "Email me a link" resumption template (Scriban, English copy only)

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
├── Components/Pairing/
│   ├── PairingTakeover.razor                 # NEW: Story 1 — full-page takeover, primary action + short-code sub-affordance
│   ├── PairingHandoffSurface.razor           # NEW: Stories 2/3 — tier-detected QR-or-install variant, common short-code panel
│   └── PairingNagBanner.razor                # NEW: Story 2 fallback — persistent dismissable banner in Sorcha Web
├── Services/User/Pairing/
│   ├── IHasPairedDeviceProbe.cs              # NEW: shared probe interface (web + PWA)
│   ├── HasPairedDeviceProbe.cs               # NEW: GET /api/devices/has-any client, cached, invalidates on DeviceEnrolled hub event
│   ├── IPwaInstallabilityProbe.cs            # NEW: detect beforeinstallprompt + UA signals (Stories 2 vs 3 disambiguation)
│   └── PwaInstallabilityProbe.cs             # NEW: JS interop for beforeinstallprompt; UA fallback for iOS

src/Apps/Sorcha.UI.Web.Client/
├── (mounting changes only — adds PairingNagBanner + HasPairedDeviceProbe DI registration)
└── Program.cs                                # EXTEND: register pairing services

src/Apps/Sorcha.UI.Web/
├── Pages/
│   ├── Setup/AddDevice.cshtml + .cshtml.cs   # NEW: hosts PairingHandoffSurface; reachable from signup callback + /devices "Add my phone" + persistent banner
│   ├── Get.cshtml + .cshtml.cs               # NEW: sorcha.dev/get landing (Story 4)
│   ├── Login.cshtml.cs                       # EXTEND: post-success routing — to /setup/add-device when zero paired devices
│   └── Signup.cshtml.cs                      # EXTEND: post-success routing — to /setup/add-device when zero paired devices (always true on signup)

src/Apps/Sorcha.Wallet.Pwa/
├── Pages/
│   ├── Enrol.razor                           # EXTEND: render copy variant from echoed mode; ignore returnTo on standalone tokens
│   └── (no new pages — PairingTakeover mounts from MainLayout)
├── Shared/MainLayout.razor                   # EXTEND: mount PairingTakeover conditionally outside MudContainer (like WelcomeTakeover today)
└── Program.cs                                # EXTEND: register HasPairedDeviceProbe + PwaInstallabilityProbe

tests/Sorcha.Tenant.Service.Tests/
├── Services/
│   ├── EnrolSessionServiceModeTests.cs       # NEW: mode persistence, mode/context mismatch rejection, mode echo on redeem
│   └── PairingShortCodeServiceTests.cs       # NEW: mint shape (6-digit numeric), TTL, single-use, redeem unwraps to enrol-session
└── Endpoints/
    ├── PairingShortCodeEndpointsTests.cs     # NEW: WebApplicationFactory integration — mint, redeem, rate limit, replay
    └── DeviceAggregateEndpointsTests.cs      # NEW: has-any aggregate auth, zero-device vs paired-device responses

tests/Sorcha.UI.Components.User.Tests/
├── Pairing/
│   ├── PairingTakeoverTests.cs               # NEW: bunit — renders on zero-device, hides on paired, short-code sub-affordance, hub-event dismissal
│   ├── PairingHandoffSurfaceTests.cs         # NEW: bunit — QR variant vs install variant by IPwaInstallabilityProbe verdict, short-code always visible on install variant
│   └── PairingNagBannerTests.cs              # NEW: bunit — shows on zero-device, dismissable per-session, link routes to /setup/add-device
└── Services/User/Pairing/
    └── HasPairedDeviceProbeTests.cs          # NEW: caching, hub-event invalidation, local pair-event invalidation

tests/Sorcha.Wallet.Pwa.Tests/
└── Pages/
    └── EnrolModeCopyTests.cs                 # NEW: bunit — gated vs standalone copy variants, returnTo honored only on gated

tests/Sorcha.UI.Web.Tests/
├── Pages/
│   ├── SetupAddDeviceTests.cs                # NEW: bunit — hosts handoff surface, gates auto-route on signup callback only when zero paired devices
│   └── GetLandingTests.cs                    # NEW: bunit — unauthenticated render, sign-in routes through gate
└── PageModels/
    ├── LoginPostSuccessRoutingTests.cs       # NEW: routes to /setup/add-device when has-any returns false; otherwise honors returnUrl as today
    └── SignupPostSuccessRoutingTests.cs      # NEW: always routes to /setup/add-device after first signup

tests/Sorcha.UI.E2E/  (Playwright)
└── ColdStartOnboarding/
    ├── PwaUnpairedTakeoverE2E.cs             # NEW: Story 1 happy path + remote-pair dismissal
    ├── DesktopToPhoneHandoffE2E.cs           # NEW: Story 2 QR scan path + skip-then-banner + Add-my-phone return
    ├── MobileWebInstallHandoffE2E.cs         # NEW: Story 3 install path + short-code fallback path
    └── ColdLandingE2E.cs                     # NEW: Story 4 unauthenticated landing + sign-in-with-zero-devices routing
```

**Structure Decision**: Microservices web layout — extends the existing `Sorcha.Tenant.Service` for all backend surface area (single existing service owns auth + device records, so no cross-service work needed) and the shared `Sorcha.UI.Components.User` audience-partitioned component library for surfaces consumed by both `Sorcha.UI.Web.Client` and `Sorcha.Wallet.Pwa`. The Razor pages on `Sorcha.UI.Web` host the public landings (sorcha.dev/get + /setup/add-device). The Enrol.razor in `Sorcha.Wallet.Pwa` is extended in-place (no new redeem route — single primitive decision from spec). No new projects, no new services, no schema changes — this stays inside existing boundaries.

## Complexity Tracking

> No Constitution Check violations. No entries required.
