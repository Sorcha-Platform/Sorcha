# Server-Rendered Auth Pages Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move auth UI (login, signup, OAuth callbacks, email verification, password reset, logout) from Blazor WASM to server-rendered Razor Pages in the Tenant Service, eliminating the ~15MB WASM download for unauthenticated users.

**Architecture:** Razor Pages in the Tenant Service served at `/auth/*` via YARP. Pages call domain services directly (no HTTP round-trip). After auth, tokens are passed to the Blazor SPA via URL fragment redirect (`/app/#token=...`). API endpoints remain unchanged for CLI/mobile clients.

**Tech Stack:** ASP.NET Core Razor Pages, Bootstrap 5, vanilla JS (WebAuthn), YARP reverse proxy, BCrypt, JWT

**Spec:** `docs/superpowers/specs/2026-03-11-server-rendered-auth-pages-design.md`

---

## File Map

### New Files — Tenant Service

| File | Responsibility |
|------|---------------|
| `Services/ILoginService.cs` | Login domain logic (extracted from AuthEndpoints.cs) |
| `Services/LoginService.cs` | Implementation: password verify, 2FA check, token issuance |
| `Services/IRegistrationService.cs` | Registration domain logic (extracted from AuthEndpoints.cs) |
| `Services/RegistrationService.cs` | Implementation: validation, user creation, email verify |
| `Services/IPasswordResetService.cs` | Password reset token generation, validation, reset |
| `Services/PasswordResetService.cs` | Implementation: token gen, email, password update |
| `Pages/_ViewImports.cshtml` | Tag helpers and namespace imports |
| `Pages/_ViewStart.cshtml` | Default layout assignment |
| `Pages/Shared/_AuthLayout.cshtml` | Auth page layout (gradient bg, centered card) |
| `Pages/Auth/Login.cshtml` | Login form HTML |
| `Pages/Auth/Login.cshtml.cs` | Login page model |
| `Pages/Auth/Signup.cshtml` | Signup form HTML |
| `Pages/Auth/Signup.cshtml.cs` | Signup page model |
| `Pages/Auth/SocialCallback.cshtml` | Social OAuth callback HTML |
| `Pages/Auth/SocialCallback.cshtml.cs` | Social callback page model |
| `Pages/Auth/OidcCallback.cshtml` | OIDC callback HTML |
| `Pages/Auth/OidcCallback.cshtml.cs` | OIDC callback page model |
| `Pages/Auth/VerifyEmail.cshtml` | Email verification landing HTML |
| `Pages/Auth/VerifyEmail.cshtml.cs` | Email verification page model |
| `Pages/Auth/Logout.cshtml` | Logout confirmation HTML |
| `Pages/Auth/Logout.cshtml.cs` | Logout page model |
| `Pages/Auth/ResetPassword.cshtml` | Password reset form HTML |
| `Pages/Auth/ResetPassword.cshtml.cs` | Password reset page model |
| `Pages/Auth/Error.cshtml` | Generic auth error HTML |
| `Pages/Auth/Error.cshtml.cs` | Error page model |
| `wwwroot/css/auth.css` | Auth page styles (cloned from Blazor UI) |
| `wwwroot/css/bootstrap.min.css` | Bootstrap 5 (copied from UI project) |
| `wwwroot/js/webauthn.js` | Vanilla JS for passkey flows |

### New Files — Blazor UI

| File | Responsibility |
|------|---------------|
| `Sorcha.UI.Web/wwwroot/js/fragment-handoff.js` | Read tokens from URL fragment, clear fragment |

### Modified Files

| File | Change |
|------|--------|
| `Sorcha.Tenant.Service/Program.cs` | Add RazorPages, StaticFiles, Antiforgery |
| `Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` | Refactor login/register to use ILoginService/IRegistrationService |
| `Sorcha.ApiGateway/appsettings.json` | Add YARP routes for `/auth/*` and `/auth-static/*` |
| `Sorcha.UI.Web.Client/Routes.razor` | Update RedirectToLogin to forceLoad `/auth/login` |
| `Sorcha.UI.Web.Client/Program.cs` | Add fragment token pickup on boot |
| `Sorcha.UI.Core/Services/Authentication/AuthenticationService.cs` | Remove login/register methods, keep refresh/logout |

### Deleted Files

| File | Reason |
|------|--------|
| `Sorcha.UI.Web.Client/Pages/Login.razor` | Replaced by server page |
| `Sorcha.UI.Web.Client/Pages/PublicSignup.razor` | Replaced by server page |
| `Sorcha.UI.Web.Client/Pages/Logout.razor` | Replaced by server page |
| `Sorcha.UI.Web.Client/Components/Layout/AuthLayout.razor` | No auth pages in WASM |
| `Sorcha.UI.Web.Client/Components/RedirectToLogin.razor` | Replaced by inline redirect |

---

## Chunk 1: Service Extraction & Foundation

### Task 1: Extract ILoginService from AuthEndpoints

Extract the inline login logic from `AuthEndpoints.cs` into a reusable service. Both the API endpoint and the upcoming Login Razor Page will call this service.

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Services/ILoginService.cs`
- Create: `src/Services/Sorcha.Tenant.Service/Services/LoginService.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Program.cs`
- Test: `tests/Sorcha.Tenant.Service.Tests/`

- [ ] **Step 1: Create ILoginService interface**

Create `src/Services/Sorcha.Tenant.Service/Services/ILoginService.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of a login attempt.
/// </summary>
/// <param name="Success">Whether login succeeded (tokens issued or 2FA challenge returned).</param>
/// <param name="Tokens">Access and refresh tokens if login completed without 2FA.</param>
/// <param name="TwoFactorRequired">True if 2FA verification is needed before tokens can be issued.</param>
/// <param name="LoginToken">Short-lived token for 2FA verification flow.</param>
/// <param name="AvailableMethods">Available 2FA methods (e.g., "totp", "passkey").</param>
/// <param name="Error">Error message if login failed.</param>
public record LoginResult(
    bool Success,
    TokenResponse? Tokens = null,
    bool TwoFactorRequired = false,
    string? LoginToken = null,
    List<string>? AvailableMethods = null,
    string? Error = null);

/// <summary>
/// Authenticates users with email and password. Handles BCrypt verification,
/// 2FA detection, and JWT token issuance. Used by both the API endpoint
/// and the server-rendered Login Razor Page.
/// </summary>
public interface ILoginService
{
    /// <summary>
    /// Attempts to authenticate a user with email and password.
    /// Returns tokens directly if no 2FA is configured, or a login token
    /// for 2FA verification if TOTP/passkey is enabled.
    /// </summary>
    Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create LoginService implementation**

Create `src/Services/Sorcha.Tenant.Service/Services/LoginService.cs`:

Extract the logic from `AuthEndpoints.cs` lines 165-284 into the service. The service injects `TenantDbContext`, `ITokenService`, `ITotpService`, `IPasskeyService`, `ITokenRevocationService`, and `ILogger<LoginService>`.

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Authenticates users with email/password, checking BCrypt hash,
/// 2FA requirements, and issuing JWT tokens.
/// </summary>
public class LoginService : ILoginService
{
    private readonly TenantDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly ITotpService _totpService;
    private readonly IPasskeyService _passkeyService;
    private readonly ITokenRevocationService _revocationService;
    private readonly ILogger<LoginService> _logger;

    public LoginService(
        TenantDbContext db,
        ITokenService tokenService,
        ITotpService totpService,
        IPasskeyService passkeyService,
        ITokenRevocationService revocationService,
        ILogger<LoginService> logger)
    {
        _db = db;
        _tokenService = tokenService;
        _totpService = totpService;
        _passkeyService = passkeyService;
        _revocationService = revocationService;
        _logger = logger;
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        // Rate limiting check
        if (await _revocationService.IsRateLimitedAsync($"login:{email}", ct))
        {
            return new LoginResult(false, Error: "Too many login attempts. Please try again later.");
        }

        // User lookup
        var user = await _db.UserIdentities
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Email == email && u.Status == "Active", ct);

        if (user is null)
        {
            await _revocationService.IncrementFailedAuthAttemptsAsync($"login:{email}", ct);
            return new LoginResult(false, Error: "Invalid email or password.");
        }

        // Password hash check (null means external IDP user)
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            _logger.LogWarning("Login failed: User has no password (external IDP user?) - {Email}", email);
            return new LoginResult(false, Error: "Invalid email or password.");
        }

        // BCrypt verification
        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            await _revocationService.IncrementFailedAuthAttemptsAsync($"login:{email}", ct);
            _logger.LogWarning("Login failed: Invalid password - {Email}", email);
            return new LoginResult(false, Error: "Invalid email or password.");
        }

        // Reset failed attempts on successful password
        await _revocationService.ResetFailedAuthAttemptsAsync($"login:{email}", ct);

        // 2FA check
        var totpStatus = await _totpService.GetStatusAsync(user.Id, ct);
        var hasTotpEnabled = totpStatus.IsEnabled;
        var passkeys = await _passkeyService.GetCredentialsByOwnerAsync(
            "OrgUser", user.Id, ct);
        var hasPasskeys = passkeys.Any();

        if (hasTotpEnabled || hasPasskeys)
        {
            var methods = new List<string>();
            if (hasTotpEnabled) methods.Add("totp");
            if (hasPasskeys) methods.Add("passkey");

            var loginToken = await _totpService.GenerateLoginTokenAsync(user.Id, ct);

            return new LoginResult(true, TwoFactorRequired: true,
                LoginToken: loginToken, AvailableMethods: methods);
        }

        // No 2FA — issue tokens directly
        var tokens = await _tokenService.GenerateUserTokenAsync(user, user.Organization!, ct);

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return new LoginResult(true, Tokens: tokens);
    }
}
```

- [ ] **Step 3: Create ILoginService test**

Create test file in `tests/Sorcha.Tenant.Service.Tests/Services/LoginServiceTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Moq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class LoginServiceTests
{
    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsTokens()
    {
        // Arrange — setup with mocked DB, token service, etc.
        // Act — call LoginAsync with valid email/password
        // Assert — Success is true, Tokens is not null
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsError()
    {
        // Assert — Success is false, Error contains "Invalid email or password"
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ReturnsError()
    {
        // Assert — Success is false, Error contains "Invalid email or password"
    }

    [Fact]
    public async Task LoginAsync_TwoFactorEnabled_ReturnsTwoFactorChallenge()
    {
        // Assert — TwoFactorRequired is true, LoginToken is not null
    }

    [Fact]
    public async Task LoginAsync_RateLimited_ReturnsError()
    {
        // Assert — Success is false, Error contains "Too many"
    }

    [Fact]
    public async Task LoginAsync_ExternalIdpUser_ReturnsError()
    {
        // Assert — user with null PasswordHash returns error
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "FullyQualifiedName~LoginServiceTests" -v minimal`
Expected: Tests compile and fail (arrange/act/assert stubs need completing)

- [ ] **Step 5: Complete test implementations and verify pass**

Fill in mocked DB context, token service, etc. Run tests again — all should pass.

- [ ] **Step 6: Register ILoginService in DI**

In `src/Services/Sorcha.Tenant.Service/Program.cs`, add after existing service registrations:

```csharp
builder.Services.AddScoped<ILoginService, LoginService>();
```

- [ ] **Step 7: Refactor AuthEndpoints to use ILoginService**

In `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`, replace the inline login logic (lines 165-284) with a call to `ILoginService.LoginAsync()`. The endpoint handler becomes a thin adapter that maps `LoginResult` to HTTP responses.

- [ ] **Step 8: Run existing auth tests to verify no regression**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests/ -v minimal`
Expected: All existing tests pass

- [ ] **Step 9: Commit**

```bash
git add src/Services/Sorcha.Tenant.Service/Services/ILoginService.cs \
        src/Services/Sorcha.Tenant.Service/Services/LoginService.cs \
        src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs \
        src/Services/Sorcha.Tenant.Service/Program.cs \
        tests/Sorcha.Tenant.Service.Tests/Services/LoginServiceTests.cs
git commit -m "refactor: extract ILoginService from AuthEndpoints inline logic"
```

---

### Task 2: Extract IRegistrationService from AuthEndpoints

Same pattern as Task 1, extracting the registration logic.

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Services/IRegistrationService.cs`
- Create: `src/Services/Sorcha.Tenant.Service/Services/RegistrationService.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Program.cs`
- Test: `tests/Sorcha.Tenant.Service.Tests/Services/RegistrationServiceTests.cs`

- [ ] **Step 1: Create IRegistrationService interface**

Create `src/Services/Sorcha.Tenant.Service/Services/IRegistrationService.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Result of a self-registration attempt.
/// </summary>
public record RegistrationResult(
    bool Success,
    TokenResponse? Tokens = null,
    Guid? UserId = null,
    Dictionary<string, string[]>? ValidationErrors = null,
    string? Error = null);

/// <summary>
/// Handles user self-registration with email/password for organizations
/// that have self-registration enabled. Validates password policy,
/// checks email uniqueness and domain restrictions, creates the user,
/// and sends verification email.
/// </summary>
public interface IRegistrationService
{
    /// <summary>
    /// Registers a new user in the specified organization.
    /// </summary>
    Task<RegistrationResult> RegisterAsync(
        string orgSubdomain, string email, string password,
        string displayName, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create RegistrationService implementation**

Create `src/Services/Sorcha.Tenant.Service/Services/RegistrationService.cs`. Extract logic from `AuthEndpoints.cs` lines 486-601: org resolution, self-registration check, password policy validation, email uniqueness, domain restrictions, user creation with BCrypt hash, audit event, email verification send, token issuance.

- [ ] **Step 3: Write and run tests**

Create `tests/Sorcha.Tenant.Service.Tests/Services/RegistrationServiceTests.cs` with tests for: valid registration, weak password, duplicate email, self-registration disabled, domain restriction violation.

- [ ] **Step 4: Register in DI and refactor AuthEndpoints**

Add `builder.Services.AddScoped<IRegistrationService, RegistrationService>()` in Program.cs. Replace inline register logic in AuthEndpoints with call to `IRegistrationService.RegisterAsync()`.

- [ ] **Step 5: Run all tests, commit**

```bash
git commit -m "refactor: extract IRegistrationService from AuthEndpoints inline logic"
```

---

### Task 3: Create IPasswordResetService (New)

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Services/IPasswordResetService.cs`
- Create: `src/Services/Sorcha.Tenant.Service/Services/PasswordResetService.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Program.cs`
- Test: `tests/Sorcha.Tenant.Service.Tests/Services/PasswordResetServiceTests.cs`

- [ ] **Step 1: Create IPasswordResetService interface**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Manages password reset token generation, validation, and password updates.
/// Tokens are 32-byte URL-safe base64 strings with a 1-hour TTL.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Generates a reset token, stores the hash on the user, and sends
    /// a reset email. Returns success even if the email is not found
    /// (to prevent user enumeration).
    /// </summary>
    Task<bool> RequestResetAsync(string email, string resetBaseUrl, CancellationToken ct = default);

    /// <summary>
    /// Validates that a reset token is valid and not expired.
    /// </summary>
    Task<PasswordResetValidation> ValidateTokenAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Resets the user's password after validating the token and the new
    /// password against NIST policy + HIBP breach list.
    /// </summary>
    Task<PasswordResetResult> ResetPasswordAsync(
        string token, string newPassword, CancellationToken ct = default);
}

public record PasswordResetValidation(bool IsValid, string? Email = null, string? Error = null);

public record PasswordResetResult(
    bool Success, string? Error = null,
    Dictionary<string, string[]>? ValidationErrors = null);
```

- [ ] **Step 2: Create PasswordResetService implementation**

Injects `TenantDbContext`, `IPasswordPolicyService`, `IEmailSender`, `ILogger`. Token generation uses `RandomNumberGenerator.GetBytes(32)` → `Convert.ToBase64String` with URL-safe encoding. Token hash stored on `UserIdentity.PasswordResetTokenHash` with `PasswordResetTokenExpiresAt` (1 hour). The password reset clears the token after use.

Note: This requires two new nullable columns on `UserIdentity`: `PasswordResetTokenHash` (string, 500) and `PasswordResetTokenExpiresAt` (DateTime). Add an EF migration.

- [ ] **Step 3: Add EF migration for password reset columns**

Run: `dotnet ef migrations add AddPasswordResetToken --project src/Services/Sorcha.Tenant.Service`

- [ ] **Step 4: Write and run tests**

Tests: request for existing user sends email, request for nonexistent user succeeds silently, expired token fails, valid token + valid password succeeds, valid token + weak password fails, token consumed after use.

- [ ] **Step 5: Register in DI, run all tests, commit**

```bash
git commit -m "feat: add IPasswordResetService for password reset flow"
```

---

### Task 4: Tenant Service Razor Pages Infrastructure

Set up the Razor Pages foundation in the Tenant Service.

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Program.cs`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/_ViewImports.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/_ViewStart.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Shared/_AuthLayout.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/wwwroot/css/auth.css`
- Create: `src/Services/Sorcha.Tenant.Service/wwwroot/css/bootstrap.min.css`

- [ ] **Step 1: Add Razor Pages and static files to Program.cs**

In `src/Services/Sorcha.Tenant.Service/Program.cs`, add services and middleware:

After line ~50 (service registration section):
```csharp
builder.Services.AddRazorPages();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "Sorcha.Auth.AF";
    options.Cookie.Path = "/auth";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
```

In middleware pipeline, before `app.MapAuthEndpoints()` (around line ~137):
```csharp
app.UseStaticFiles();
app.MapRazorPages();
```

- [ ] **Step 2: Create _ViewImports.cshtml**

Create `src/Services/Sorcha.Tenant.Service/Pages/_ViewImports.cshtml`:

```html
@using Sorcha.Tenant.Service
@using Sorcha.Tenant.Service.Services
@namespace Sorcha.Tenant.Service.Pages
@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers
```

- [ ] **Step 3: Create _ViewStart.cshtml**

Create `src/Services/Sorcha.Tenant.Service/Pages/_ViewStart.cshtml`:

```html
@{
    Layout = "_AuthLayout";
}
```

- [ ] **Step 4: Create _AuthLayout.cshtml**

Create `src/Services/Sorcha.Tenant.Service/Pages/Shared/_AuthLayout.cshtml`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>@ViewData["Title"] - Sorcha</title>
    <link rel="stylesheet" href="/auth-static/css/bootstrap.min.css" />
    <link rel="stylesheet" href="/auth-static/css/auth.css" />
</head>
<body class="auth-body">
    <div class="auth-container">
        @RenderBody()
    </div>
    <footer class="auth-footer">
        <a href="/app/">Back to Sorcha</a>
    </footer>
    @await RenderSectionAsync("Scripts", required: false)
</body>
</html>
```

- [ ] **Step 5: Create auth.css**

Create `src/Services/Sorcha.Tenant.Service/wwwroot/css/auth.css`. Clone styles from the existing Blazor AuthLayout and Login/Signup pages:

```css
/* Auth page styles — cloned from Sorcha.UI Blazor components */

:root {
    --primary-color: #667eea;
    --secondary-color: #764ba2;
    --btn-primary: #1b6ec2;
    --btn-primary-hover: #1861ac;
    --bg-color: #f8f9fa;
    --border-color: #dee2e6;
    --text-muted: #6c757d;
}

.auth-body {
    margin: 0;
    min-height: 100vh;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    background: linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%);
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

.auth-container {
    width: 100%;
    max-width: 480px;
    padding: 1rem;
}

.auth-card {
    background: #fff;
    border-radius: 12px;
    box-shadow: 0 10px 40px rgba(0, 0, 0, 0.2);
    overflow: hidden;
}

.auth-header {
    background: linear-gradient(135deg, var(--primary-color) 0%, var(--secondary-color) 100%);
    color: white;
    padding: 2rem;
    text-align: center;
}

.auth-header h2 {
    margin: 0;
    font-size: 2rem;
    font-weight: 600;
}

.auth-header p {
    margin: 0.5rem 0 0;
    opacity: 0.9;
    font-size: 1rem;
}

.auth-card-body {
    padding: 2rem;
}

.auth-footer {
    margin-top: 2rem;
    text-align: center;
    color: var(--text-muted);
    font-size: 0.875rem;
}

.auth-footer a {
    color: var(--text-muted);
    text-decoration: none;
}

.auth-footer a:hover {
    color: var(--btn-primary);
}

/* Divider */
.divider-row {
    display: flex;
    align-items: center;
    margin: 1.5rem 0;
}

.divider-line {
    flex: 1;
    border: none;
    border-top: 1px solid var(--border-color);
}

.divider-text {
    padding: 0 1rem;
    color: var(--text-muted);
    font-size: 0.875rem;
}

/* Social buttons */
.social-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0.5rem;
    width: 100%;
    padding: 0.625rem 1rem;
    border: 1px solid var(--border-color);
    border-radius: 6px;
    background: #fff;
    color: #333;
    font-size: 0.9rem;
    cursor: pointer;
    transition: background-color 0.15s, border-color 0.15s;
}

.social-btn:hover {
    background: var(--bg-color);
    border-color: #adb5bd;
}

/* Passkey button */
.passkey-btn {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 0.5rem;
    width: 100%;
    padding: 0.75rem 1rem;
    border: 2px solid var(--primary-color);
    border-radius: 6px;
    background: #fff;
    color: var(--primary-color);
    font-weight: 600;
    font-size: 1rem;
    cursor: pointer;
    transition: background-color 0.15s, color 0.15s;
}

.passkey-btn:hover {
    background: var(--primary-color);
    color: #fff;
}

/* Processing spinner */
.auth-processing {
    text-align: center;
    padding: 3rem 2rem;
}

.auth-processing .spinner-border {
    width: 3rem;
    height: 3rem;
    color: var(--primary-color);
}

/* Error/success states */
.auth-result {
    text-align: center;
    padding: 2rem;
}

.auth-result .icon {
    font-size: 3rem;
    margin-bottom: 1rem;
}

.auth-result.success .icon { color: #26b050; }
.auth-result.error .icon { color: #dc3545; }

/* Method tabs */
.method-tabs {
    display: flex;
    border-bottom: 2px solid var(--border-color);
    margin-bottom: 1.5rem;
}

.method-tab {
    flex: 1;
    padding: 0.75rem;
    border: none;
    background: none;
    color: var(--text-muted);
    font-weight: 500;
    cursor: pointer;
    border-bottom: 2px solid transparent;
    margin-bottom: -2px;
    transition: color 0.15s, border-color 0.15s;
}

.method-tab.active {
    color: var(--primary-color);
    border-bottom-color: var(--primary-color);
}
```

- [ ] **Step 6: Copy bootstrap.min.css**

Copy from `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css` to `src/Services/Sorcha.Tenant.Service/wwwroot/css/bootstrap.min.css`.

- [ ] **Step 7: Verify build**

Run: `dotnet build src/Services/Sorcha.Tenant.Service/`
Expected: Build succeeds

- [ ] **Step 8: Commit**

```bash
git commit -m "feat: add Razor Pages infrastructure to Tenant Service"
```

---

## Chunk 2: Auth Pages (Login, Signup, Logout)

### Task 5: Login Razor Page

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/Login.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/Login.cshtml.cs`

- [ ] **Step 1: Create Login.cshtml.cs page model**

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Sorcha.Tenant.Service.Pages.Auth;

public class LoginModel : PageModel
{
    private readonly ILoginService _loginService;
    private readonly ITotpService _totpService;
    private readonly ITokenService _tokenService;
    private readonly TenantDbContext _db;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        ILoginService loginService,
        ITotpService totpService,
        ITokenService tokenService,
        TenantDbContext db,
        ILogger<LoginModel> logger)
    {
        _loginService = loginService;
        _totpService = totpService;
        _tokenService = tokenService;
        _db = db;
        _logger = logger;
    }

    [BindProperty]
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required]
    public string Password { get; set; } = "";

    [BindProperty]
    public string? TotpCode { get; set; }

    [BindProperty]
    public string? LoginToken { get; set; }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public bool ShowTwoFactor { get; set; }
    public List<string>? AvailableMethods { get; set; }
    public string? ErrorMessage { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        // 2FA verification flow
        if (!string.IsNullOrEmpty(LoginToken) && !string.IsNullOrEmpty(TotpCode))
        {
            return await Handle2FaAsync(ct);
        }

        // Primary login flow
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await _loginService.LoginAsync(Email, Password, ct);

        if (!result.Success && !result.TwoFactorRequired)
        {
            ErrorMessage = result.Error ?? "Login failed.";
            return Page();
        }

        if (result.TwoFactorRequired)
        {
            ShowTwoFactor = true;
            LoginToken = result.LoginToken;
            AvailableMethods = result.AvailableMethods;
            return Page();
        }

        return RedirectToApp(result.Tokens!);
    }

    private async Task<IActionResult> Handle2FaAsync(CancellationToken ct)
    {
        // Step 1: Validate the login token to get the userId
        var userId = await _totpService.ValidateLoginTokenAsync(LoginToken!, ct);
        if (userId is null)
        {
            ErrorMessage = "Login session expired. Please sign in again.";
            return Page();
        }

        // Step 2: Validate the TOTP code
        var isValid = await _totpService.ValidateCodeAsync(userId.Value, TotpCode!, ct);
        if (!isValid)
        {
            ShowTwoFactor = true;
            LoginToken = LoginToken; // preserve for retry
            ErrorMessage = "Invalid verification code.";
            return Page();
        }

        // Step 3: Look up user and issue tokens
        var user = await _db.UserIdentities
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.Id == userId.Value, ct);

        if (user is null)
        {
            ErrorMessage = "User not found.";
            return Page();
        }

        var tokens = await _tokenService.GenerateUserTokenAsync(user, user.Organization!, ct);
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return RedirectToApp(tokens);
    }

    private IActionResult RedirectToApp(TokenResponse tokens)
    {
        var returnUrl = IsValidReturnUrl(ReturnUrl) ? ReturnUrl : "";
        var fragment = $"token={Uri.EscapeDataString(tokens.AccessToken)}" +
                       $"&refresh={Uri.EscapeDataString(tokens.RefreshToken)}";
        if (!string.IsNullOrEmpty(returnUrl))
        {
            fragment += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        }
        return Redirect($"/app/#{fragment}");
    }

    private static bool IsValidReturnUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        // Only allow relative paths — prevent open redirect
        return url.StartsWith('/') && !url.StartsWith("//");
    }
}
```

- [ ] **Step 2: Create Login.cshtml**

```html
@page "/auth/login"
@model Sorcha.Tenant.Service.Pages.Auth.LoginModel
@{
    ViewData["Title"] = "Sign In";
}

<div class="auth-card">
    <div class="auth-header">
        <h2>Sign In</h2>
        <p>Welcome back to Sorcha</p>
    </div>
    <div class="auth-card-body">
        @if (!string.IsNullOrEmpty(Model.ErrorMessage))
        {
            <div class="alert alert-danger alert-dismissible fade show" role="alert">
                @Model.ErrorMessage
            </div>
        }

        @if (Model.ShowTwoFactor)
        {
            <form method="post">
                @Html.AntiForgeryToken()
                <input type="hidden" asp-for="LoginToken" />
                <input type="hidden" asp-for="ReturnUrl" />
                <div class="mb-3">
                    <label class="form-label">Verification Code</label>
                    <input type="text" asp-for="TotpCode" class="form-control form-control-lg"
                           placeholder="Enter 6-digit code" autofocus autocomplete="one-time-code"
                           inputmode="numeric" maxlength="6" />
                </div>
                <button type="submit" class="btn btn-primary btn-lg w-100">Verify</button>
            </form>
        }
        else
        {
            <form method="post">
                @Html.AntiForgeryToken()
                <input type="hidden" asp-for="ReturnUrl" />
                <div class="mb-3">
                    <label class="form-label">Email</label>
                    <input type="email" asp-for="Email" class="form-control form-control-lg"
                           placeholder="you@example.com" autofocus />
                    <span asp-validation-for="Email" class="text-danger small"></span>
                </div>
                <div class="mb-4">
                    <label class="form-label">Password</label>
                    <input type="password" asp-for="Password" class="form-control form-control-lg"
                           placeholder="Enter password" />
                    <span asp-validation-for="Password" class="text-danger small"></span>
                </div>
                <button type="submit" class="btn btn-primary btn-lg w-100">Sign In</button>
            </form>

            <div class="divider-row">
                <hr class="divider-line" />
                <span class="divider-text">or</span>
                <hr class="divider-line" />
            </div>

            <button type="button" class="passkey-btn" id="passkey-signin-btn">
                <span>&#128274;</span> Sign in with Passkey
            </button>
        }

        <div class="text-center mt-4">
            <p class="text-muted mb-1">Don't have an account?
                <a href="/auth/signup@(Model.ReturnUrl != null ? $"?returnUrl={Uri.EscapeDataString(Model.ReturnUrl)}" : "")">Sign up</a>
            </p>
            <p class="text-muted">
                <a href="/auth/reset-password">Forgot password?</a>
            </p>
        </div>
    </div>
</div>

@section Scripts {
    <script src="/auth-static/js/webauthn.js"></script>
    <script>
        document.getElementById('passkey-signin-btn')?.addEventListener('click', function() {
            sorcha.webauthn.signIn('/api/auth/passkey/assertion/options',
                                   '/api/auth/passkey/assertion/verify',
                                   '@(Model.ReturnUrl ?? "")');
        });
    </script>
}
```

- [ ] **Step 3: Verify build and test page renders**

Run: `dotnet build src/Services/Sorcha.Tenant.Service/`

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: add server-rendered Login Razor Page"
```

---

### Task 6: Signup Razor Page

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/Signup.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/Signup.cshtml.cs`

- [ ] **Step 1: Create Signup.cshtml.cs page model**

Page model with three signup methods:
- `OnPostEmailAsync` — email/password registration via `IRegistrationService`
- Passkey registration handled via JS → API endpoints
- Social login handled via JS → `POST /api/auth/public/social/initiate`

Properties: Email, Password, DisplayName, OrgSubdomain (optional), ReturnUrl, ActiveTab, ErrorMessage, ValidationErrors.

- [ ] **Step 2: Create Signup.cshtml**

Three-tab form: Passkey / Social / Email & Password.
- Passkey tab: display name + email fields, JS WebAuthn flow
- Social tab: Google/Microsoft/GitHub/Apple buttons
- Email tab: standard registration form with POST

- [ ] **Step 3: Build and verify**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: add server-rendered Signup Razor Page"
```

---

### Task 7: Logout Razor Page

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/Logout.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/Logout.cshtml.cs`

- [ ] **Step 1: Create Logout page model and view**

`OnGet` — renders "Sign out?" confirmation.
`OnPost` — receives refresh token from hidden field (populated by JS from fragment), revokes via `ITokenRevocationService`, renders "Signed out" message.

- [ ] **Step 2: Commit**

```bash
git commit -m "feat: add server-rendered Logout Razor Page"
```

---

## Chunk 3: Callback & Utility Pages

### Task 8: Social Callback Page

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs`

- [ ] **Step 1: Create SocialCallback page model**

`OnGetAsync` receives `?code=...&state=...&provider=...`. The `provider` parameter identifies which OAuth provider (google, microsoft, github, apple) — this is encoded in the state parameter by `ISocialLoginService.GenerateAuthorizationUrlAsync()` and must be extracted/passed through. Calls `ISocialLoginService.ExchangeCodeAsync(provider, code, state, ct)`. On success, creates/finds public user via `IPublicUserService`, issues tokens via `ITokenService.GeneratePublicUserTokenAsync()`, redirects to `/app/#token=...`. On failure, shows error with link to signup.

- [ ] **Step 2: Create SocialCallback view**

Minimal page — processing spinner while server handles the exchange. Error state shown if exchange fails.

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: add server-rendered Social Callback page"
```

---

### Task 9: OIDC Callback Page

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/OidcCallback.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/OidcCallback.cshtml.cs`

- [ ] **Step 1: Create OidcCallback page model**

`OnGetAsync` receives `?code=...&state=...`. Calls `IOidcExchangeService.ExchangeCodeAsync()`. Handles profile completion if needed (renders form). Issues tokens and redirects to `/app/#token=...`.

- [ ] **Step 2: Create OidcCallback view**

Processing spinner by default. If `requiresProfileCompletion`, renders inline display name + email form that POSTs back.

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: add server-rendered OIDC Callback page"
```

---

### Task 10: Verify Email Page

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/VerifyEmail.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/VerifyEmail.cshtml.cs`

- [ ] **Step 1: Create VerifyEmail page model**

`OnGetAsync` receives `?token=...`. Calls `IEmailVerificationService.VerifyTokenAsync()`. Sets `IsVerified` or `ErrorMessage` for the view.

- [ ] **Step 2: Create VerifyEmail view**

Success: green checkmark, "Email verified" message, link to `/auth/login`.
Error: red X, error message, link to `/auth/login`.

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: add server-rendered Email Verification page"
```

---

### Task 11: Reset Password Page

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/ResetPassword.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/ResetPassword.cshtml.cs`

- [ ] **Step 1: Create ResetPassword page model**

Two modes based on query params:
- No token: `OnGet` shows email input form. `OnPostRequestAsync` calls `IPasswordResetService.RequestResetAsync()`, shows "Check your email" message.
- With token: `OnGet` validates token, shows new password form. `OnPostResetAsync` calls `IPasswordResetService.ResetPasswordAsync()`, shows success + link to login.

- [ ] **Step 2: Create ResetPassword view**

Conditional rendering based on mode (request vs reset) and result state.

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: add server-rendered Password Reset page"
```

---

### Task 12: Error Page

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/Error.cshtml`
- Create: `src/Services/Sorcha.Tenant.Service/Pages/Auth/Error.cshtml.cs`

- [ ] **Step 1: Create Error page**

Simple page showing a user-friendly error message. `OnGet` reads `?message=...` query param (sanitized). Default message: "Something went wrong. Please try again."

- [ ] **Step 2: Commit**

```bash
git commit -m "feat: add server-rendered auth Error page"
```

---

### Task 13: WebAuthn JavaScript

**Files:**
- Create: `src/Services/Sorcha.Tenant.Service/wwwroot/js/webauthn.js`

- [ ] **Step 1: Create webauthn.js**

Vanilla JS module providing:
- `sorcha.webauthn.signIn(optionsUrl, verifyUrl, returnUrl)` — passkey assertion flow
- `sorcha.webauthn.register(optionsUrl, verifyUrl, displayName, email, returnUrl)` — passkey registration flow

Both functions: fetch options from API → call `navigator.credentials.get/create()` → post result to verify endpoint → on success, redirect to `/app/#token=...&refresh=...`.

```javascript
var sorcha = sorcha || {};
sorcha.webauthn = {
    signIn: async function(optionsUrl, verifyUrl, returnUrl) {
        // 1. Fetch assertion options
        // 2. navigator.credentials.get()
        // 3. POST assertion to verifyUrl
        // 4. Redirect to /app/#token=...
    },
    register: async function(optionsUrl, verifyUrl, displayName, email, returnUrl) {
        // 1. POST registration options with displayName + email
        // 2. navigator.credentials.create()
        // 3. POST attestation to verifyUrl
        // 4. Redirect to /app/#token=...
    }
};
```

- [ ] **Step 2: Commit**

```bash
git commit -m "feat: add WebAuthn vanilla JS for server-rendered auth pages"
```

---

## Chunk 4: YARP Routes & Blazor Cleanup

### Task 14: Add YARP Routes for Auth Pages

**Files:**
- Modify: `src/Services/Sorcha.ApiGateway/appsettings.json`

- [ ] **Step 1: Add auth page routes**

Add two new routes in the YARP Routes section, before the admin-ui catch-all routes (before line ~61). These must have lower order values to take precedence:

```json
"auth-pages-route": {
    "ClusterId": "tenant-cluster",
    "Order": 5,
    "Match": {
        "Path": "/auth/{**catch-all}"
    },
    "Transforms": [
        { "PathPattern": "/auth/{**catch-all}" }
    ]
},
"auth-static-route": {
    "ClusterId": "tenant-cluster",
    "Order": 4,
    "Match": {
        "Path": "/auth-static/{**catch-all}"
    },
    "Transforms": [
        { "PathRemovePrefix": "/auth-static" }
    ]
}
```

No `AuthorizationPolicy` — these are public pages.
No `RateLimiterPolicy` — rate limiting is handled per-page in the Tenant Service.

- [ ] **Step 2: Verify YARP config is valid JSON**

Run: `dotnet build src/Services/Sorcha.ApiGateway/`

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: add YARP routes for server-rendered auth pages"
```

---

### Task 15: Fragment Token Handoff in Blazor

**Files:**
- Create: `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/js/fragment-handoff.js`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html` (add script reference)

- [ ] **Step 1: Create fragment-handoff.js**

```javascript
window.sorcha = window.sorcha || {};
window.sorcha.fragmentHandoff = {
    getAndClear: function() {
        var hash = window.location.hash;
        if (!hash || hash.length < 2) return null;
        // Clear fragment immediately
        history.replaceState(null, '', window.location.pathname + window.location.search);
        var params = new URLSearchParams(hash.substring(1));
        var token = params.get('token');
        var refresh = params.get('refresh');
        var returnUrl = params.get('returnUrl');
        if (!token) return null;
        return { token: token, refresh: refresh, returnUrl: returnUrl };
    }
};
```

- [ ] **Step 2: Add script to index.html**

Add `<script src="js/fragment-handoff.js"></script>` in the `<head>` of `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html`.

- [ ] **Step 3: Add token pickup to Program.cs**

In `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs`, after building the host, add startup logic that checks for fragment tokens. This runs once on WASM boot:

The approach: register an `IHostedService` or use a component that runs on first render. The simplest approach is to have `Routes.razor` or `App.razor` check for fragment tokens in `OnAfterRenderAsync`.

Alternative: Add a `FragmentTokenHandler` component that runs in `Routes.razor`:

```csharp
// In Routes.razor, inject IJSRuntime and ITokenCache
// In OnAfterRenderAsync(firstRender), call JS to get/clear fragment,
// store tokens, and notify auth state changed.
```

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: add fragment token handoff for server-rendered auth flow"
```

---

### Task 16: Update Blazor Redirect & Remove Old Auth Pages

**Files:**
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Routes.razor`
- Delete: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Login.razor`
- Delete: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/PublicSignup.razor`
- Delete: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Logout.razor`
- Delete: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/AuthLayout.razor`
- Delete: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/RedirectToLogin.razor`
- Modify: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Authentication/AuthenticationService.cs`

- [ ] **Step 1: Update Routes.razor**

Replace `<RedirectToLogin />` in the `<NotAuthorized>` section with inline redirect to `/auth/login`:

```razor
<NotAuthorized>
    @{
        var returnUrl = Uri.EscapeDataString(NavigationManager.Uri);
        NavigationManager.NavigateTo($"/auth/login?returnUrl={returnUrl}", forceLoad: true);
    }
</NotAuthorized>
```

Add the fragment token handler logic (from Task 15) to this component or a child component.

- [ ] **Step 2: Delete old Blazor auth pages**

Remove:
- `Pages/Login.razor`
- `Pages/PublicSignup.razor`
- `Pages/Logout.razor`
- `Components/Layout/AuthLayout.razor`
- `Components/RedirectToLogin.razor`

- [ ] **Step 3: Clean up AuthenticationService.cs**

Remove methods that are no longer called from WASM:
- `LoginAsync` (OAuth2 password grant)
- `LoginWithTwoFactorAsync`
- `StoreExternalTokenAsync` (keep if needed for fragment handoff, otherwise remove)

Keep:
- `RefreshTokenAsync` (still needed for background refresh)
- `LogoutAsync` (update to navigate to `/auth/logout` with `forceLoad: true`)
- `GetAccessTokenAsync`
- `GetRefreshTokenAsync`
- `IsAuthenticatedAsync`
- `GetAuthenticationInfoAsync`

Update `LogoutAsync` to navigate to the server-rendered logout page:
```csharp
public async Task LogoutAsync()
{
    var refreshToken = await _tokenCache.GetRefreshTokenAsync();
    await _tokenCache.ClearAsync();
    _authStateProvider.NotifyAuthenticationStateChanged();
    // Navigate to server-rendered logout page with token in fragment
    _navigationManager.NavigateTo(
        $"/auth/logout#{(refreshToken != null ? $"refresh={Uri.EscapeDataString(refreshToken)}" : "")}",
        forceLoad: true);
}
```

- [ ] **Step 4: Verify build**

Run: `dotnet build`
Expected: Solution builds with no errors (there may be warnings from removed references)

- [ ] **Step 5: Fix any broken references**

Check for any remaining references to `Login.razor`, `PublicSignup.razor`, `AuthLayout.razor`, `RedirectToLogin.razor` in other files. Remove or update as needed.

- [ ] **Step 6: Commit**

```bash
git commit -m "feat: remove Blazor auth pages, redirect to server-rendered pages"
```

---

## Chunk 5: Testing & Documentation

### Task 17: Unit Tests for Razor Page Models

**Files:**
- Create/Modify: `tests/Sorcha.Tenant.Service.Tests/Pages/LoginModelTests.cs`
- Create: `tests/Sorcha.Tenant.Service.Tests/Pages/SignupModelTests.cs`
- Create: `tests/Sorcha.Tenant.Service.Tests/Pages/SocialCallbackModelTests.cs`
- Create: `tests/Sorcha.Tenant.Service.Tests/Pages/OidcCallbackModelTests.cs`
- Create: `tests/Sorcha.Tenant.Service.Tests/Pages/VerifyEmailModelTests.cs`
- Create: `tests/Sorcha.Tenant.Service.Tests/Pages/ResetPasswordModelTests.cs`
- Create: `tests/Sorcha.Tenant.Service.Tests/Pages/LogoutModelTests.cs`

- [ ] **Step 1: Write LoginModel tests**

Test cases:
- `OnPostAsync_ValidCredentials_RedirectsToAppWithTokenFragment`
- `OnPostAsync_InvalidCredentials_ShowsError`
- `OnPostAsync_TwoFactorRequired_ShowsTwoFactorForm`
- `OnPostAsync_TwoFactorCode_ValidCode_RedirectsToApp`
- `OnPostAsync_TwoFactorCode_InvalidCode_ShowsError`
- `OnPostAsync_WithReturnUrl_IncludesReturnUrlInFragment`
- `OnPostAsync_WithMaliciousReturnUrl_IgnoresReturnUrl`

- [ ] **Step 2: Write remaining page model tests**

Similar patterns for Signup, SocialCallback, OidcCallback (including profile completion flow), VerifyEmail, ResetPassword, Logout.

- [ ] **Step 3: Run all tests**

Run: `dotnet test -v minimal`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git commit -m "test: add unit tests for server-rendered auth page models"
```

---

### Task 18: Integration Tests

**Files:**
- Create: `tests/Sorcha.Tenant.Service.Tests/Pages/AuthPagesIntegrationTests.cs`

- [ ] **Step 1: Write integration tests using WebApplicationFactory**

Test cases:
- `GET /auth/login` returns 200 with HTML containing login form
- `GET /auth/signup` returns 200 with HTML containing signup form
- `POST /auth/login` with valid credentials returns 302 redirect to `/app/#token=...`
- `POST /auth/login` with invalid credentials returns 200 with error message
- `GET /auth/verify-email?token=valid` returns 200 with success message
- `GET /auth/verify-email?token=invalid` returns 200 with error message
- `GET /auth/social/callback` without params returns error page
- `GET /auth/reset-password` returns 200 with email form
- Static file `/auth-static/css/auth.css` returns 200

- [ ] **Step 2: Run integration tests**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests/ --filter "FullyQualifiedName~AuthPagesIntegration" -v minimal`

- [ ] **Step 3: Commit**

```bash
git commit -m "test: add integration tests for server-rendered auth pages"
```

---

### Task 19: Documentation Updates

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/README.md`
- Modify: `docs/reference/API-DOCUMENTATION.md` (if exists)
- Modify: `.specify/MASTER-TASKS.md`

- [ ] **Step 1: Update Tenant Service README**

Add section documenting the server-rendered auth pages, their routes, and the token handoff mechanism.

- [ ] **Step 2: Update MASTER-TASKS.md**

Mark relevant tasks as complete. Add any new tasks discovered during implementation.

- [ ] **Step 3: Commit**

```bash
git commit -m "docs: update documentation for server-rendered auth pages"
```

---

### Task 20: Full Build & Smoke Test

- [ ] **Step 1: Build entire solution**

Run: `dotnet build`
Expected: 0 errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test`
Expected: All tests pass (existing + new)

- [ ] **Step 3: Docker build test**

Run: `docker-compose build tenant-service api-gateway`
Expected: Both images build successfully

- [ ] **Step 4: Final commit if any fixes needed**

```bash
git commit -m "chore: fix any remaining build/test issues"
```
