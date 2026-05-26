# Implementation Plan: Citizen Wallet Home — "Bolder" Visual Reskin

**Branch**: `141-wallet-home-redesign` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/141-wallet-home-redesign/spec.md`

## Summary

Restyle the Citizen Wallet PWA home (`Sorcha.Wallet.Pwa/Pages/Index.razor`) and shell (`Sorcha.Wallet.Pwa/MainLayout.razor`) to the approved "Bolder" design direction, introducing four new shared, user-facing Razor components (`WalletHero`, `BigActionButton`, `WalletCardStack` empty-only, `FloatingTabBar`) plus a theme/token bridge that wires dark mode and publishes the brand gradient. The change is a **reskin over Feature 125's information architecture** — every existing band, behaviour, navigation target, and data flow is preserved; only presentation changes. New components live in `Sorcha.UI.Components.User/Components/Wallet/` so the PWA consumes them directly and the web app picks them up via the `Sorcha.UI.Core` re-export. Verification is Playwright E2E against the Docker stack at phone and tablet widths.

## Technical Context

**Language/Version**: C# 14 / .NET 10; Razor components (Blazor WebAssembly)
**Primary Dependencies**: MudBlazor 9.2.0; `Sorcha.UI.Components.User` (shared component lib), `Sorcha.UI.Core` (re-export), `Sorcha.Wallet.Pwa` (host). Existing services reused: `IThemeService`, `ContextChipSwitcher`, F118 inbox (`IInboxApiService`/`TenantHubConnection`), `ICredentialCache`, `ISyncService`, the F125 home bands, F124 `WaitingCard`/`WelcomeTakeover`.
**Storage**: N/A — no new persisted data. Consumes existing client state only.
**Testing**: Playwright .NET E2E (`tests/Sorcha.UI.E2E.Tests`, `Category=CitizenWallet`) against Docker; bUnit component tests (`Sorcha.UI.Core.Tests`) for the new presentational components where logic warrants it.
**Target Platform**: Browser (Blazor WASM PWA) mounted at `/wallet`; shared components also compile into the `/app` web host.
**Project Type**: Web/front-end (shared component library + PWA host + web host).
**Performance Goals**: 60 fps interactions; press feedback perceived as immediate; no layout jank on first paint. No regression in Home first-paint behaviour (background sync remains fire-and-forget).
**Constraints**: PWA base-relative navigation (`NavigateTo("present")`, home `NavigateTo("")`); no `ISnackbar` (CI gate `check-no-snackbar.ps1`); PWA bundle hygiene (CI gate `check-pwa-bundle.ps1` — no forbidden assemblies, `Sorcha.UI.Components.User` present); honour `prefers-reduced-motion`; CSS isolation (`*.razor.css`) preferred over inline styles.
**Scale/Scope**: 1 page restyle + 1 shell restyle + 4 new components + 1 theme/token bridge + localisation of one warning string. ~5 components, ~1 theme file edit, ~2 host edits, 1 E2E page object + test class.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies? | Compliance |
|---|---|---|
| I. Microservices-First | Indirect | No service changes. Dependency flow preserved: PWA → `Components.User`; `Core` → `Components.User` (existing re-export). No upward deps introduced. ✅ |
| II. Security First | Minimal | No new external inputs, no secrets, no new boundaries. Navigation targets reused as-is. ✅ |
| III. API Documentation | N/A | No new APIs/endpoints. XML doc on new public component parameters per house style. ✅ (component-level) |
| IV. Testing | Yes | E2E coverage for home + nav at phone/tablet; bUnit for component logic (empty/populated branch, disabled-Present, reduced-motion). New code targets the project's E2E+unit norms for UI. ✅ |
| V. Code Quality | Yes | Nullable enabled, no Release warnings, async I/O, DI. CSS isolation for component styles. ✅ |
| VI. Blueprint Standards | N/A | No blueprints. |
| VII. DDD / ubiquitous language | Yes | "Credential", "Participant", "Present", "Verify" used consistently in UI copy/components. ✅ |
| VIII. Observability | N/A | Pure presentation; no new telemetry required. Existing logging on Index.razor preserved. ✅ |

**Result**: PASS. No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/141-wallet-home-redesign/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions on theming, layout, motion, placement
├── data-model.md        # Phase 1 — view-state the home reads (no persisted entities)
├── quickstart.md        # Phase 1 — how to run + verify the redesign locally
├── contracts/
│   └── component-contracts.md   # Phase 1 — parameter contracts for the 4 new components
├── checklists/
│   └── requirements.md  # Spec quality checklist (already created)
└── tasks.md             # Phase 2 — created by /speckit.tasks (NOT here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
├── Components/Wallet/
│   ├── WalletHero.razor + .razor.css              # NEW — gradient hero + folded header row
│   ├── BigActionButton.razor + .razor.css         # NEW — primary/ghost tall action button
│   ├── WalletCardStack.razor + .razor.css         # NEW — empty ghost fan only
│   └── FloatingTabBar.razor + .razor.css           # NEW — floating pill nav
├── Theme/
│   └── SorchaMudTheme.cs                            # EDIT — add design surface/bg/text tokens (light+dark)
└── (Resources / localisation for the sync-warning string)  # NEW/EDIT — move warning copy to a resource

src/Apps/Sorcha.Wallet.Pwa/
├── Pages/Index.razor                               # EDIT — compose WalletHero + BigActionButton + WalletCardStack over preserved F125 bands
├── MainLayout.razor                                # EDIT — replace bottom MudAppBar rail with FloatingTabBar; bind MudThemeProvider IsDarkMode to IThemeService; move header affordances usage into hero where appropriate
└── wwwroot/css/app.css                             # EDIT — page-bg token + --sorcha-gradient var + prefers-reduced-motion base hooks

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor   # VERIFY only — confirm shared components/theme tokens render; no wallet-home page added

tests/Sorcha.UI.E2E.Tests/
├── PageObjects/CitizenWallet/WalletHomePage.cs     # NEW — selectors for hero, actions, ghost stack, tab bar
└── Docker/CitizenWallet/WalletHomeRedesignTests.cs # NEW — empty/populated, nav, dark mode, phone/tablet, a11y-name checks
```

**Structure Decision**: Shared user-facing components go in `Sorcha.UI.Components.User/Components/Wallet/` per the Feature 122 placement rule (the `Components/Wallet/` folder already exists for transaction widgets). Namespaces stay rooted at `Sorcha.UI.Core.Components.Wallet` (the library's `RootNamespace` is `Sorcha.UI.Core`), so consumer `using` directives are `Sorcha.UI.Core.Components.Wallet`. The PWA references the library directly; `Sorcha.UI.Core` ProjectReferences it so the web host picks the same components up transparently. The theme token edit lives in the already-shared `SorchaMudTheme` so both hosts draw from one source.

## Complexity Tracking

> No constitution violations. Section intentionally empty.
