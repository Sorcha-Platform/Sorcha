# Implementation Plan: Fix Passkey Login Auth-State Notification (Auth Hardening C)

**Branch**: `167-fix-passkey-auth-state` | **Date**: 2026-06-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/167-fix-passkey-auth-state/spec.md`

## Summary

After a passkey (or any) sign-in, the web app receives the freshly issued token via the URL-fragment
handoff and consumes it inside `CustomAuthenticationStateProvider.GetAuthenticationStateCoreAsync()`.
The session is correctly established (the token is cached and the next auth-state query is
authenticated), but **no `AuthenticationStateChanged` event is raised on the fresh-consume path**.
Components that already rendered as anonymous (notably **Profile** and **Security**) hold the cached
`_authStateTask` and never re-evaluate, so they show "not signed in" until a manual reload.

**Technical approach**: When — and only when — `TryConsumeFragmentTokenAsync` consumes a *fresh*
fragment token and establishes an authenticated session, the provider re-broadcasts the auth state to
already-rendered subscribers by raising `NotifyAuthenticationStateChanged`. The re-broadcast is fired
**after** the in-flight auth-state task completes (so consumers re-query the now-cached, authenticated
state), is **idempotent** (the one-time fragment staging is already cleared after first consume, so a
re-query falls back to the cached token — never a second consume), and is **gated** so it never fires on
anonymous navigation, on cache-only resolution, or for an expired/invalid token. This hardens the
*shared* handoff, so it fixes the defect for every sign-in method that returns through the fragment
handoff (passkey, social/SSO, password), not just passkey.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blazor WebAssembly client)

**Primary Dependencies**: `Microsoft.AspNetCore.Components.Authorization`
(`AuthenticationStateProvider`, `CascadingAuthenticationState`, `AuthorizeRouteView`),
`Microsoft.JSInterop`, `System.IdentityModel.Tokens.Jwt`

**Storage**: Browser `localStorage` via JSInterop — fragment staging
(`sorcha:fragment-pending` + `window.__sorcha_fragment_token`) and the encrypted token cache
(`sorcha:tokens:{profile}` via `ITokenCache` / `BrowserTokenCache`). No server-side storage change.

**Testing**: xUnit + Moq + FluentAssertions (`tests/Sorcha.UI.Core.Tests`); Playwright E2E
(`tests/Sorcha.UI.E2E.Tests`) for the end-to-end signed-in-after-login verification.

**Target Platform**: Blazor WebAssembly browser client (`Sorcha.UI.Web.Client`), components in
`Sorcha.UI.Components.User` (shared with the Wallet PWA).

**Project Type**: Web application (Blazor WASM front-end). The change is confined to the front-end
auth-state-notification layer.

**Performance Goals**: No measurable perf impact — one extra event raise per fresh login. The
re-broadcast must add no perceptible delay to landing on a signed-in page.

**Constraints**: No flicker (FR-005), idempotent / single-consume (FR-006), no recursion / infinite
notification loop, no change to token issuance or transport (FR-009). Must not break the existing
`TokenRefreshService`, `OrgSwitcher`, `LogoutConfirmDialog`, and `MainLayout` callers of
`NotifyAuthenticationStateChanged()`.

**Scale/Scope**: Single provider class plus targeted unit + E2E tests. Two affected pages verified
(Profile, Security); the shared `MainLayout` consumer benefits transparently.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | ✅ PASS | Front-end only; no service boundaries crossed, no new coupling. |
| II. Security First | ✅ PASS | Preserves one-time-token semantics; never announces signed-in for an absent/expired/invalid token (FR-004). No secrets, no token transport change. |
| III. API Documentation | ✅ PASS | No public REST/gRPC surface change. New/changed public members on `CustomAuthenticationStateProvider` get `/// <summary>` XML docs. |
| IV. Testing Requirements | ✅ PASS | xUnit unit tests for the fresh-consume broadcast + idempotency + anonymous no-flicker; Playwright E2E for the user-visible outcome. Target >85% on changed code. |
| V. Code Quality | ✅ PASS | Nullable enabled; async/await; no new warnings; matches surrounding provider style. |
| VI. Blueprint Standards | ✅ N/A | No blueprints involved. |
| VII. Domain-Driven Design | ✅ PASS | Uses existing ubiquitous terms; no model rename. |
| VIII. Observability | ✅ PASS | Optional debug-level structured log on fresh-consume broadcast; no string-interpolated logs. |

**Result**: PASS — no violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/167-fix-passkey-auth-state/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (state/entities of the notification flow)
├── quickstart.md        # Phase 1 output (validation guide)
├── contracts/
│   └── auth-state-notification.md   # In-process contract for the re-broadcast
├── checklists/
│   └── requirements.md  # Pre-existing spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/
├── Sorcha.UI.Components.User/
│   ├── Services/Shared/Authentication/
│   │   ├── CustomAuthenticationStateProvider.cs   # PRIMARY CHANGE — re-broadcast on fresh consume
│   │   ├── ITokenCache.cs / BrowserTokenCache.cs   # unchanged (session establishment)
│   │   └── TokenRefreshService.cs                  # unchanged (existing notify caller)
│   └── Models/Shared/Authentication/
│       └── FragmentTokenResult.cs                  # unchanged
├── Sorcha.UI.Web.Client/
│   ├── Routes.razor                                # CascadingAuthenticationState + AuthorizeRouteView (unchanged)
│   ├── Pages/MyProfile.razor                       # [Authorize] page — verified, not modified
│   ├── Pages/Security.razor                        # [Authorize] page — verified, not modified
│   └── wwwroot/app/js/fragment-handoff.js          # staging IIFE (unchanged)

tests/
├── Sorcha.UI.Core.Tests/Services/Authentication/
│   └── CustomAuthenticationStateProviderTests.cs   # NEW/EXPANDED — fresh-consume broadcast + idempotency
└── Sorcha.UI.E2E.Tests/                            # E2E: signed-in Profile/Security after login, no reload
```

**Structure Decision**: Web-application layout. The fix lives entirely in the shared front-end auth
layer — primarily `CustomAuthenticationStateProvider.cs` (namespace
`Sorcha.UI.Core.Services.Authentication`, physically in `Sorcha.UI.Components.User`). The Profile and
Security pages are verification targets only and are not modified. No backend or service code changes.

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
