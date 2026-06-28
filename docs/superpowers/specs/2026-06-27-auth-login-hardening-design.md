# Auth / Login Hardening — Design

**Date:** 2026-06-27
**Status:** Approved (design) — pending implementation plan
**Scope:** Sorcha web (`Sorcha.UI.Web` + `Sorcha.UI.Web.Client`), PWA (`Sorcha.Wallet.Pwa`), Tenant
Service (`Sorcha.Tenant.Service`), shared component library (`Sorcha.UI.Components.User`).

## Summary

Three related fixes/features on the authentication surface, shipped in sequence:

- **A — Social provider icons** on the login/signup buttons (Apple / GitHub / Google / Microsoft).
- **C — Passkey "not logged in" bug**: after a passkey login, the web Profile and Security pages
  report the user as not signed in and the Security page errors (401).
- **B — Account linking + step-up**: replace the current *silent* auto-link of an unconnected
  social with an explicit, step-up-gated "link to your existing account?" flow; surface 2FA
  enrolment; consolidate the proactive auth-management UI into shared components.

The guiding architectural decision: **every auth-management surface is a host-agnostic shared
Blazor component** in `Sorcha.UI.Components.User/Components/Security/`, depending only on the existing
shared client services and cascading `AuthenticationState`. **Web hosts them now; the apps host the
identical components later** (Step 2, scoped but not built in this effort).

## Goals

1. Login/signup social buttons carry the provider's brand icon.
2. A passkey login leaves the user reliably authenticated on every page, including Profile and
   Security, with authorized API calls succeeding.
3. Logging in with an unconnected social that matches an existing account no longer silently links;
   it requires proof of the existing account (passkey / existing social / password + 2FA) before
   linking. Cancel aborts cleanly.
4. 2FA enrolment is reachable from the web Security page (the backend is already complete).
5. The proactive management surfaces (add-social, 2FA enrol, passkey management) live in the shared
   library so the apps can reuse them unchanged in Step 2.

## Non-Goals

- Building the PWA/app Security surface now (Step 2 — scoped below, deferred).
- Native Android Credential Manager passkeys (tracked separately; WebView limitation).
- Changing how a social login with **no** matching account behaves (web still creates a new account;
  the PWA still routes new users to web signup).
- Rewriting the OAuth provider integration, the challenge ladder, or the TOTP engine — all reused.

---

## Workstream A — Social provider icons (quick, standalone PR)

**PWA** — `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor` (lines 44–50): the social buttons are
Blazor `MudButton`s rendered from `_providers`. Add `StartIcon="@ProviderIcon(provider)"` using the
existing brand-icon map already used in `SocialLinksSection.razor`:

```csharp
private static string ProviderIcon(string provider) => provider switch
{
    "Google"    => Icons.Custom.Brands.Google,
    "GitHub"    => Icons.Custom.Brands.GitHub,
    "Microsoft" => Icons.Custom.Brands.Microsoft,
    "Apple"     => Icons.Custom.Brands.Apple,
    _           => Icons.Material.Filled.Public,
};
```

**Web** — `src/Services/Sorcha.Tenant.Service/Pages/Auth/Login.cshtml` (lines 98–108) and
`Signup.cshtml` (lines 66–84) are **server-rendered Razor pages**, not Blazor, so MudBlazor icons are
unavailable. Add a small inline brand-SVG snippet (a Razor partial or a `provider → svg` switch in the
page) keyed by the same provider strings, rendered inside the existing `.social-btn` button before the
"Continue with {provider}" text.

Provider key strings are case-sensitive and come from `GET /api/auth/social/providers`
(`SocialLoginService.GetConfiguredProviderNames()`): `"Google"`, `"Microsoft"`, `"GitHub"`, `"Apple"`.

**Acceptance:** each rendered social button shows the correct brand mark on web login, web signup, and
the PWA sign-in screen; unknown providers fall back to a neutral icon.

---

## Workstream C — Passkey auth-state bug (bug-fix PR)

### Root cause

On the **web** host, passkey login completes on the server-rendered `/auth/login` page (webauthn.js →
`POST /api/auth/passkey/assertion/verify`), which returns tokens and redirects to `/app/#token=…&refresh=…`.
`fragment-handoff.js` stashes the fragment into local storage before Blazor boots, and
`CustomAuthenticationStateProvider.GetAuthenticationStateAsync()` consumes it
(`TryConsumeFragmentTokenAsync`). The provider caches its result (`_authStateTask ??= …`) and **does
not raise `NotifyAuthenticationStateChanged`** after the token is consumed/persisted. Net effect: the
first authorized render and `AuthenticatedHttpMessageHandler` observe stale anonymous state, so:

- Profile / Security render as "not logged in", and
- `AuthenticatedHttpMessageHandler` resolves no token for the active profile → requests go out without
  a Bearer → `GET /api/me/auth-methods` (called by `SecurityHome.razor`) returns **401**.

### Fix

1. In the web fragment/passkey consumption path, after the token is persisted under the **active
   profile**, raise `NotifyAuthenticationStateChanged(...)` with the authenticated state (and ensure
   the cached `_authStateTask` is reset so it re-evaluates), so the cascading auth state and the
   message handler both see the token before the first authorized render.
2. Confirm the token is written under the **same profile key** that `AuthenticatedHttpMessageHandler`
   reads (`GetActiveProfileNameAsync()` → `BrowserTokenCache`); fix any key mismatch.
3. Verify the **PWA** path: `SignIn.razor.HandleResult` already calls `AuthState.NotifyChanged()` on
   success, so the PWA is expected to be unaffected — confirm there is no parallel passkey entry point
   that persists without notifying, and align if found.

### Tests

- Playwright (web): passkey login → navigate to Profile → asserts signed-in identity is shown →
  navigate to Security → asserts the page loads and `GET /api/me/auth-methods` returns 200.
- Unit: the auth-state provider raises a change notification after fragment-token consumption.

### Files

- `Sorcha.UI.Web.Client` `CustomAuthenticationStateProvider.cs` (fragment consumption ~lines 107–155),
  `AuthenticatedHttpMessageHandler.cs` (token resolution ~lines 33–83),
  `wwwroot/js/fragment-handoff.js`, `app/index.html`.
- `SecurityHome.razor` (the 401 call site, ~line 116).

---

## Workstream B — Account linking + step-up (web; the main effort)

### B-backend — replace silent auto-link

Today `PlatformUserService.ResolveOrCreateSocialUserAsync` (lines 266–390): if `(provider, subject)`
is already linked it signs in; **else if the social email matches an existing account and both emails
are verified it silently links and signs in**; else it creates a new account (web) or refuses (wallet).
The silent auto-link is the security hole (control of the matching social ⇒ control of the Sorcha
account, with no proof of the existing account).

**Change:** when `(provider, subject)` is unconnected **and** the verified social email matches an
existing account, return a new **`LinkRequired`** outcome carrying a **signed, short-lived
"link-pending" token** instead of a session. The token encodes `{ provider, subject, email,
displayName, targetPlatformUserId, exp(~5 min) }`, signed with the same HMAC scheme used for the TOTP
login token (`TotpService.GenerateLoginTokenAsync` pattern). No new persistence.

**New endpoint** `POST /api/auth/social/link-confirm`:
- Body: the link-pending token + a step-up **challenge token** scoped to the new operation.
- Validates both tokens, asserts the challenge subject == the link-pending `targetPlatformUserId`,
  calls `ISocialLinkService.LinkAsync(targetUserId, provider, subject, email, displayName)`, then
  issues the full JWT (same path as a normal social login success).
- Collision handling reuses `LinkAsync`'s existing results (`AlreadyLinkedToDifferentUser`,
  `EmailCollision`) → surfaced as 409.

**Challenge ladder:** add `ScopedOperation.LinkSocial` to `AuthChallengeEnums.cs`. The existing
`AuthChallengeService` picks the strongest available method (TOTP → Password → Passkey → ReOAuth);
for LinkSocial the accepted proof is **passkey, an existing linked social (ReOAuth), or password+2FA**.
Policy: if the account has 2FA enrolled, password alone is insufficient (password+TOTP); if the
account has only a password and no 2FA, password is the account's existing strength and is accepted
(we do not raise the bar above the account's own configured methods). This policy is stated explicitly
so it is testable.

**No-match case unchanged:** social email matching no account → web creates a new account
(`allowCreate=true`), PWA/wallet routes the user to web signup. No behavioural change.

### B-UI — `LinkExistingAccountPrompt` (shared component)

A new shared component in `Sorcha.UI.Components.User/Components/Security/`:

- Input: the link-pending token (delivered to the host after the social round-trip — web via the
  callback redirect/fragment, the app later via its fragment handler).
- Renders: "You signed in with {provider}. Link it to your existing Sorcha account **{masked email}**?"
  with **Link** and **Cancel**.
- **Link** opens the existing `AuthChallengeDialog` with `operation=LinkSocial`; on a verified
  challenge it calls `POST /api/auth/social/link-confirm`, stores the returned token via the host's
  auth-state mechanism, and proceeds as a normal sign-in success.
- **Cancel** drops the flow (no link, no new account) and returns to the sign-in screen. The
  link-pending token simply expires.

The component depends only on injected client services (`AuthMethodsClientService` /
social-link client + `AuthChallengeDialog`) and cascading `AuthenticationState` — no host-specific
routing. The web host renders it on a post-callback route; the app renders the same component in Step 2.

### B-management — consolidate proactive surfaces into the shared library

- **Add-social** (`SocialLinksSection.razor`, `intent=link` → `ISocialLinkService.LinkAsync`) already
  exists; ensure the canonical copy lives in `Sorcha.UI.Components.User/Components/Security/` and the
  web host references it (remove any divergent copy under `Sorcha.UI.Web.Client/Components/Settings/`).
- **2FA enrol**: the backend is complete (`TotpService` / `TotpEndpoints`: setup → QR → verify →
  backup codes, challenge-gated disable). Add/relocate the enrolment component (secret QR via the
  existing `HybridQrAffordance`, code verify, backup-code display) to the shared library and make it
  reachable from the web Security page.
- **Passkey management** (`PasskeysSection.razor`) — confirm it lives in the shared library.

The web Security page (`SecurityHome.razor`) becomes a thin host composing these shared sections.

### Tests

- Tenant Service integration: unconnected-social + email-match → `LinkRequired` + link-pending token
  (no session issued); `link-confirm` with a valid challenge → linked + JWT; `link-confirm` with a
  mismatched/absent challenge → 401/403; collision → 409.
- Unit: link-pending token sign/verify + expiry; `LinkSocial` challenge method selection per the
  stated policy.
- bUnit: `LinkExistingAccountPrompt` (Link → challenge → confirm; Cancel → abort).
- Playwright (web): social login on an unconnected provider matching an existing account → prompt →
  step-up → linked + signed in; Cancel → back at sign-in, unlinked.

---

## Step 2 — App parity (scoped, NOT built in this effort)

Because the components and client services are shared, the app work is **host-glue, not
re-implementation**. No server changes.

| Piece | Effort | Note |
|---|---|---|
| Register the shared auth client services in PWA DI + a token-bearing `DelegatingHandler` | **S** | PWA uses an IndexedDB token store rather than the web message handler. |
| PWA `Security.razor` page shell + base-relative nav | **S** | Thin host; the `/wallet/` path-prefix rule (base-relative `NavigateTo`) applies. |
| Consolidate the two `webauthn.js` copies and verify passkey interop under the PWA host; TOTP-QR via `HybridQrAffordance` | **M** | The one genuine cross-host risk. |
| Expose 2FA enrol in the PWA (challenge-on-login already works there) | **S** | Server endpoints unchanged. |
| bUnit + Playwright coverage for the PWA Security surface | **M** | Mirror the web tests. |

**Overall Step-2 estimate: ~M (a few focused days), low architectural risk.** The unknowns are
passkey-interop consolidation across hosts and the PWA path-prefix nav; everything else is wiring.

---

## Sequencing

1. **A** — social icons (quick PR).
2. **C** — passkey auth-state bug (quick PR; actively breaks testing today).
3. **B** — linking + step-up + shared-component consolidation (web; the main effort).
4. **Step 2** — app parity (separate, later; scoped above).

## Risks & mitigations

- **Passkey-interop divergence across hosts** (two `webauthn.js` copies) — consolidate as part of B so
  Step 2 inherits one implementation.
- **Step-up policy ambiguity** — the password-vs-password+2FA rule is stated explicitly above and
  covered by a unit test, so it cannot drift silently.
- **Auto-link behaviour change is user-visible** — existing users who relied on silent linking will now
  see a one-time step-up prompt; this is the intended security improvement, documented in the auth
  guide.

## Documentation to update on implementation

- `docs/guides/AUTHENTICATION-SETUP.md` — the new link-on-unconnected-social flow + `LinkSocial`
  challenge; the auto-link policy change.
- Tenant Service README — `POST /api/auth/social/link-confirm`, link-pending token.
- `Sorcha.UI.Components.User/README.md` — the new shared Security components.
- `.claude/skills/sorcha-architecture/SKILL.md` — if the auth endpoints table is affected.
