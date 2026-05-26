# Citizen Wallet PWA — Social + Passkey sign-in

- **Date:** 2026-05-25
- **Status:** Design approved; ready for implementation plan
- **Surface:** `Sorcha.Wallet.Pwa` (citizen wallet PWA, mounted at `/wallet/`) + small changes in `Sorcha.Tenant.Service`
- **Related:** F114 (citizen wallet PWA), F116 (account linking / auth-method management), F126/F128 (council enrol gate + cold-start pairing), F136 (tiered-audience JWT identity model)

## Problem

The PWA can only sign a citizen in with **email + password (+ TOTP 2FA)**, and that surface is buried in a card inside `Settings.razor`. There is no signed-out front door at all: `App.razor` uses a plain `<Router>`/`RouteView` (no `AuthorizeRouteView`, no `CascadingAuthenticationState`), so every page is open and unauthenticated callers just hit failing API calls.

We want **passkey** and **social** sign-in alongside password, presented on a dedicated sign-in screen, with the PWA properly gated behind authentication.

Most of the backend already exists (F116): anonymous passkey assertion and social-login endpoints that issue tokens. The bulk of this work is **PWA client-side** plus **two small backend changes** (Consumer-tier minting and a surface-aware social callback).

## Scope

**In scope (v1):**
- Dedicated sign-in screen at `/signin` with three methods: passkey, social, password (+2FA).
- An auth gate: signed-out access to a protected route redirects to `/signin?returnUrl=…`.
- Social providers: Google, Apple, Microsoft, GitHub — rendered **dynamically** from whichever are enabled per deployment.
- Refresh tokens stored + silent renewal.

**Out of scope:**
- **Sign-up / new-account creation** in the PWA. Login only — accounts are created via the council enrol gate (F126), pairing (F128), or web signup. An unknown social identity is **refused** (with a link to web signup), never auto-created.
- Auth-method *management* in the PWA (the `AuthMethods.razor` stub stays a stub for now; F116's management surface migration is a separate effort).
- Popup/`postMessage` social flow (full-page redirect only in v1).

## Locked decisions

1. **Login only.** No PWA signup; unknown social identity → "Sorry, no account" + link to the Tenant `/auth/signup` page. No silent account creation.
2. **Dedicated sign-in screen + auth gate** (not an enhanced Settings card).
3. **Social return = surface-aware callback redirect** (Approach A). The single fixed OAuth `redirect_uri` (`/auth/social/callback`) is preserved; the PWA's `initiate` carries `surface=wallet`, threaded through OAuth `state`, and the existing Razor callback mints a Consumer-tier token and redirects to `/wallet/#token=…&refresh=…`.
4. **Every PWA sign-in path requests Consumer tier.** Requesting `consumer` is a safe downgrade (never an escalation), so the server honors it without an entitlement fight (F136). This also fixes a latent bug: the PWA password login currently sends no tier hint and therefore mints a **Platform**-tier token, which F136 refuses on `/api/v1/wallet/*`.
5. **Refresh tokens + silent renewal** are in v1.
6. **Ordering with F128:** auth gate first (must be signed in) → `PairingTakeover` (device-presence gate) fires after sign-in for citizens with no device.

## Architecture & components

### `Sorcha.Wallet.Pwa`

- **`Pages/SignIn.razor`** (`@page "/signin"`) — the dedicated screen. Passkey button (hidden when WebAuthn unsupported), dynamic social row (only enabled providers), email + password with the existing 2FA step, and an inline error region. **Visual/layout follows provided screenshots**; this doc fixes the structure underneath.
- **Auth gate** — introduce the idiomatic Blazor path the PWA currently lacks:
  - A token-backed `AuthenticationStateProvider` (modeled on `CustomAuthenticationStateProvider` in `Sorcha.UI.Components.User`) that reads `IAccessTokenStore`.
  - `<CascadingAuthenticationState>` in `App.razor`; switch `RouteView` → `AuthorizeRouteView`.
  - `[Authorize]` on protected pages; `NotAuthorized` redirects to `/signin?returnUrl=…` (base-relative nav — PWA path-prefix rule).
- **`IAuthService` / `AuthService`** gains:
  - `SignInWithPasskeyAsync(string? email = null)` — discoverable-first.
  - `BeginSocialSignInAsync(string provider)` — calls `initiate {surface:"wallet"}`, full-page nav to the returned authorization URL.
  - `CompleteSocialReturnAsync()` — the fragment handler (parse `#token`/`#refresh`, persist, strip fragment).
  - Password path adds `tier:"consumer"` to the request.
- **`PasskeyInteropService`** + **`wwwroot/js/webauthn.js`** — ported from `Sorcha.UI.Web.Client` (copy for v1; candidate to move into the shared lib later). Wrapped behind an interface so unit tests use an in-memory fake.
- **Fragment-before-gate bootstrap.** App startup parses and stores any `#token`/`#refresh` fragment **before** the auth gate evaluates, so a returning social user is not bounced to `/signin` with the token still in the URL fragment. (Mirrors the existing `/app` web-client behaviour.)
- **Refresh / silent renewal.** Store the refresh token (IndexedDB, alongside the access token record). `BearerTokenHandler` gains a 401 → refresh (`POST /api/auth/token/refresh`, body `{refreshToken}`; re-mints the same tier per F136, so a Consumer refresh stays Consumer) → retry path; refresh failure → sign out + `/signin`. `AccessTokenRecord` / the login response DTOs are extended to capture the refresh token.

### `Sorcha.Tenant.Service`

- **Social initiate** (`SocialLoginInitiateRequest`): add optional `Surface` (allowlist: e.g. `wallet` | `app`; default app/none). `ISocialLoginService.GenerateAuthorizationUrlAsync` + the cached state persist `surface`. `SocialAuthCallbackResult` carries it back from `ExchangeCodeAsync`.
- **Social callback** (`SocialCallbackModel.OnGetAsync`): when `surface=="wallet"` →
  - mint **Consumer-tier** (`GenerateUserTokenAsync(..., tier: Tier.Consumer)`),
  - **login-only:** resolve existing accounts only (including match-by-verified-email per the F115 strict-link policy); if the identity maps to no existing account, render a refusal page with a link to `/auth/signup` rather than creating one,
  - redirect to `/wallet/#token=…&refresh=…` (the F128 SetupAddDevice routing gate still applies, but targeting `/wallet`),
  - on failure/refusal, redirect to `/wallet/signin?authError=…` so errors render in the PWA's look.
- **Passkey assertion verify** (`PublicPasskeyAssertionVerifyRequest` + handler): add an optional tier hint; pass `Tier.Consumer` when requested (today it hardcodes the Platform default).
- **Tier through 2FA:** ensure the tier preference set at `/api/auth/login` time rides through the `loginToken` into `verify-2fa` so 2FA completion also mints Consumer.

## Method flows

**Password (+2FA):** unchanged except the PWA `LoginRequest` sends `tier:"consumer"`; tier preference carried through `loginToken` → `verify-2fa`.

**Passkey:** `POST /api/auth/passkey/assertion/options` (no email → discoverable; email entry as fallback) → `PasskeyInteropService.GetCredentialAsync()` runs `navigator.credentials.get()` → `POST /api/auth/passkey/assertion/verify {transactionId, assertionResponse, tier:"consumer"}` → store Consumer token (+ refresh).

**Social:** `POST /api/auth/social/initiate {provider, surface:"wallet"}` → full-page nav to provider → fixed `/auth/social/callback` Razor page (reads surface, mints Consumer, applies login-only refusal) → redirect `/wallet/#token=…&refresh=…` → PWA fragment handler stores the token and routes to `returnUrl`/home.

## Auth-gate scope

**Public (reachable signed-out):** `/signin`, `/enrol` (redeems the `?session=` token from F126/F128 — both an onboarding and a sign-in path; gating it would deadlock first-token acquisition), `CancelledEnrolment`, and the social-return landing (`/wallet/#token=…`).

**Protected (redirect to `/signin?returnUrl=…`):** Home, Devices, Activity, Settings, Profile, Present, Applications, ApplicationInstance, CredentialDetail, Verify. Settings becomes protected — the sign-in card moves out to `/signin`; sign-out stays in Settings.

## Error handling / edge cases

- **Passkey:** `IsWebAuthnSupportedAsync()` → hide the button on unsupported browsers; user-cancel / no discoverable credential → inline message + email fallback.
- **Social:** failures/refusals for `surface=wallet` → `/wallet/signin?authError=…`; the "no account" refusal carries the web-signup link.
- **Token expiry:** silent refresh in `BearerTokenHandler`; refresh failure → sign out + `/signin`.
- **Feedback surface:** `IInlineFeedback` / inline `MudAlert` only — no `ISnackbar` (platform rule, CI-gated).
- **Sign-out:** existing per-device wipe is preserved; the new `AuthenticationStateProvider` must reflect sign-out so the gate re-engages.
- F128 `PairingTakeover` ordering unchanged.

## Testing

**PWA unit / bUnit:** `SignIn.razor` shows the correct methods (passkey hidden when unsupported; only enabled providers); `AuthService` methods with a stubbed `HttpMessageHandler` and an in-memory passkey-interop fake (never mock `IJSRuntime.InvokeAsync<T>` directly — F114 lesson); assert password login sends `tier:"consumer"`; fragment handler parses and persists.

**Backend unit (`Sorcha.Tenant.Service.Tests`, reflection-based static-handler pattern):** assertion-verify mints Consumer on tier hint; social `initiate` persists `surface`; `SocialCallbackModel` branches tier + redirect on `surface=="wallet"`; login-only refusal (`IsNew` + `surface=wallet`) renders the signup link; `surface` allowlist rejects unknown values.

**E2E (`Sorcha.UI.E2E.Tests/Docker/CitizenWallet/`):** new `CitizenWalletSignInPage` page object, `data-testid` on every control. Cases: signed-out hit on a protected route → redirect to `/signin?returnUrl=…`; password happy path → authenticated + a wallet API call succeeds (proves Consumer tier); passkey via Playwright CDP **virtual authenticator**; social **return** leg deterministically (navigate to `/wallet/#token=<minted-consumer-token>&refresh=…`, assert store + land home) with the provider leg mocked/unit-covered; auto console-error / 5xx / CSS-health from the base classes; new routes use base-relative nav; sign-out wipe regression guard still passes.

**Manual:** Docker stack, golden path + edges (cancel passkey, unknown social → refusal + link, expiry → silent refresh) in a browser.

## Follow-ups / not-now

- Popup/`postMessage` social flow (desktop polish).
- Move `webauthn.js` + passkey interop into the shared `Sorcha.UI.Components.User` library if/when the web client and PWA converge.
- Auth-method management surface migration into the PWA (`AuthMethods.razor`).

## Implementation notes

- Land on a dedicated branch off `master` (current working branch `138-federation-trust-hardening` is unrelated).
- `SignIn.razor` visual/layout to be finalised against provided screenshots at build time.
