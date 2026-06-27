# Phase 1 Data Model: Fix Passkey Login Auth-State Notification

**Feature**: 167-fix-passkey-auth-state | **Date**: 2026-06-27

This feature changes a *notification flow*, not persisted data. The "entities" below are the in-memory
state and signals involved in the auth-state re-broadcast. No schema, table, or document changes.

## Entities

### 1. Authenticated session indicator (`AuthenticationState`)
The app-wide signal of whether the current user is signed in and who they are, produced by
`CustomAuthenticationStateProvider` and consumed by `CascadingAuthenticationState` /
`AuthorizeRouteView` / `AuthorizeView` and any component injecting `AuthenticationStateProvider`.

| Field | Source | Notes |
|-------|--------|-------|
| `User` (`ClaimsPrincipal`) | JWT claims from the cached/consumed token | Authenticated when identity has claims + `"jwt"` auth type; anonymous = empty `ClaimsIdentity`. |
| Authenticated (derived) | `User.Identity?.IsAuthenticated` | Pages branch signed-in vs not-signed-in on this. |

**Lifecycle / transitions** relevant to this feature:
- `Anonymous → Authenticated` on a fresh fragment-token consume — **this transition is what must be
  broadcast** (the defect: it is established but not announced).
- `Authenticated → Authenticated (new session)` on re-login / org-switch — must reflect the newest
  session, not a stale one (FR-008; org-switch already calls notify via `OrgSwitcher`).
- `* → Anonymous` on expired/invalid/absent token — must **not** be announced as signed-in (FR-004).

### 2. Post-login handoff token (`FragmentTokenResult` → `TokenCacheEntry`)
The one-time credential delivered in the URL fragment.

| Field | Type | Notes |
|-------|------|-------|
| `Token` | string (JWT) | Access token; validated for expiry before use. |
| `Refresh` | string? | Refresh token. |
| `ReturnUrl` | string? | Intended destination after sign-in. |

Staged by `fragment-handoff.js` in `localStorage["sorcha:fragment-pending"]` +
`window.__sorcha_fragment_token`; consumed into a `TokenCacheEntry`
(`AccessToken`, `RefreshToken`, `ExpiresAt`, `ProfileName`, `IssuedAt`) stored at
`localStorage["sorcha:tokens:{profile}"]`. **Consumed at most once** — staging is cleared after a
successful store.

### 3. Re-broadcast control state (new, in-memory, provider-private)
The minimal state the fix introduces on `CustomAuthenticationStateProvider`.

| Field | Type | Purpose | Invariant |
|-------|------|---------|-----------|
| `_authStateTask` | `Task<AuthenticationState>?` | Memoised auth-state (existing) | Nulled by `NotifyAuthenticationStateChanged` so the next query re-evaluates. |
| fresh-consume signal | bool (local or field) | Set when `TryConsumeFragmentTokenAsync` returns a fresh, valid entry | True ⇒ a `Anonymous→Authenticated` transition occurred this evaluation. |
| `_alreadyBroadcast` | bool | Idempotency guard | The fresh-consume broadcast fires **at most once** per sign-in; prevents flicker/duplicate events (FR-006). |

## State machine — fresh-consume re-broadcast

```text
                 ┌─────────────────────────────────────────────┐
                 │ GetAuthenticationStateCoreAsync()           │
                 └─────────────────────────────────────────────┘
                                  │
         TryConsumeFragmentTokenAsync()
                                  │
        ┌─────────────────────────┼─────────────────────────┐
        │ fresh valid token       │ no/expired fragment      │
        ▼                         ▼                          ▼
  store + clear staging     fall back to cache         (no token)
  freshConsume = true       freshConsume = false       freshConsume = false
        │                         │                          │
        ▼                         ▼                          ▼
   Authenticated            Authenticated/Anonymous     Anonymous
        │                         │                          │
        ▼                         ▼                          ▼
  after task resolves:       NO broadcast               NO broadcast
  if freshConsume && authed
     && !_alreadyBroadcast →
        _alreadyBroadcast = true
        NotifyAuthenticationStateChanged()  ← re-query falls to cache, no re-consume
```

## Validation rules (from FRs)

- Broadcast **iff** fresh token consumed **and** resulting state authenticated (FR-001, FR-004).
- Never broadcast for absent/expired/invalid token (FR-004) → no anonymous flicker (FR-005).
- At most one broadcast per sign-in; token consumed at most once (FR-006).
- No change to token issuance/transport or staging format (FR-009).
