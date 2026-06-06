# Implementation Plan: Wallet-aware PairingTakeover

**Branch**: `149-pwa-pairing-takeover-wallet-aware` | **Date**: 2026-06-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/149-pwa-pairing-takeover-wallet-aware/spec.md`
**Design**: `docs/superpowers/specs/2026-06-06-pwa-pairing-takeover-wallet-aware-design.md`

## Summary

Make the Citizen Wallet PWA `PairingTakeover` overlay wallet-aware. Add a tiny consumer-tier
endpoint `GET /api/v1/wallet/exists → 200 { hasWallet }` (always 200, no auth-ambiguity), consume it
from a PWA-local one-shot `IHasWalletProbe`, and add a third "create-wallet" state to the takeover
that routes a walletless citizen to the web host's `/wallets/create` (fire-and-forget force-load,
mirroring the existing web-signup handoff). Citizens who already have a wallet keep today's pair flow
unchanged. The wallet check is one-shot because walletless is a terminal cold-start state.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: ASP.NET Core Minimal APIs (Wallet Service); Blazor WebAssembly +
MudBlazor (PWA); bUnit + xUnit + FluentAssertions (tests)

**Storage**: N/A — read-only resolution of an existing wallet record via `IWalletRepository`
through `ResolveCitizenContextAsync`. No schema/EF change, no migration.

**Testing**: xUnit + bUnit (`Sorcha.UI.Testing.ComponentTestFixture`) for the PWA component and the
probe; mocked `HttpMessageHandler` for the typed client; optional Playwright E2E under
`tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/`.

**Target Platform**: Wallet Service (containerised .NET) + PWA (Blazor WASM, browser, mounted under
`/wallet/`).

**Project Type**: Web — backend service endpoint + frontend (PWA) component/service.

**Performance Goals**: One extra lightweight GET per cold-start takeover render; negligible.

**Constraints**: No `ISnackbar` (CI gate); consumer-tier audience only; companion-first (no in-PWA
wallet creation); no flashing of intermediate UI states; fail-safe on transient errors.

**Scale/Scope**: 3 new files (response model, probe interface, probe impl) + 1 new endpoint + edits
to `PairingTakeover.razor` and PWA DI registration. 2 new test files. Doc-sync to the
`sorcha-architecture` skill + roadmap.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Microservices-First** — PASS. The existence endpoint lives in the Wallet Service (owner of
  wallet data); the PWA talks only to the Wallet Service via the gateway. No upward/cross coupling.
- **II. Security First** — PASS. Endpoint is consumer-tier (`RequireConsumerAudience`) +
  `RateLimitPolicies.Strict`. Returns only a boolean; leaks no wallet address/PII. No secrets.
- **III. API Documentation** — PASS. Endpoint gets `.WithName/.WithSummary/.WithDescription` +
  `.Produces<WalletExistsResponse>`; new model carries XML docs. Built-in OpenAPI (no Swagger).
- **IV. Testing Requirements** — PASS. xUnit + bUnit; AAA; deterministic mocked handler;
  component-state coverage for all branches; >85% target for the new code.
- **V. Code Quality** — PASS. async/await, DI, nullable enabled, no warnings; matches existing
  citizen-endpoint and probe patterns.
- **VI. Blueprint Standards** — N/A (no blueprints).
- **VII. Domain-Driven Design** — PASS. Uses existing ubiquitous terms (Wallet, citizen).
- **VIII. Observability** — PASS. Endpoint inherits service telemetry/health; probe logs transient
  failures via `ILogger` (structured, no string interpolation). No new meters required.

**Result: PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/149-pwa-pairing-takeover-wallet-aware/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (wallet-exists.openapi.yaml)
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── Common/Sorcha.CitizenWallet.Abstractions/
│   └── Models/
│       └── WalletExistsResponse.cs                 # NEW — { bool HasWallet }
├── Services/Sorcha.Wallet.Service/
│   └── Endpoints/
│       └── CitizenWalletEndpoints.cs               # EDIT — map GET /api/v1/wallet/exists + handler
└── Apps/Sorcha.Wallet.Pwa/
    ├── Services/Wallet/
    │   ├── IHasWalletProbe.cs                       # NEW — one-shot Task<bool> HasWalletAsync
    │   └── HasWalletProbe.cs                        # NEW — typed-HttpClient impl, fail-safe true
    ├── Components/
    │   └── PairingTakeover.razor                    # EDIT — 3-state machine + create-wallet body
    └── Extensions/
        └── ServiceCollectionExtensions.cs          # EDIT — register HasWalletProbe HttpClient

tests/
└── Sorcha.Wallet.Pwa.Tests/
    ├── Services/
    │   └── HasWalletProbeTests.cs                   # NEW — 200 true / 200 false / transient→true
    └── Components/
        └── PairingTakeoverTests.cs                  # NEW — walletless / has-wallet / in-flight / has-device
```

**Structure Decision**: Web split across an existing backend service
(`Sorcha.Wallet.Service`) and the existing PWA frontend (`Sorcha.Wallet.Pwa`), with the shared DTO
in the existing `Sorcha.CitizenWallet.Abstractions` contract assembly. No new projects.

## Complexity Tracking

> No constitution violations — section intentionally empty.
