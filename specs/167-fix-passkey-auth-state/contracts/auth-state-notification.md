# Contract: Post-Login Auth-State Re-Notification

**Feature**: 167-fix-passkey-auth-state | **Type**: In-process (Blazor component / DI) contract

This feature exposes no REST/gRPC surface. Its contract is the **behavioural contract** of
`CustomAuthenticationStateProvider` toward its in-process consumers (`CascadingAuthenticationState`,
`AuthorizeRouteView`, `AuthorizeView`, and any component subscribing to `AuthenticationStateChanged`).

## Component under contract

`Sorcha.UI.Core.Services.Authentication.CustomAuthenticationStateProvider`
(file: `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`),
registered scoped as both the concrete type and `AuthenticationStateProvider`.

## Contract clauses

### C1 — Fresh consume raises exactly one change event
**Given** a valid, unexpired token is staged in `localStorage["sorcha:fragment-pending"]` or
`window.__sorcha_fragment_token`,
**when** `GetAuthenticationStateAsync()` runs and consumes it,
**then** the provider raises `AuthenticationStateChanged` **exactly once** with an **authenticated**
`AuthenticationState`, after the consuming task has completed.
*(FR-001, FR-002, FR-003)*

### C2 — No event on cache-only resolution
**Given** no fragment token is staged but a valid token exists in the cache,
**when** `GetAuthenticationStateAsync()` runs,
**then** the provider returns the authenticated state and raises **no** `AuthenticationStateChanged`
event from the consume path.
*(FR-005 — no flicker on ordinary navigation)*

### C3 — No event for absent/expired/invalid token
**Given** no token is staged or the staged/cached token is expired or unreadable,
**when** `GetAuthenticationStateAsync()` runs,
**then** the provider returns an **anonymous** state and raises **no** signed-in event.
*(FR-004)*

### C4 — Idempotent, single-consume
**Given** a fresh token was consumed and broadcast once,
**when** `GetAuthenticationStateAsync()` is called again (including the re-query triggered by the
broadcast itself),
**then** the staged token is **not** consumed a second time (staging already cleared), the result is
authenticated from cache, and **no additional** consume-path event is raised.
*(FR-006)*

### C5 — Existing notify callers unchanged
`NotifyAuthenticationStateChanged()` retains its current public behaviour (nulls `_authStateTask`,
re-queries, raises the event) for existing callers: `TokenRefreshService`, `OrgSwitcher`,
`LogoutConfirmDialog`, `MainLayout` tier-upgrade.
*(FR-008 — no regression for re-login / org-switch / logout)*

### C6 — No token issuance/transport change
The fragment staging keys, JWT validation, store-before-clear ordering, and `TokenCacheEntry` shape are
unchanged.
*(FR-009)*

## Observable signals for tests

| Signal | How observed (unit) | How observed (E2E) |
|--------|---------------------|--------------------|
| `AuthenticationStateChanged` fired | subscribe to the event, count invocations | n/a |
| Final auth state | `(await GetAuthenticationStateAsync()).User.Identity.IsAuthenticated` | Profile/Security render signed-in content |
| Token consumed once | `ITokenCache.StoreTokenAsync` invoked once (Moq `Times.Once`) | login retry count in `GlobalAuthSetup` does not increase |
| No flicker | no signed-in event on anonymous/cache paths (C2, C3) | no "not signed in" → "signed in" visible toggle without reload |
