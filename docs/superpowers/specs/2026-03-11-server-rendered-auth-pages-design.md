# Server-Rendered Auth Pages Design

**Date:** 2026-03-11
**Status:** Approved
**Scope:** Move authentication UI (login, signup, OAuth callbacks, email verification, password reset, logout) from Blazor WASM to server-rendered Razor Pages in the Tenant Service.

---

## Problem

All auth pages (login, signup) are Blazor WASM components, requiring a ~15-20MB .NET runtime download before users see a form. Additionally:

- Social OAuth callback page (`/auth/social/callback`) is missing entirely
- No email verification landing page exists
- No password reset flow exists
- OIDC callback has no UI landing page
- Auth pages served from `/app/auth/*` conflate public pages with the authenticated SPA

## Solution

Server-rendered Razor Pages in the Tenant Service, routed via YARP at `/auth/*`. The Tenant Service already owns all auth domain logic, so pages call services directly — no HTTP round-trip.

## Architecture

```
┌─────────────────────────────────────────────────────┐
│                   API Gateway (YARP)                 │
│                                                      │
│  /auth/*          → Tenant Service (Razor Pages)    │
│  /api/auth/*      → Tenant Service (JSON APIs)      │
│  /app/*           → Blazor WASM (authenticated SPA) │
│  /api/*           → Backend services                 │
└─────────────────────────────────────────────────────┘
```

- `/auth/*` — server-rendered HTML, no auth policy, public
- `/api/auth/*` — JSON APIs, unchanged, for CLI/mobile/service clients
- `/app/*` — Blazor WASM, authenticated content only

## Token Handoff: Server Pages → Blazor WASM

After successful auth, the server redirects to:

```
/app/#token={accessToken}&refresh={refreshToken}&returnUrl={encodedPath}
```

**Why fragment (Option A):**
- Fragments are never sent to the server (HTTP spec) — no proxy/log exposure
- No new API endpoints needed (vs cookie exchange)
- Simple client-side pickup
- Note: JWTs are typically ~800-1200 bytes each; well within browser URL limits (~2KB fragment is safe, browsers support 2083+ chars total)

**Blazor-side pickup:**
- On WASM boot, check `window.location.hash` for token params
- Store in `ITokenCache` (localStorage)
- `history.replaceState` to clear fragment immediately
- `NotifyAuthenticationStateChanged()` to update auth state
- Navigate to `returnUrl` if present, otherwise `/dashboard`

**Return URL preservation:**
- When Blazor redirects unauthenticated users to `/auth/login`, it appends `?returnUrl=/app/registers/123`
- The login page preserves `returnUrl` through form submissions (hidden field)
- On success, `returnUrl` is included in the fragment redirect
- Server validates `returnUrl` is a relative path (no open redirect)

**Token refresh** stays in Blazor — background timer calls `POST /api/auth/token/refresh`. On refresh failure, full-page redirect to `/auth/login`.

## Pages

### Login (`/auth/login`)

Server-rendered form with three states:

1. **Initial** — email + password fields, passkey sign-in button, link to signup
2. **2FA** — TOTP code input or passkey assertion (when `TwoFactorLoginResponse` returned)
3. **Error** — inline validation messages

Page model contains login logic inline (matching existing `AuthEndpoints.cs` pattern): looks up `UserIdentity` by email, verifies BCrypt password hash, checks 2FA requirement, issues tokens via `ITokenService`.
Passkey sign-in uses vanilla JS (`navigator.credentials.get()`) posting to `/api/auth/passkey/assertion/verify`.
On success: redirect `/app/#token=...&refresh=...&returnUrl=...`

### Signup (`/auth/signup`)

Three registration methods:

1. **Passkey** — display name + email → JS WebAuthn → token → redirect
2. **Social** — provider buttons (Google, Microsoft, GitHub, Apple) → OAuth redirect
3. **Email/password** — form fields → validates via `IPasswordPolicyService`, creates `UserIdentity` with BCrypt hash, issues tokens via `ITokenService` → redirect

Link to `/auth/login` for existing users.

### Social Callback (`/auth/social/callback`)

Receives `?code=...&state=...` from OAuth provider.
`OnGetAsync` calls `ISocialLoginService.ExchangeCodeAsync()`.
Shows "Completing sign-in..." spinner during processing.
On success: redirect `/app/#token=...&refresh=...`
On failure: error message + link to `/auth/signup`

### OIDC Callback (`/auth/oidc/callback`)

Receives `?code=...&state=...` from org IDP.
Calls `IOidcExchangeService.ExchangeAuthorizationCodeAsync()`.
If `requiresProfileCompletion`: renders inline profile form.
On success: redirect `/app/#token=...&refresh=...`

### Verify Email (`/auth/verify-email`)

GET-only page receiving `?token=...`.
Calls `IEmailVerificationService.VerifyAsync(token)`.
Renders success or error message.
Links to `/auth/login`.

### Logout (`/auth/logout`)

Two-step page:

1. **GET** — renders "Sign out?" confirmation with a POST form button
2. **POST** — Blazor app calls this via `forceLoad` navigation with refresh token passed in the fragment (never as query param). Page JS reads the fragment, submits it as a hidden form field to the POST handler. The handler revokes the token via `ITokenRevocationService` and renders "You've been signed out" with link to `/auth/login`.

Refresh tokens are never passed as query parameters (they'd appear in server logs, browser history, and referrer headers).

### Reset Password (`/auth/reset-password`)

Two states:

1. **Request** (no token) — email input, sends reset link via `IPasswordResetService`
2. **Reset** (`?token=...`) — new password form, validates and updates

*Note: `IPasswordResetService` and backing logic are new — do not exist yet.*

### Error (`/auth/error`)

Generic error page for auth flow failures. Displays a user-friendly message with a link back to `/auth/login`. Used as the fallback when callbacks fail or tokens are invalid.

## File Structure

### New Files in Tenant Service

```
Sorcha.Tenant.Service/
├── Pages/
│   ├── _ViewImports.cshtml
│   ├── Shared/
│   │   └── _AuthLayout.cshtml
│   ├── Auth/
│   │   ├── Login.cshtml + Login.cshtml.cs
│   │   ├── Signup.cshtml + Signup.cshtml.cs
│   │   ├── SocialCallback.cshtml + SocialCallback.cshtml.cs
│   │   ├── OidcCallback.cshtml + OidcCallback.cshtml.cs
│   │   ├── VerifyEmail.cshtml + VerifyEmail.cshtml.cs
│   │   ├── Logout.cshtml + Logout.cshtml.cs
│   │   ├── ResetPassword.cshtml + ResetPassword.cshtml.cs
│   │   └── Error.cshtml + Error.cshtml.cs
│   └── _ViewStart.cshtml
└── wwwroot/
    ├── css/
    │   ├── bootstrap.min.css
    │   └── auth.css
    └── js/
        └── webauthn.js
```

### Modified Files

| File | Change |
|------|--------|
| `Sorcha.Tenant.Service/Program.cs` | Add `AddRazorPages()`, `UseStaticFiles()`, `MapRazorPages()` |
| `Sorcha.ApiGateway/appsettings.json` | Add YARP routes for `/auth/*` and static files |
| `Sorcha.UI.Web.Client/Program.cs` | Add fragment token pickup on boot |
| `Sorcha.UI.Web.Client/Routes.razor` | Update `RedirectToLogin` to use `/auth/login` with `forceLoad` |

### Removed Files

| File | Reason |
|------|--------|
| `Sorcha.UI.Web.Client/Pages/Login.razor` | Replaced by server page |
| `Sorcha.UI.Web.Client/Pages/PublicSignup.razor` | Replaced by server page |
| `Sorcha.UI.Web.Client/Pages/Logout.razor` | Replaced by server page |
| `Sorcha.UI.Web.Client/Components/Layout/AuthLayout.razor` | No auth pages remain in WASM |
| `Sorcha.UI.Web.Client/Components/RedirectToLogin.razor` | Replaced by `forceLoad` navigation |

### Removed Methods from UI.Core

`AuthenticationService` in `Sorcha.UI.Core`: Remove `LoginAsync()`, `LoginWithTwoFactorAsync()`, `LoginWithPasskeyAsync()`, `HandleSocialSignup()` and related methods that perform login/registration from the WASM side. Keep `LogoutAsync()` (which now does `forceLoad` navigation to `/auth/logout`) and token refresh methods.

## CSS Strategy

Clone a subset of existing UI styles into `auth.css`:

- Card styles (`.login-card`, `.signup-card`, header gradient)
- Background gradient (`#f5f7fa → #c3cfe2`)
- Color palette (`#667eea`, `#764ba2`, `#1b6ec2`)
- Form field styles (Bootstrap 5 classes)
- Divider and social button styles
- Copy `bootstrap.min.css` from UI project

No MudBlazor dependency. Plain HTML + CSS.

## YARP Routes

Add to `appsettings.json` before admin-ui catch-all routes:

```json
"auth-pages-route": {
    "ClusterId": "tenant-cluster",
    "Match": { "Path": "/auth/{**catch-all}" },
    "Transforms": [{ "PathPattern": "/auth/{**catch-all}" }]
},
"auth-static-route": {
    "ClusterId": "tenant-cluster",
    "Match": { "Path": "/auth-static/{**catch-all}" },
    "Transforms": [{ "PathRemovePrefix": "/auth-static" }]
}
```

No `AuthorizationPolicy` — these are public pages.

**Static file serving:** The Tenant Service calls `app.UseStaticFiles()` which serves from its own `wwwroot/`. Through the gateway, CSS/JS are accessed at `/auth-static/css/auth.css` (the `PathRemovePrefix` strips `/auth-static`, so the Tenant Service sees `/css/auth.css` → `wwwroot/css/auth.css`). The `_AuthLayout.cshtml` references assets using this prefix:

```html
<link rel="stylesheet" href="/auth-static/css/bootstrap.min.css" />
<link rel="stylesheet" href="/auth-static/css/auth.css" />
<script src="/auth-static/js/webauthn.js"></script>
```

## Antiforgery / CSRF

Razor Pages POST forms use ASP.NET Core's built-in antiforgery tokens (`@Html.AntiForgeryToken()` / `[ValidateAntiForgeryToken]`). This works through YARP because:

- The antiforgery cookie is set with `Path=/auth` so it's scoped to auth pages
- YARP forwards cookies transparently
- The token in the form hidden field and the cookie are both forwarded to the Tenant Service
- Configure antiforgery in `Program.cs`:

```csharp
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "Sorcha.Auth.AF";
    options.Cookie.Path = "/auth";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
```

Passkey and social login flows use JS `fetch()` calls to API endpoints (which use Bearer tokens, not cookies) — antiforgery is not needed for those paths.

## Integration Points

Razor Pages call domain services directly (no HTTP). These are the actual service interfaces from the Tenant Service codebase:

```
Login.cshtml.cs         → IOrganizationService (user lookup), ITokenService (JWT issuance),
                           ITotpService (2FA verification), IPasskeyService (passkey 2FA)
Signup.cshtml.cs        → IPublicUserService (passkey/social user creation),
                           IPasswordPolicyService (NIST validation), ITokenService (JWT issuance)
SocialCallback.cshtml.cs → ISocialLoginService.ExchangeCodeAsync()
OidcCallback.cshtml.cs  → IOidcExchangeService.ExchangeAuthorizationCodeAsync(),
                           IOidcProvisioningService (user provisioning)
VerifyEmail.cshtml.cs   → IEmailVerificationService.VerifyAsync()
Logout.cshtml.cs        → ITokenRevocationService (existing)
ResetPassword.cshtml.cs → IPasswordResetService (new), IPasswordPolicyService (validation)
```

Login page model replicates the logic currently in `AuthEndpoints.cs` (BCrypt password verification, 2FA check, token issuance). Consider extracting a shared `ILoginService` to avoid duplication between the Razor Page and the API endpoint — both would call the same service.

## New Services

### ILoginService

Extracts login logic from `AuthEndpoints.cs` into a reusable service. Both the API endpoint and the Login Razor Page call this service, eliminating duplication.

```csharp
public interface ILoginService
{
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default);
}

public record LoginResult(
    bool Success,
    TokenResponse? Tokens = null,
    TwoFactorLoginResponse? TwoFactorChallenge = null,
    string? Error = null);
```

### IRegistrationService

Extracts self-registration logic from `AuthEndpoints.cs`. Used by both the API endpoint and the Signup Razor Page.

```csharp
public interface IRegistrationService
{
    Task<RegistrationResult> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default);
}

public record RegistrationResult(
    bool Success,
    TokenResponse? Tokens = null,
    Dictionary<string, string[]>? ValidationErrors = null);
```

### IPasswordResetService

```csharp
public interface IPasswordResetService
{
    Task<Result> RequestResetAsync(string email);
    Task<Result> ValidateTokenAsync(string token);
    Task<Result> ResetPasswordAsync(string token, string newPassword);
}
```

- Generates URL-safe token (32 bytes, base64), 1-hour TTL
- Stores hashed token on user entity
- Sends email via existing `IEmailSender`
- Validates against NIST password policy + HIBP on reset

## Testing

| Layer | Scope | Approach |
|-------|-------|----------|
| Unit | Page model handlers | xUnit + Moq — mock domain services |
| Integration | HTML rendering + form POST + redirects | `WebApplicationFactory` + `HttpClient` |
| E2E | Full login → Blazor token pickup | Playwright |
| Regression | Existing API endpoints unchanged | Existing test suites |

Tests added to existing `Sorcha.Tenant.Service.Tests` project.

## Rollback

- API endpoints unchanged — CLI/mobile/service clients unaffected
- No database or model changes (except password reset token field)
- YARP routes can be reverted, Blazor pages restored if needed
