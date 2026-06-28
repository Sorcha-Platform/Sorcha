# Implementation Plan: Fix PWA Consumer-Token Claims

**Branch**: `165-fix-pwa-consumer-claims` | **Date**: 2026-06-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/165-fix-pwa-consumer-claims/spec.md`

## Summary

Three Citizen Wallet PWA surfaces — **Security**, **Devices**, and **Add a phone** — fail to load for real citizens because the consumer-tier token they present does not reliably resolve to the citizen's stable platform identity (`platform_user_id`), so the wallet backend cannot locate the citizen's wallet/device binding.

The Phase 0 research established a decisive fact that **reframes the root cause**: the token minter already adds `platform_user_id` unconditionally for every human-tier token (`TokenService.GenerateUserTokenAsync`, `TokenService.cs:110`), and all interactive issuance paths plus refresh already route through it. The defect is therefore **not** a missing claim in the minting code. It is the combination of:

1. **Deployment lag** — the build running on `n1` predates the Feature 136 minting change, so tokens *issued there* lack the claim; and
2. **An unsafe backend fallback** — the no-fallback device endpoints (`ResolveCitizenContext`, `CitizenWalletEndpoints.cs:587`) fall back from a missing `platform_user_id` to `ClaimTypes.NameIdentifier` (= `sub` = `UserIdentity.Id`, the **org-scoped** user id, `TokenService.cs:105`), which is the **wrong** key for device lookups keyed by the platform user id. The result is a silent empty/mis-bound response — exactly the "fails to load / blank" symptom — rather than a clean degrade.

**Technical approach:** (a) lock the already-correct minting behaviour behind regression tests across every consumer-token issuance and refresh path; (b) harden the citizen identity-resolution seam in the Wallet Service so a token lacking `platform_user_id` recovers the *true* platform user id from `sub` via the identity registry (matching the pattern already used by `PlatformUserDeviceEndpoints.ResolvePlatformUserIdAsync`) instead of mis-binding to the org-scoped id; (c) redeploy to `n1` and verify the three surfaces end-to-end with a real interactive citizen sign-in. No new claim names or token formats are introduced (per spec Assumptions).

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: .NET Aspire 13+, ASP.NET Core Minimal APIs, `System.IdentityModel.Tokens.Jwt`, `Sorcha.ServiceDefaults.Auth` (`SorchaAudiences`, `Tier`, `SorchaIssuer`), `Sorcha.ServiceClients.Http` (`TokenClaimConstants`), Blazor WASM (PWA)

**Storage**: PostgreSQL (Tenant identity registry, Wallet repository). No schema change required — this feature touches token claims and claim-resolution logic, not persisted shape.

**Testing**: xUnit + FluentAssertions + Moq (unit/integration); live interactive verification on `n1` (manual + recorded evidence)

**Target Platform**: Linux containers (services), Blazor WASM PWA (`app.sorcha.wallet`)

**Project Type**: Web — distributed microservices backend + Blazor WASM PWA frontend

**Performance Goals**: No new performance budget. Identity resolution adds at most one indexed identity-registry read on the legacy/degraded-token path only (the common path reads the claim directly).

**Constraints**:
- MUST NOT introduce new claim names or token formats (spec Assumptions).
- MUST preserve the Feature 136 tier boundary: a consumer token stays inert at platform/admin surfaces and continues to omit roles + wallet binding (FR-006).
- MUST degrade — not error — for legacy pre-fix tokens until they expire (FR-007).
- Verification is **interactive on `n1`**, not synthetic-token-only (FR-008, spec Assumptions).

**Scale/Scope**: Small, surgical change. Primary files: `TokenService.cs` (assert-only / no behaviour change expected), `CitizenWalletEndpoints.cs` (`ResolveCitizenContext` hardening), plus regression tests in the Tenant and Wallet test projects. One PWA-surface verification pass (no PWA page rewrite — spec Assumption: "not a rewrite of the pages").

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | ✅ Pass | Change is confined to Tenant (mint) and Wallet (resolve) services; no new cross-service coupling. Identity recovery uses the existing identity registry already referenced by the Tenant device endpoints. |
| II. Security First | ✅ Pass | Strengthens identity resolution and explicitly preserves the tier boundary (FR-006). No secrets touched. The hardened fallback resolves to the *correct* identity, removing a silent mis-binding. |
| III. API Documentation | ✅ Pass | No new endpoints. Touched endpoints keep their `.WithSummary()`/`.WithDescription()`; any changed XML docs on `ResolveCitizenContext`/helpers updated in place. |
| IV. Testing Requirements | ✅ Pass | Net-new regression tests lock minting coverage across all issuance paths + the resolution fallback. Target >85% on changed code; deterministic and isolated. |
| V. Code Quality | ✅ Pass | Nullable enabled; async I/O for the registry lookup; no new warnings. |
| VI. Blueprint Standards | ✅ N/A | No blueprints involved. |
| VII. Domain-Driven Design | ✅ Pass | Uses ubiquitous terms (Participant/citizen, consumer tier). No new domain vocabulary. |
| VIII. Observability | ✅ Pass | Adds structured-log breadcrumbs on the degraded/recovery path (mirrors the existing `RefreshTokenAsync` recovery log at `TokenService.cs:313`). Existing `sorcha_token_minted_total{tier}` / `sorcha_tier_request_rejected_total` metrics already cover minting. |

**Result: PASS — no violations. Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/165-fix-pwa-consumer-claims/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output — root-cause reframing + decisions
├── data-model.md        # Phase 1 output — consumer-token claim set + identity-resolution precedence
├── quickstart.md        # Phase 1 output — n1 interactive verification runbook
├── contracts/           # Phase 1 output
│   ├── consumer-token-claims.md        # Consumer-tier claim contract (Feature 136 conformance)
│   └── citizen-identity-resolution.md  # Backend identity/wallet resolution precedence contract
├── checklists/          # Pre-existing
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── Services/
│   ├── Sorcha.Tenant.Service/
│   │   ├── Services/
│   │   │   ├── TokenService.cs              # Mint (line 110 adds platform_user_id) + RefreshTokenAsync recovery (≈313)
│   │   │   └── LoginService.cs              # Password / org-selection issuance paths
│   │   ├── Pages/Auth/
│   │   │   ├── SocialCallback.cshtml.cs     # Social sign-in (isWallet ⇒ Tier.Consumer)
│   │   │   ├── Login.cshtml.cs              # 2FA completion (Razor)
│   │   │   └── OidcCallback.cshtml.cs       # Enterprise OIDC
│   │   └── Endpoints/
│   │       ├── AuthEndpoints.cs             # Verify2Fa, VerifyPasskey, SwitchOrganization
│   │       ├── PublicPasskeyEndpoints.cs    # Passkey register/assert (consumer hint)
│   │       └── PlatformUserDeviceEndpoints.cs # Reference: identity-registry fallback (ResolvePlatformUserIdAsync ≈215)
│   └── Sorcha.Wallet.Service/
│       └── Endpoints/
│           └── CitizenWalletEndpoints.cs    # ResolveCitizenContext (587) — HARDEN; ResolveCitizenContextAsync (610) — reference
├── Common/
│   ├── Sorcha.ServiceDefaults/Auth/
│   │   └── SorchaAudiences.cs               # Tier→audience mapping (boundary unchanged)
│   └── Sorcha.ServiceClients.Http/Auth/
│       └── TokenClaimConstants.cs           # Optional: add PlatformUserId constant (consistency, no new claim name)
└── Apps/Sorcha.UI/Sorcha.Wallet.Pwa/
    └── Pages/
        ├── Security.razor                   # Surface 1 — mounts shared SecurityHome
        ├── Devices.razor                    # Surface 2 — ICitizenWalletClient.ListDevicesAsync()
        └── Enrol.razor                      # Surface 3 — "Add a phone" enrolment wizard

tests/
├── Sorcha.Tenant.Service.Tests/            # Minting-coverage regression tests (all issuance paths + refresh)
└── Sorcha.Wallet.Service.Tests/            # ResolveCitizenContext fallback/recovery tests
```

**Structure Decision**: Existing Sorcha web-microservices layout (backend services under `src/Services`, shared auth primitives under `src/Common`, PWA under `src/Apps/Sorcha.UI`). The change is surgical and lands in two services (Tenant mint-side assertions, Wallet resolve-side hardening) plus their test projects; no new projects or directories are created. PWA pages are verified, not rewritten.

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
