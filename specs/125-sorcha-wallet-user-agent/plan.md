# Implementation Plan: Sorcha Wallet (Full User-Agent v1)

**Branch**: `125-sorcha-wallet-user-agent` | **Date**: 2026-05-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/125-sorcha-wallet-user-agent/spec.md`

## Summary

Grow today's credentials-only Citizen Wallet PWA into the **Sorcha Wallet** — the in-pocket end-user agent for any Sorcha user. Three primary capability additions: doorstep verification (citizen-as-verifier with the same OID4VP pipeline `Sorcha.Verifier` runs today), application-from-phone (the existing `SorchaFormRenderer` + persona autofill + mobile camera capture wired into the PWA), and multi-context UI (the user has one wallet, one identity, but switches between organisational contexts cleanly). All on top of a project-wide rename from `Sorcha.Citizen.Wallet` to `Sorcha.Wallet.Pwa` (and `Sorcha.Citizen.Verifier` to `Sorcha.Verifier`) and a single signing abstraction (`IUserSigner`) that lands the managed-mode implementation in v1 while leaving the slot open for self-custody and co-signed implementations in v2.

The technical approach is **library-first**: `Sorcha.UI.Components.User` is the contract; new shared primitives (ContextChipSwitcher, VerifyHomeAction, PresentHomeAction, NeedsAttentionBand, RecentActivityFeed, GuidedTourScaffold, ErrorRecoveryScaffold, PortraitCaptureControl, TransactionHistoryFeed) land in the library, plus migrations of MyDevices / MyAuthMethods / MyProfile from `Sorcha.UI.Web` into the library. PWA pages are thin wrappers that compose library components.

## Technical Context

**Language/Version**: .NET 10 / C# 14 (per CLAUDE.md; constitution Tech Stack table is stale at C# 13 — not a violation, this feature follows the active project version).
**Primary Dependencies**: ASP.NET Core Minimal APIs (server), Blazor WebAssembly (PWA shell), MudBlazor (UI primitives), Microsoft.AspNetCore.SignalR.Client (push), JsonSchema.Net (form rendering), FluentValidation (validators), Microsoft.JSInterop (camera bridge), `Sorcha.UI.Components.User` (shared component library), Aspire.StackExchange.Redis.DistributedCaching (server-side cache).
**Storage**: IndexedDB on the PWA (per-device flags, credential cache, delegation, sync cursor, access token, device meta, **new**: verification history, guided-tour flag, persona-per-context cache). PostgreSQL via EF Core in Wallet Service (existing `WalletDbContext`) and Tenant Service (existing `TenantDbContext`; persona table needs a context column — minimal migration). Redis via `IDistributedCache` for pending-application notice (already wired by PR #697).
**Testing**: xUnit + FluentAssertions + Moq for unit tests across `Sorcha.Wallet.Service.Tests`, `Sorcha.Wallet.Pwa.Tests` (renamed from `Sorcha.Citizen.Wallet.Tests`), `Sorcha.UI.Core.Tests`. Playwright (via NUnit) under `tests/Sorcha.UI.E2E.Tests/Docker/` for the three headline demo beats, post-redeploy cache test, auth-gated nav coverage.
**Target Platform**: Mobile Safari + Chrome PWA (primary), Chromium-based mobile browsers (NFC support optional), desktop browsers for the web shell. Linux containers for Sorcha.Wallet.Service + Sorcha.Tenant.Service + Sorcha.Wallet.Pwa nginx serving.
**Project Type**: Web — multi-service backend + Blazor WASM PWA + shared component library + reference verifier app. Existing Sorcha multi-project layout.
**Performance Goals**:
- Verify result under 5 seconds from QR scan to trust panel.
- Context switch under 1 second from tap to fully-refreshed Home content.
- Hero Present tap → presentation request within 1 second.
- Application form first paint under 2 seconds.
- All existing F124 perf targets preserved.

**Constraints**:
- Zero regression in existing F124 test suites (SC-006).
- Multi-peer cross-peer delivery test (`run-multi-peer.ps1`) continues to pass.
- No breaking change to the `/wallet/` URL prefix.
- `IUserSigner` abstraction must remain consumer-blind so self-custody (v2) drops in without UI rewrites.
- Reading age of user-visible copy ≤ Year 8 average (SC-010).

**Scale/Scope**: Single-citizen scale today (one wallet per user, ≤10 credentials per context typically). Future scale matches the rest of the platform — the wallet doesn't impose new server-side load profiles beyond the existing per-citizen sync + presentation traffic.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new service. Wallet PWA + shared library grow; Wallet Service + Tenant Service may gain a per-context persona endpoint (additive, no schema break). Dependencies flow downward only — the new `IUserSigner` abstraction lives in the PWA layer, not in Core. |
| II. Security First | PASS | Citizen JWT scoped per-context; per-context content scoping enforced server-side via existing PlatformUser + OrgMembership + JWT audience checks. Verification reuses the existing `VerifiablePresentationValidator` + `IIssuerKeyResolver`. All new input validated via FluentValidation. The ephemeral verifier identity is a wallet-generated EC P-256 key, never persisted server-side. |
| III. API Documentation | PASS | New endpoints (if any) use Scalar + `.WithName` / `.WithSummary` / `.WithDescription` / `.Produces` conventions per the existing pattern. Most of this feature is PWA-side; new server endpoints are limited to per-context persona scoping. |
| IV. Testing Requirements | PASS | Target >85% on new code. Unit tests for `IUserSigner`, `ContextChipSwitcher`, new library primitives. Playwright coverage for three demo beats (FR-035), post-redeploy cache, auth-gated nav (FR-036, SC-008). xUnit + FluentAssertions + Moq throughout. All deterministic. |
| V. Code Quality | PASS | C# 14, async/await, DI, nullable enabled. No new compiler warnings. |
| VI. Blueprint Creation Standards | N/A | No new blueprints. The existing AssuredIdentity blueprint is consumed; no fluent-API blueprint creation. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: Credential, Presentation, Issuance, Verification, Disclosure. New vocabulary — "active context", "verification record", "user signer" — is consistent with existing organisation-membership and PlatformUser models. |
| VIII. Observability by Default | PASS | OpenTelemetry counters for: `sorcha_wallet_context_switch_total`, `sorcha_wallet_verification_total{outcome}`, `sorcha_wallet_application_submission_total`, `sorcha_wallet_tour_completion_total`. Structured logs on all new server-side surfaces. Health checks unaffected. |

**Verdict**: No violations. Complexity Tracking section omitted.

## Project Structure

### Documentation (this feature)

```text
specs/125-sorcha-wallet-user-agent/
├── plan.md              # This file (/speckit.plan command output)
├── spec.md              # Feature spec
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── per-context-persona.openapi.yaml      # Phase 1 output — only new server-side contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (already created)
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

Multi-project Sorcha layout. The feature touches four areas:

```text
src/
├── Apps/
│   ├── Sorcha.Wallet.Pwa/                              # was Sorcha.Citizen.Wallet
│   │   ├── Pages/
│   │   │   ├── Index.razor                              # Home rebuilt around library bands
│   │   │   ├── Enrol.razor                              # rename, copy refresh, no logic change
│   │   │   ├── Verify.razor                             # NEW — verify-from-wallet entry
│   │   │   ├── Activity.razor                           # consumes TransactionHistoryFeed
│   │   │   ├── Settings.razor                           # hub linking to Devices, Auth, Profile
│   │   │   ├── Devices.razor                            # consumes MyDevices from library
│   │   │   ├── AuthMethods.razor                        # NEW page — consumes MyAuthMethods
│   │   │   ├── Profile.razor                            # NEW page — consumes MyProfile, per-context
│   │   │   ├── Applications.razor                       # NEW — application listing + start
│   │   │   ├── ApplicationInstance.razor                # NEW — hosts SorchaFormRenderer for a blueprint instance
│   │   │   └── CredentialDetail.razor                   # consumes CredentialDetailView
│   │   ├── Services/
│   │   │   ├── IUserSigner.cs                           # NEW interface
│   │   │   ├── ManagedUserSigner.cs                     # NEW — v1 implementation
│   │   │   ├── IUserContext.cs                          # NEW — active-context state
│   │   │   ├── ManagedUserContext.cs                    # NEW — backed by JWT + OrgMembership
│   │   │   ├── IVerificationHistoryStore.cs             # NEW — IndexedDB + InMemory impls
│   │   │   ├── IGuidedTourStore.cs                      # NEW — IndexedDB + InMemory impls
│   │   │   ├── IPerContextPersonaCache.cs               # NEW — caches per-context personas
│   │   │   └── [existing services preserved]
│   │   ├── Components/
│   │   │   ├── WelcomeTakeover.razor                    # preserved from F124
│   │   │   ├── WaitingCard.razor                        # preserved from F124
│   │   │   ├── ConfirmRevokeDialog.razor                # consider lifting to library
│   │   │   └── RenameDeviceDialog.razor                 # consider lifting to library
│   │   ├── MainLayout.razor                              # adds context chip
│   │   └── Sorcha.Wallet.Pwa.csproj                     # rename
│   │
│   ├── Sorcha.Verifier/                                 # was Sorcha.Citizen.Verifier
│   │   └── [contents preserved; rename only]
│   │
│   └── Sorcha.UI/
│       ├── Sorcha.UI.Components.User/                   # library — grows substantially
│       │   ├── Components/
│       │   │   ├── Context/
│       │   │   │   └── ContextChipSwitcher.razor        # NEW
│       │   │   ├── Home/
│       │   │   │   ├── PresentHomeAction.razor          # NEW
│       │   │   │   ├── VerifyHomeAction.razor           # NEW
│       │   │   │   ├── NeedsAttentionBand.razor         # NEW
│       │   │   │   └── RecentActivityFeed.razor         # NEW
│       │   │   ├── Capture/
│       │   │   │   └── PortraitCaptureControl.razor     # NEW
│       │   │   ├── History/
│       │   │   │   └── TransactionHistoryFeed.razor     # NEW
│       │   │   ├── Verify/
│       │   │   │   └── VerifyFlow.razor                 # NEW — orchestrates scan + verify + display
│       │   │   ├── Settings/
│       │   │   │   ├── MyDevices.razor                  # MIGRATED from Sorcha.UI.Web
│       │   │   │   ├── MyAuthMethods.razor              # MIGRATED from Sorcha.UI.Web
│       │   │   │   └── MyProfile.razor                  # MIGRATED from Sorcha.UI.Web (per-context aware)
│       │   │   ├── Onboarding/
│       │   │   │   └── GuidedTourScaffold.razor         # NEW
│       │   │   ├── Errors/
│       │   │   │   └── ErrorRecoveryScaffold.razor      # NEW
│       │   │   ├── Shared/
│       │   │   │   ├── EmptyStateWithCta.razor          # REFACTOR of EmptyState
│       │   │   │   └── [existing components preserved]
│       │   │   └── Forms/
│       │   │       └── Layouts/
│       │   │           └── IdCardLayout.razor           # ENHANCE — fuller body for issued state
│       │   ├── Models/
│       │   │   ├── VerifyOutcome.cs                     # NEW
│       │   │   ├── VerificationRecord.cs                # NEW
│       │   │   ├── ContextSummary.cs                    # NEW
│       │   │   └── [existing models preserved]
│       │   └── Sorcha.UI.Components.User.csproj
│       │
│       ├── Sorcha.UI.Web.Client/                        # web shell — migrates pages out
│       │   └── [MyDevices/MyAuthMethods/MyProfile lifted to library; shell wrappers stay]
│       └── Sorcha.UI.Web/                               # unchanged backend host
│
└── Services/
    ├── Sorcha.Wallet.Service/                            # unchanged in structure
    │   └── Endpoints/                                    # may add per-context persona endpoints
    └── Sorcha.Tenant.Service/                            # gains per-context persona scope
        ├── Endpoints/
        │   └── PersonaEndpoints.cs                       # extend with optional ?context=<orgId>
        ├── Models/
        │   └── PlatformUserPersona.cs                    # add ContextOrgId? column
        └── Migrations/
            └── [single new migration if pre-release rule still holds — squash into initial]

tests/
├── Sorcha.Wallet.Pwa.Tests/                             # renamed from Sorcha.Citizen.Wallet.Tests
│   └── Services/                                         # add IUserSigner, IUserContext, history/tour stores
├── Sorcha.UI.Core.Tests/                                # extend with new library component tests
├── Sorcha.Wallet.Service.Tests/                         # unchanged
├── Sorcha.Tenant.Service.Tests/                         # extend with per-context persona tests
└── Sorcha.UI.E2E.Tests/Docker/CitizenWallet/            # may rename folder to Wallet/
    ├── CitizenWalletNavigationTests.cs                  # rename → WalletNavigationTests
    ├── CitizenWalletPushTests.cs                        # rename → WalletPushTests
    ├── CitizenWalletNginxCacheHeadersTests.cs           # rename → WalletNginxCacheHeadersTests
    ├── DoorstepVerificationTests.cs                      # NEW — beat 1 coverage
    ├── ApplicationFromPhoneTests.cs                      # NEW — beat 2 coverage
    ├── ContextSwitchingTests.cs                          # NEW — beat 3 coverage
    ├── PostRedeployCacheTests.cs                         # NEW — #700 Phase 2 closure
    └── AuthGatedNavigationTests.cs                       # NEW — #700 Phase 2 closure

walkthroughs/AssuredIdentity/
├── run-phase1-identity.ps1                              # may update copy after rename
└── README.md                                            # update "Citizen Wallet" → "Sorcha Wallet"
```

**Structure Decision**: Existing Sorcha multi-project layout, web-application shape. Library-first growth: `Sorcha.UI.Components.User` is the contract that both shells (PWA + web) consume. The rename is mechanical; no new projects.

## Complexity Tracking

No constitution violations. Section omitted.
