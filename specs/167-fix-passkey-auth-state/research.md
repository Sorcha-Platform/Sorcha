# Phase 0 Research: Fix Passkey Login Auth-State Notification

**Feature**: 167-fix-passkey-auth-state | **Date**: 2026-06-27

This document resolves the unknowns from the Technical Context and records the design decisions behind
the fix. The spec carried **no** `[NEEDS CLARIFICATION]` markers; the research below is therefore
focused on root-cause confirmation and the safest mechanism for the re-announcement.

## R1. Root cause of "not signed in" after passkey login

**Decision**: The defect is a missing `AuthenticationStateChanged` broadcast on the fresh fragment-token
consume path, not a token-issuance or storage bug.

**Evidence** (`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`):
- `GetAuthenticationStateAsync()` (line 42-45) memoises the result in `_authStateTask` and returns the
  same task on every subsequent call until it is explicitly nulled.
- `TryConsumeFragmentTokenAsync()` (line 107-155) reads the staged token, validates it, stores it via
  `ITokenCache.StoreTokenAsync`, and clears the staging — but **does nothing to notify subscribers**.
- `NotifyAuthenticationStateChanged()` (line 95-99) — the only path that raises the change event and
  resets `_authStateTask` — is called by `TokenRefreshService` (background refresh), `OrgSwitcher`,
  `LogoutConfirmDialog`, and `MainLayout`'s tier-upgrade path, **but never by the login/fresh-consume
  path**.

**Consequence**: If any consumer evaluated auth state while still anonymous (e.g. a component rendered
before the fragment token finished consuming, or a render path that resolved before the cache write),
its cached `_authStateTask` resolves anonymous and is never invalidated. `AuthorizeView` /
`AuthorizeRouteView` only re-render when `AuthenticationStateChanged` fires. A manual reload constructs
a fresh provider and re-queries, which is why reload "fixes" it.

**Rationale**: This matches the feature description verbatim — *"re-notify auth state after
fragment-token consume."* The fix is to add the missing broadcast, scoped to the fresh-consume case.

**Alternatives considered**:
- *Make Profile/Security poll or re-query on an interval* — rejected: treats the symptom, adds load and
  flicker, and leaves every other consumer broken.
- *Force a full navigation/reload after login* — rejected: defeats the SPA handoff, causes a visible
  white flash, and re-introduces the token-bounce race the fragment handoff was built to avoid.
- *Eagerly consume the token in `MainLayout.OnAfterRenderAsync`* — rejected: `MainLayout` is not the
  only entry point, the consume belongs in the provider that owns the one-time token, and this would
  still miss components rendered before `MainLayout`.

## R2. Where to raise the re-broadcast without recursion

**Decision**: Detect a *fresh consume* inside `GetAuthenticationStateCoreAsync`, and raise
`NotifyAuthenticationStateChanged` **once**, **after** the current auth-state task has completed (via a
continuation / fire-after-return), guarded by an `_alreadyBroadcast` flag.

**Rationale**:
- `NotifyAuthenticationStateChanged()` nulls `_authStateTask` and calls
  `GetAuthenticationStateAsync()` again. Calling it *synchronously inside*
  `GetAuthenticationStateCoreAsync` would re-enter the core method while the first task is still
  in-flight — a re-entrancy hazard. Firing it *after* the first task resolves means the re-query falls
  through to the already-written cache (staging is cleared), so it cannot re-consume the one-time token
  and returns the authenticated state cleanly.
- A boolean guard makes the broadcast idempotent across multiple `GetAuthenticationStateAsync()` callers
  that may arrive before the first task completes (they already share the one memoised task).

**Mechanism options weighed**:
- *`Task.ContinueWith` / `async` continuation on the stored task* — chosen. Cleanest: attach the
  one-shot notify to the completion of `_authStateTask`.
- *Raise inside the core method before returning* — rejected (re-entrancy, see above).
- *Expose a public `ConsumeFragmentAndNotifyAsync()` for `MainLayout` to call* — rejected as the primary
  mechanism: it leaks the consume concern out of the provider and depends on a specific caller. May be
  retained only as a thin convenience wrapper if a deterministic call site is needed for tests.

## R3. Gating — fire only on a real fresh sign-in (no flicker, FR-004/FR-005)

**Decision**: The broadcast fires **only** when *all* of these hold in a single
`GetAuthenticationStateCoreAsync` execution: (a) `TryConsumeFragmentTokenAsync` returned a non-null,
non-expired entry (a *fresh* token was present and valid), and (b) the resulting state is authenticated.
It does **not** fire when the token came from the cache fallback, when no token was present, or when the
token was expired/invalid.

**Rationale**: Maps directly to FR-004 (announce only on successful consume + valid session) and FR-005
(no announcement, hence no flicker, on anonymous navigation). Distinguishing "fresh consume" from "cache
fallback" requires `TryConsumeFragmentTokenAsync` to signal that it actually consumed something — a
local flag set when it returns a fresh entry is sufficient.

**Alternatives considered**:
- *Always broadcast on every `GetAuthenticationStateCoreAsync`* — rejected: fires on anonymous loads and
  cache hits, risks flicker and redundant work, violates FR-005/FR-006.

## R4. Idempotency & one-time-token safety (FR-006)

**Decision**: Rely on two layers: (1) the existing staging clear in `TryConsumeFragmentTokenAsync` (line
145) means a second pass finds no staged token and falls back to cache — no second consume; (2) the
`_alreadyBroadcast` guard means the change event is raised at most once per fresh sign-in.

**Rationale**: Satisfies FR-006 ("one-time token MUST NOT be consumed more than once; repeated
announcements MUST NOT cause flicker"). The store-before-clear ordering already preserved by the current
code is unchanged, so the FragmentTokenHandler fallback semantics are retained.

## R5. Coverage of all sign-in methods (FR-007)

**Decision**: Place the fix in the shared fragment-consume path, not in any passkey-specific code.

**Rationale**: Passkey, social/SSO, and password sign-ins all return to the web app through the same
`/app/#token=…&refresh=…&returnUrl=…` fragment handoff and the same
`CustomAuthenticationStateProvider`. Fixing the shared consume path fixes the whole class (US2 / FR-007)
with no per-method code.

## R6. Testing approach

**Decision**: Two layers.
- **Unit** (`tests/Sorcha.UI.Core.Tests`, xUnit + Moq + FluentAssertions): drive
  `CustomAuthenticationStateProvider` with a mocked `IJSRuntime` (staged fragment token), mocked
  `ITokenCache`, and a subscriber to `AuthenticationStateChanged`; assert the event fires exactly once
  on fresh consume, does **not** fire on anonymous/cache-only/expired-token paths, and that the token is
  stored exactly once (no double consume). An existing `TokenRefreshServiceTests.cs` already covers the
  background-refresh notify path and is the style template.
- **E2E** (`tests/Sorcha.UI.E2E.Tests`, Playwright): sign in (reuse `GlobalAuthSetup.PerformLoginAsync`,
  which already documents the token-bounce race), then navigate to Profile and Security and assert the
  signed-in experience renders **without a reload**; plus an anonymous-navigation no-flicker check.

**Rationale**: Unit tests lock the notification contract and idempotency cheaply and deterministically;
the E2E test proves the user-visible Success Criteria (SC-001..SC-005). The existing E2E retry logic for
the token-bounce race is a signal the same area was previously flaky — the fix should let that retry
count drop.

## Open questions / follow-ups

- The cited design doc `docs/superpowers/specs/2026-06-27-auth-login-hardening-design.md` (Workstream C)
  was absent when the spec was authored. If it lands, reconcile R2's mechanism (continuation vs. explicit
  call site) against any prescribed approach. No blocker for implementation.
