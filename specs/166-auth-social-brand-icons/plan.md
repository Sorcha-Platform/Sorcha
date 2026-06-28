# Implementation Plan: Social Provider Brand Icons on Login & Signup

**Branch**: `166-auth-social-brand-icons` | **Date**: 2026-06-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/166-auth-social-brand-icons/spec.md`

## Summary

Add the correct, recognisable brand icon (Google, Microsoft, GitHub, Apple) to each social
provider button on three auth surfaces: the **web login** page, the **web signup** page (both
server-rendered Razor Pages in the Tenant Service), and the **citizen wallet PWA** sign-in screen
(Blazor WASM + MudBlazor). The feature is **visual only** — it reuses the existing provider
configuration, sign-in flows, token handling, and callbacks without modification.

Technical approach (matches the "web inline SVG + PWA `Icons.Custom.Brands`" intent of the request):

- **Web (Razor Pages)** — a small server-side icon resolver maps a provider key to an inline,
  decorative (`aria-hidden`) SVG markup string, rendered as a leading mark inside the existing
  `.social-btn` flex layout. Unknown providers resolve to a neutral generic glyph. Multi-colour
  marks (Google, Microsoft) keep their official brand colours; monochrome marks (Apple, GitHub)
  use `currentColor` so they stay legible in both light and dark presentation.
- **PWA (MudBlazor)** — map each provider key to `Icons.Custom.Brands.{Google|Microsoft|GitHub|Apple}`
  and pass it as the `StartIcon` of the existing `MudButton`, matching the passkey button's leading-icon
  treatment. Unknown providers resolve to `Icons.Material.Filled.Public`.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**:
- Web: ASP.NET Core Razor Pages (Tenant Service), no new packages — inline SVG strings.
- PWA: Blazor WebAssembly + MudBlazor **9.5.0** (`Icons.Custom.Brands.*` confirmed available).

**Storage**: N/A (no persistence change). Provider list is read at render time from existing config.

**Testing**: xUnit + FluentAssertions for the icon-resolver unit tests; existing Playwright E2E
infra (Docker) for visual/behavioural verification on both surfaces.

**Target Platform**: Server-rendered web auth pages (any modern browser); citizen wallet PWA (mobile/desktop browser, WASM).

**Project Type**: Web application (server-rendered Razor auth surface + Blazor WASM PWA companion).

**Performance Goals**: No measurable regression. Inline SVG adds a few hundred bytes per button to
already-rendered HTML; no extra network requests. MudBlazor icons are compiled-in SVG path strings.

**Constraints**:
- Visual-only — MUST NOT alter redirect/token/callback/error behaviour (FR-006, SC-004).
- Icons are decorative — existing text label remains the accessible name; no duplicate/empty
  screen-reader announcements (FR-010, SC-005).
- Legible in light AND dark presentation, including predominantly black/white marks (FR-008, SC-006).
- Provider set stays driven by existing configuration — no add/remove/reorder (FR-005).
- No broken/placeholder images; unknown provider → neutral fallback (FR-007, SC-002).

**Scale/Scope**: 4 supported providers; 3 surfaces (2 Razor pages + 1 PWA component). Small, focused
UI change touching ~3 view files + 1 web icon resolver + 1 PWA icon map + CSS + tests.

### Surfaces & anchor points (from codebase survey)

| Surface | File | Current render |
|---------|------|----------------|
| Web login | `src/Services/Sorcha.Tenant.Service/Pages/Auth/Login.cshtml` (~L98-108) | `@foreach (var provider in Model.AvailableProviders)` → `<button class="social-btn" data-provider="@provider">Continue with @provider</button>` |
| Web signup | `src/Services/Sorcha.Tenant.Service/Pages/Auth/Signup.cshtml` (~L67-84) | Same loop inside the Social tab |
| Web provider source | `ISocialLoginService.GetConfiguredProviderNames()` → `IReadOnlyList<string>` (e.g. `"Google"`, `"GitHub"`) | unchanged |
| Web button CSS | `src/Services/Sorcha.Tenant.Service/wwwroot/css/auth.css` `.social-btn` (~L95-115) | flex, `gap: 0.5rem`, `align-items:center` |
| PWA sign-in | `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor` (~L44-51) | `<MudButton Variant="Variant.Outlined" …>Continue with @provider</MudButton>` |
| PWA passkey ref | `SignIn.razor` (~L36-41) | `MudButton … StartIcon="@Icons.Material.Filled.Fingerprint"` |
| PWA provider source | `ISocialProvidersClient.GetConfiguredAsync()` → lowercase names (`"google"`) | unchanged |
| Existing PWA-side icon-map precedent | `Sorcha.UI.Components.User/Components/Security/SocialLinksSection.razor` (~L187-194) `ProviderIcon(string)` switch | reuse pattern |

> **Case sensitivity note**: web provider names arrive capitalised (`"Google"`), PWA names lowercase
> (`"google"`). Both resolvers MUST match **case-insensitively** so the same four keys resolve on both surfaces.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| I. Microservices-First | ✅ No new cross-service coupling. Web change is self-contained in Tenant Service; PWA change self-contained in Sorcha.Wallet.Pwa. No new upward dependencies. |
| II. Security First | ✅ No auth/token/redirect behaviour touched (FR-006). No secrets, no new external boundary, no new input. Inline SVG is static, author-controlled markup (no user data interpolated into SVG). |
| III. API Documentation | ✅ No new public API endpoints. Any new public C# helper (web icon resolver) carries `/// <summary>` per project convention. |
| IV. Testing | ✅ New icon-resolver logic is unit-tested (every supported provider → non-empty icon; unknown → fallback; case-insensitive). Visual/behavioural parity covered by Playwright. Targets >85% on new code. |
| V. Code Quality | ✅ Nullable enabled, async only where present, no new warnings. Matches existing `.social-btn` / `ProviderIcon` patterns. |
| VI. Blueprint Standards | N/A (no blueprints). |
| VII. Domain-Driven Design | ✅ Uses existing "social provider" vocabulary; no domain model change. |
| VIII. Observability | N/A (presentational; no new telemetry surface — sign-in telemetry already exists and is unchanged). |

**Result**: PASS — no violations. Complexity Tracking section intentionally empty.

## Project Structure

### Documentation (this feature)

```text
specs/166-auth-social-brand-icons/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output — internal UI mapping contract (no new external API)
│   └── icon-resolution.md
├── checklists/          # Pre-existing
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Tenant.Service/
├── Pages/Auth/
│   ├── Login.cshtml                  # add leading brand-icon markup to social-btn loop
│   └── Signup.cshtml                 # add leading brand-icon markup to social-btn loop
├── Services/
│   └── SocialProviderBrandIcon.cs    # NEW: server-side provider-key → inline SVG resolver (+ neutral fallback)
└── wwwroot/css/auth.css              # .social-btn svg sizing/alignment for light+dark legibility

src/Apps/Sorcha.Wallet.Pwa/
└── Pages/SignIn.razor                # add StartIcon via provider-key → Icons.Custom.Brands map (+ fallback)

tests/Sorcha.Tenant.Service.Tests/
└── Services/SocialProviderBrandIconTests.cs   # NEW: resolver mapping + case-insensitivity + fallback

tests/ (PWA component or Playwright)            # visual/behavioural parity checks (existing infra)
```

**Structure Decision**: Web application split — server-rendered Razor auth pages live in
`Sorcha.Tenant.Service` (the JWT issuer hosts the login/signup UI), and the citizen-facing PWA
sign-in lives in `Sorcha.Wallet.Pwa`. The two surfaces use different icon delivery mechanisms by
design (inline SVG vs. MudBlazor `Icons.Custom.Brands`), unified by a shared provider-key vocabulary
and a shared fallback contract documented in `contracts/icon-resolution.md`.

## Complexity Tracking

> No constitution violations — section intentionally empty.
