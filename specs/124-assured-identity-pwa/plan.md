# Implementation Plan: AssuredIdentity on the PWA

**Branch**: `124-assured-identity-pwa` | **Date**: 2026-05-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/124-assured-identity-pwa/spec.md`

## Summary

Replace the HAIP filesystem wallet target in the existing AssuredIdentity walkthrough (Feature 107) with the real Citizen Wallet PWA (Feature 114) as the credential recipient, and add a first-credential takeover arrival moment. Scope B from the validated design: minimum swap + arrival moment. Three vertical slices: (1) a small Wallet Service capability for setting/reading a pending-application notice; (2) PWA changes for the waiting state, welcome takeover, and enrol-Done copy; (3) walkthrough rewrite removing the HAIP filesystem wallet path and switching the blueprint's target audience.

The technical approach is additive — one new endpoint group, one new PWA store (mirroring the existing `IDeviceMetaStore` IndexedDB pattern), three Razor-page edits, and surgical walkthrough script changes. No new service, no schema migration in the existing data-store-audited path. The blueprint change (`targetAudience: HaipExternalWallet` → `SorchaLocalWallet`) is a one-line config flip uncovered during research; the spec assumed it was already in place.

## Technical Context

**Language/Version**: .NET 10 / C# 14 (per CLAUDE.md; constitution Tech Stack table is stale at C# 13 — not a violation, this feature follows the active project version)
**Primary Dependencies**: ASP.NET Core Minimal APIs, Scalar (OpenAPI), Blazor WebAssembly (PWA), MudBlazor (UI), SignalR (CredentialAvailable push), Sorcha.Cryptography (existing), JsonSchema.Net (existing)
**Storage**: PWA-side IndexedDB (new `WalletFlags` record alongside the existing `DeviceMetaRecord` in the `device` store); Wallet Service-side `IDistributedCache`-backed pending-application notice keyed by `PlatformUserId` with a 24-hour TTL (Redis in production, in-memory in tests; no EF migration required)
**Testing**: xUnit + FluentAssertions + Moq for service + PWA unit tests; existing Playwright E2E suite for the wallet (extended with two new tests covering takeover persistence + waiting-state visibility)
**Target Platform**: Linux containers for Sorcha.Wallet.Service; mobile Safari / Chrome PWA for Sorcha.Citizen.Wallet; PowerShell 7.5+ for walkthrough scripts on the operator's workstation
**Project Type**: Web — multi-service backend + Blazor WASM PWA + PowerShell walkthrough harness
**Performance Goals**: Welcome takeover renders within 200 ms of credential visibility on the wallet; pending-application notice read latency under 50 ms p95; SC-001 end-to-end demo sequence completes in 60 s including AI analyst processing
**Constraints**: Zero regression in existing Feature 114 test suite; multi-peer cross-peer delivery test (`run-multi-peer.ps1`) continues to pass; no new persisted entity in the storage-audit-gated list (constitution + Feature 113 audit posture)
**Scale/Scope**: Single demonstration citizen for the walkthrough; production scale identical to Feature 114 (one record per active citizen, low write volume — set/clear on application submission events)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new service. One endpoint group added to existing Sorcha.Wallet.Service. Dependencies flow downward only — the new in-memory/Redis notice store has no dependency on Application or Infrastructure layers above it. |
| II. Security First | PASS | New endpoint authenticated by the existing `CitizenWalletPolicyName` policy, scoped to the calling PlatformUser. Notice carries no credential content, just a human-readable label. Inputs validated with FluentValidation matching the existing `CitizenWalletEndpoints` pattern. |
| III. API Documentation | PASS | New endpoints follow `.WithName` / `.WithSummary` / `.WithDescription` / `.Produces` conventions; OpenAPI surface auto-exposed at `/openapi/v1.json`. XML doc comments on the static handler methods. |
| IV. Testing Requirements | PASS | Plan targets >85% coverage on new code: unit tests for `PendingApplicationStore`, endpoint handler tests (reflection-based static-handler invocation pattern already used in `PresentationEndpointTests`), PWA service tests for the new flags store, two Playwright E2E flows. xUnit + FluentAssertions + Moq throughout. Deterministic. |
| V. Code Quality | PASS | C# 14, async/await on all I/O, DI throughout, nullable enabled, no warnings expected. New components follow the existing scoped-vs-singleton patterns established by Feature 114. |
| VI. Blueprint Creation Standards | PASS | One JSON edit to `walkthroughs/AssuredIdentity/blueprints/assured-identity.json` (`targetAudience` value flip). No fluent-API blueprint creation. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: "credential", "wallet", "citizen", "blueprint". New noun "pending-application notice" introduced consistently across spec, data model, contracts, and code. |
| VIII. Observability by Default | PASS | New endpoint emits structured logs (operation name, citizen id, label) and a counter (`sorcha_pending_application_notice_total{op}`) on the existing `Sorcha.Wallet.Service` meter. Health check unaffected. |

**Verdict**: No violations. Complexity Tracking section omitted.

## Project Structure

### Documentation (this feature)

```text
specs/124-assured-identity-pwa/
├── plan.md              # This file (/speckit.plan command output)
├── spec.md              # Feature spec
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── pending-application-notice.openapi.yaml   # Phase 1 output
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks — NOT this command)
```

### Source Code (repository root)

Multi-project Sorcha layout. The feature touches three areas:

```text
src/
├── Apps/
│   └── Sorcha.Citizen.Wallet/                       # PWA (Blazor WASM)
│       ├── Pages/
│       │   ├── Index.razor                           # waiting state + welcome takeover host
│       │   └── Enrol.razor                           # conditional Done-step copy (FR-001)
│       ├── Components/
│       │   ├── WelcomeTakeover.razor                 # NEW — full-screen overlay
│       │   └── WaitingCard.razor                     # NEW — pulsing skeleton
│       ├── Services/
│       │   ├── IWalletFlagsStore.cs                  # NEW — welcomedAt persistence
│       │   ├── IndexedDbWalletFlagsStore.cs          # NEW — IndexedDB impl
│       │   └── IPendingApplicationClient.cs          # NEW — HTTP client for /pending-applications
│       └── wwwroot/css/welcome-takeover.css          # NEW — animation keyframes
└── Services/
    └── Sorcha.Wallet.Service/
        ├── Endpoints/
        │   └── PendingApplicationEndpoints.cs        # NEW — set/clear/read endpoints
        ├── Services/
        │   ├── Interfaces/
        │   │   └── IPendingApplicationStore.cs       # NEW
        │   └── Implementation/
        │       └── RedisPendingApplicationStore.cs   # NEW — IDistributedCache-backed
        └── Models/
            ├── SetPendingApplicationRequest.cs       # NEW
            └── PendingApplicationResponse.cs         # NEW

tests/
├── Sorcha.Wallet.Service.Tests/
│   ├── Services/
│   │   └── PendingApplicationStoreTests.cs          # NEW — set/clear/read + TTL
│   └── Endpoints/
│       └── PendingApplicationEndpointTests.cs       # NEW — reflection-based handler tests
└── Sorcha.Citizen.Wallet.Tests/                     # (extend if exists; create test project if absent)
    └── Services/
        └── IndexedDbWalletFlagsStoreTests.cs        # NEW

walkthroughs/AssuredIdentity/
├── blueprints/
│   └── assured-identity.json                        # MODIFY — targetAudience flip
├── setup.ps1                                         # MODIFY — pre-sign-in citizen to PWA
├── run-phase1-identity.ps1                           # MODIFY — set/clear pending notice
├── run-phase2-licence.ps1                            # MODIFY — same notice-setting pattern
├── run-agents.ps1                                    # MODIFY — analyst-scripted timing for demo
├── README.md                                          # MODIFY — describe PWA-default path
└── wallet/                                            # DELETE — entire directory
    ├── credentials/
    ├── holder-key.jwk.json
    └── holder-key.pem
```

**Structure Decision**: Existing Sorcha multi-project layout. No new projects; one Razor-component pair in the PWA, one endpoint file + store impl in the Wallet Service, surgical script edits in the walkthrough, deletion of the HAIP filesystem wallet directory. New tests live alongside existing tests for the same projects. The `Sorcha.Citizen.Wallet.Tests` project is created if it does not already exist (research item).

## Complexity Tracking

No constitution violations. Section omitted.
