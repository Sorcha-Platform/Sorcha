# Quickstart & Validation: Fix Passkey Login Auth-State Notification

**Feature**: 167-fix-passkey-auth-state | **Date**: 2026-06-27

A run/validation guide proving the fix end-to-end. Implementation detail lives in the eventual
`tasks.md`; behavioural expectations live in [contracts/auth-state-notification.md](./contracts/auth-state-notification.md)
and [data-model.md](./data-model.md).

## Prerequisites

- .NET 10 SDK, Docker Desktop
- A user account with a **registered passkey** in the target environment
- Built solution: `dotnet restore && dotnet build`

## Affected component

`CustomAuthenticationStateProvider`
(`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Services/Shared/Authentication/CustomAuthenticationStateProvider.cs`).
Profile (`Sorcha.UI.Web.Client/Pages/MyProfile.razor`) and Security
(`Sorcha.UI.Web.Client/Pages/Security.razor`) are **verification targets** — not modified.

## 1. Unit validation (fast, deterministic)

Locks the notification contract (C1–C5) with mocked `IJSRuntime` + `ITokenCache`.

```bash
dotnet test tests/Sorcha.UI.Core.Tests \
  --filter "FullyQualifiedName~CustomAuthenticationStateProvider"
```

**Expected** — all green:

| Scenario | Expectation (contract) |
|----------|------------------------|
| Fresh staged token consumed | `AuthenticationStateChanged` fires **once**, final state authenticated (C1) |
| Cache-only (no fragment) | No consume-path event; state authenticated (C2) |
| Expired / absent token | No signed-in event; state anonymous (C3) |
| Second `GetAuthenticationStateAsync()` after consume | No second `StoreTokenAsync`; no extra event (C4) |
| `NotifyAuthenticationStateChanged()` direct call | Still resets + re-broadcasts (C5, existing behaviour) |

## 2. End-to-end validation (user-visible outcome)

Proves SC-001..SC-005 against the running web app.

```bash
# Bring the stack up
docker-compose up -d
# Run the UI E2E suite (or the auth-focused filter)
dotnet test tests/Sorcha.UI.E2E.Tests --filter "FullyQualifiedName~Auth"
```

**Manual smoke (mirrors the spec's Independent Tests):**
1. Sign out / start anonymous in the web app (`http://localhost/app`).
2. Sign in with the **passkey**; complete the WebAuthn ceremony; let the app return.
3. Without reloading, navigate to **Profile** → it renders the signed-in profile (not "not signed in").
4. Without reloading, navigate to **Security** → it renders security settings (not "not signed in").
5. Confirm **no** brief "signed in → signed out" flicker occurred during the landing.

**Expected**: Both pages show the signed-in experience on first visit, no manual reload (SC-001, SC-003).

## 3. Regression / edge checks

| Check | Expected |
|-------|----------|
| Anonymous direct navigation to a protected page (no token) | Prompts sign-in as today; no spurious signed-in flicker (FR-005, SC-004) |
| Re-login to switch org/account | App reflects the **new** session; no stale prior session; no transient "not signed in" on Profile/Security (FR-008, SC-004) |
| Handoff carries expired/invalid token | App stays/falls back to anonymous and routes to sign-in; not stuck flapping (Edge cases) |
| Non-passkey method through the same handoff (social/SSO, password) | Lands signed-in on Profile/Security without reload (FR-007, SC-005) |

## Success signals

- All unit assertions in §1 pass.
- E2E + manual smoke in §2 show signed-in Profile/Security with no reload.
- §3 regressions hold; the `GlobalAuthSetup` token-bounce retry count does not increase (ideally drops).
