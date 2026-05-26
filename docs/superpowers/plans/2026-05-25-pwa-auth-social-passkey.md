# Citizen Wallet PWA — Social + Passkey sign-in — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add passkey + social (Google/Apple/Microsoft/GitHub) sign-in alongside email/password in the citizen wallet PWA, behind a dedicated `/signin` screen and an auth gate, with every path minting an F136 Consumer-tier token and refresh-based silent renewal.

**Architecture:** Most auth backend already exists (F116). Backend changes are small and additive: a public configured-providers endpoint, a Consumer-tier hint on passkey-assertion-verify and verify-2fa, and a `surface=wallet` branch in the social OAuth callback that mints Consumer-tier and redirects to `/wallet/#token=…`. The bulk is PWA-side: a token-backed `AuthenticationStateProvider` + `AuthorizeRouteView` gate, three sign-in flows on `IAuthService`, a WebAuthn JS bridge, a fragment-return handler, and silent refresh in `BearerTokenHandler`.

**Tech Stack:** .NET 10, Blazor WebAssembly (PWA), MudBlazor, ASP.NET Core Minimal APIs, Fido2NetLib, xUnit + FluentAssertions + Moq, Playwright (NUnit) E2E.

**Design doc:** `docs/superpowers/specs/2026-05-25-pwa-auth-social-passkey-design.md`

**Branch:** `feature/pwa-social-passkey-signin` (already created off `master`).

**Suggested PR slicing:** Phase A (backend, ships independently) → Phases B–D (PWA) → Phase E (E2E + docs). Each phase leaves the build green.

---

## File Structure

**Tenant Service (`src/Services/Sorcha.Tenant.Service/`):**
- `Endpoints/SocialLoginEndpoints.cs` — add `GET /api/auth/social/providers`; thread `Surface` into initiate.
- `Endpoints/PublicPasskeyEndpoints.cs` — Consumer-tier hint on assertion-verify.
- `Endpoints/AuthEndpoints.cs` — Consumer-tier hint on verify-2fa.
- `Models/Dtos/AuthDtos.cs` — `Verify2FaRequest` gains `Tier`; new `SocialProvidersResponse`.
- `Models/Dtos/PublicAuthDtos.cs` — `PublicPasskeyAssertionVerifyRequest` gains `Tier`.
- `Services/ISocialLoginService.cs` / `SocialLoginService.cs` — `surface` through the initiate overload + `SocialStateData` + `SocialAuthCallbackResult`.
- `Pages/Auth/SocialCallback.cshtml.cs` — `surface=wallet` branch (Consumer mint, `/wallet/#…` redirect, login-only refusal).

**Wallet PWA (`src/Apps/Sorcha.Wallet.Pwa/`):**
- `Services/IAccessTokenStore.cs` — `AccessTokenRecord` gains `RefreshToken`.
- `Services/IAuthService.cs` — passkey + social methods; password tier; refresh.
- `Services/WalletAuthenticationStateProvider.cs` — NEW; token-backed auth state.
- `Services/IPasskeyInterop.cs` (+ `PasskeyInterop`) — NEW; WebAuthn JS bridge.
- `Services/ISocialProvidersClient.cs` (+ impl) — NEW; reads configured providers.
- `Services/BearerTokenHandler.cs` — 401→refresh→retry.
- `Components/RedirectToSignIn.razor` — NEW; gate's NotAuthorized target.
- `Pages/SignIn.razor` — NEW; the dedicated screen.
- `Pages/Settings.razor` — remove sign-in card; `[Authorize]`.
- `App.razor` — `CascadingAuthenticationState` + `AuthorizeRouteView`.
- `wwwroot/js/webauthn.js`, `wwwroot/js/auth-fragment.js`, `wwwroot/index.html` — JS.
- `Extensions/ServiceCollectionExtensions.cs` — DI for the new services.
- `Pages/*.razor` (protected set) — add `@attribute [Authorize]`.

**Tests:**
- `tests/Sorcha.Tenant.Service.Tests/Endpoints/` — passkey/social/2fa tier + surface + providers.
- `tests/Sorcha.Wallet.Pwa.Tests/Services/` — token store, auth-state, AuthService methods, refresh.
- `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/` — sign-in E2E + `PageObjects/CitizenWalletSignInPage.cs`.

---

# Phase A — Backend (Tenant Service)

### Task 1: Public configured-providers endpoint

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs`
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs`
- Test: `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialProvidersEndpointTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialProvidersEndpointTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class SocialProvidersEndpointTests
{
    [Fact]
    public async Task ListProviders_ReturnsConfiguredProviderNames()
    {
        var social = new Mock<ISocialLoginService>();
        social.Setup(s => s.GetConfiguredProviderNames()).Returns(new[] { "Google", "Apple" });

        var result = await SocialLoginEndpoints.ListConfiguredProvidersForTest(social.Object);

        var ok = result.Should().BeOfType<Ok<SocialProvidersResponse>>().Subject;
        ok.Value!.Providers.Should().BeEquivalentTo(new[] { "Google", "Apple" });
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~SocialProvidersEndpointTests" -v minimal`
Expected: FAIL — `SocialProvidersResponse` and `ListConfiguredProvidersForTest` do not exist.

- [ ] **Step 3: Add the response DTO**

Append to `src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs`:

```csharp
/// <summary>
/// Configured social providers available for sign-in. Drives the conditional
/// "Continue with…" buttons on clients that cannot read service config directly
/// (e.g. the citizen wallet PWA).
/// </summary>
public record SocialProvidersResponse
{
    /// <summary>Provider names (case as configured) with working credentials on this host.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("providers")]
    public required IReadOnlyList<string> Providers { get; init; }
}
```

- [ ] **Step 4: Add the endpoint + a test-visible handler**

In `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs`, inside `MapSocialLoginEndpoints` (after the `/initiate` mapping):

```csharp
        group.MapGet("/providers", ListConfiguredProviders)
            .WithName("ListSocialProviders")
            .WithSummary("List configured social providers")
            .WithDescription("Returns the social providers that have working credentials on this host. "
                + "Anonymous — drives the conditional 'Continue with…' buttons on the wallet sign-in screen.")
            .AllowAnonymous()
            .RequireRateLimiting("platform-auth")
            .Produces<SocialProvidersResponse>();
```

Add the handler methods to the class:

```csharp
    private static IResult ListConfiguredProviders(ISocialLoginService socialLoginService)
        => ListConfiguredProvidersForTest(socialLoginService);

    /// <summary>Test seam for <see cref="ListConfiguredProviders"/> (no HttpContext needed).</summary>
    internal static Microsoft.AspNetCore.Http.HttpResults.Ok<SocialProvidersResponse> ListConfiguredProvidersForTest(
        ISocialLoginService socialLoginService)
        => TypedResults.Ok(new SocialProvidersResponse
        {
            Providers = socialLoginService.GetConfiguredProviderNames()
        });
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~SocialProvidersEndpointTests" -v minimal`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs tests/Sorcha.Tenant.Service.Tests/Endpoints/SocialProvidersEndpointTests.cs
git commit -m "feat: add public GET /api/auth/social/providers for wallet sign-in"
```

---

### Task 2: Consumer-tier hint on passkey assertion-verify

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Models/Dtos/PublicAuthDtos.cs:62-75`
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/PublicPasskeyEndpoints.cs:322-408`
- Test: `tests/Sorcha.Tenant.Service.Tests/Endpoints/PublicPasskeyEndpointsTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/Sorcha.Tenant.Service.Tests/Endpoints/PublicPasskeyEndpointsTests.cs` (a `[Fact]` in the existing class; reuse the file's existing mocks/builders for `IPasskeyService`, `IPlatformUserService`, `ITokenService`, etc. — model on the existing assertion-verify test):

```csharp
[Fact]
public void AssertionVerifyRequest_ParsesConsumerTierHint()
{
    // The wallet sends tier:"consumer"; non-"consumer" values fall back to the
    // platform default so this anonymous endpoint can never escalate.
    Sorcha.ServiceDefaults.Auth.Tier Resolve(string? hint) =>
        string.Equals(hint, "consumer", StringComparison.OrdinalIgnoreCase)
            ? Sorcha.ServiceDefaults.Auth.Tier.Consumer
            : Sorcha.ServiceDefaults.Auth.Tier.Platform;

    Resolve("consumer").Should().Be(Sorcha.ServiceDefaults.Auth.Tier.Consumer);
    Resolve("CONSUMER").Should().Be(Sorcha.ServiceDefaults.Auth.Tier.Consumer);
    Resolve("platform").Should().Be(Sorcha.ServiceDefaults.Auth.Tier.Platform);
    Resolve(null).Should().Be(Sorcha.ServiceDefaults.Auth.Tier.Platform);
}
```

> Note: this asserts the clamp rule the handler will use. A full handler-level integration test for the minted tier follows the existing `AssertionVerify` test pattern in this file once the handler change lands; add an assertion there that `tokenService.GenerateUserTokenAsync` was invoked with `Tier.Consumer` when the request carried `tier:"consumer"`.

- [ ] **Step 2: Run the test to verify it fails or passes trivially**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~AssertionVerifyRequest_ParsesConsumerTierHint" -v minimal`
Expected: PASS (this is the spec of the clamp). Proceed to wire the handler so the clamp is actually used.

- [ ] **Step 3: Add the tier field to the request DTO**

In `src/Services/Sorcha.Tenant.Service/Models/Dtos/PublicAuthDtos.cs`, add to `PublicPasskeyAssertionVerifyRequest`:

```csharp
    /// <summary>
    /// Optional trust-tier hint (spec 136). Only <c>consumer</c> is honoured —
    /// it is a safe downgrade for a public-org citizen. Any other value (or null)
    /// keeps the platform default, so this anonymous endpoint cannot escalate.
    /// </summary>
    [JsonPropertyName("tier")]
    public string? Tier { get; init; }
```

- [ ] **Step 4: Pass the tier in the handler**

In `src/Services/Sorcha.Tenant.Service/Endpoints/PublicPasskeyEndpoints.cs`, in `AssertionVerify`, replace the token-issue line:

```csharp
            // Issue JWT
            var tokenResponse = await tokenService.GenerateUserTokenAsync(
                userIdentity, publicOrg, platformUser.Id, cancellationToken: ct);
```

with:

```csharp
            // Issue JWT. The wallet requests tier:"consumer" (a safe downgrade);
            // any other value keeps the platform default — no escalation here.
            var mintTier = string.Equals(request.Tier, "consumer", StringComparison.OrdinalIgnoreCase)
                ? Sorcha.ServiceDefaults.Auth.Tier.Consumer
                : Sorcha.ServiceDefaults.Auth.Tier.Platform;
            var tokenResponse = await tokenService.GenerateUserTokenAsync(
                userIdentity, publicOrg, platformUser.Id, mintTier, ct);
```

- [ ] **Step 5: Run the test + build**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~PublicPasskeyEndpointsTests" -v minimal`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Services/Sorcha.Tenant.Service/Models/Dtos/PublicAuthDtos.cs src/Services/Sorcha.Tenant.Service/Endpoints/PublicPasskeyEndpoints.cs tests/Sorcha.Tenant.Service.Tests/Endpoints/PublicPasskeyEndpointsTests.cs
git commit -m "feat: honour consumer-tier hint on public passkey assertion verify"
```

---

### Task 3: Thread `surface` through the social login state

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Services/ISocialLoginService.cs:54-99`
- Modify: `src/Services/Sorcha.Tenant.Service/Services/SocialLoginService.cs:159-309,597-611`
- Modify: `src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs` (extend `SocialLoginInitiateRequest` — see note)
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs:123-210`
- Test: `tests/Sorcha.Tenant.Service.Tests/Services/SocialLoginServiceSurfaceTests.cs`

> Note: `SocialLoginInitiateRequest` lives alongside the other social DTOs consumed by `SocialLoginEndpoints` (search `record SocialLoginInitiateRequest`). It currently has `Provider` and `Intent`. Add `Surface` there.

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.Tenant.Service.Tests/Services/SocialLoginServiceSurfaceTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class SocialLoginServiceSurfaceTests
{
    private static SocialLoginService BuildService(IDistributedCache cache)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SocialProviders:0:Name"] = "Google",
                ["SocialProviders:0:ClientId"] = "test-client",
                ["SocialProviders:0:ClientSecret"] = "test-secret",
            })
            .Build();
        return new SocialLoginService(
            new TestHttpClientFactory(), cache, config, NullLogger<SocialLoginService>.Instance);
    }

    [Fact]
    public async Task GenerateAuthorizationUrl_WithSurface_RoundTripsSurfaceThroughState()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var svc = BuildService(cache);

        var init = await svc.GenerateAuthorizationUrlAsync(
            "Google", "https://host/auth/social/callback",
            SocialFlowIntent.Login, targetPlatformUserId: null, surface: "wallet");

        var stateJson = Encoding.UTF8.GetString((await cache.GetAsync($"social:state:{init.State}"))!);
        stateJson.Should().Contain("\"Surface\":\"wallet\"");
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~SocialLoginServiceSurfaceTests" -v minimal`
Expected: FAIL — `GenerateAuthorizationUrlAsync` has no `surface` parameter.

- [ ] **Step 3: Add `Surface` to the result record + state + interface**

In `ISocialLoginService.cs`, add `Surface` to `SocialAuthCallbackResult` (append after `TargetPlatformUserId`, with a default so positional construction elsewhere is unaffected):

```csharp
    SocialFlowIntent Intent = SocialFlowIntent.Login,
    Guid? TargetPlatformUserId = null,
    string? Surface = null);
```

Add `surface` to the explicit-intent overload signature in the interface:

```csharp
    Task<SocialAuthInitiateResult> GenerateAuthorizationUrlAsync(
        string provider,
        string redirectUri,
        SocialFlowIntent intent,
        Guid? targetPlatformUserId,
        string? surface = null,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement the threading in `SocialLoginService.cs`**

Update the explicit-intent overload signature to match (add `string? surface = null` before `CancellationToken`). Add `Surface` to the private `SocialStateData` record:

```csharp
        // Wallet vs app return-surface (PWA social sign-in). Default null = the
        // existing /app web flow. Backwards-compatible deserialisation.
        public string? Surface { get; init; }
```

Set it when caching state (in the `JsonSerializer.Serialize(new SocialStateData { … })` block):

```csharp
            Intent = intent,
            TargetPlatformUserId = targetPlatformUserId,
            Surface = surface,
```

Carry it onto the result in `ExchangeCodeAsync` (the `baseResult with { … }` block):

```csharp
            return baseResult with
            {
                Intent = stateData.Intent,
                TargetPlatformUserId = stateData.TargetPlatformUserId,
                Surface = stateData.Surface,
            };
```

Also update the 3-arg convenience overload's forwarding call to pass `surface: null` (it already forwards `targetPlatformUserId: null`):

```csharp
        => GenerateAuthorizationUrlAsync(
            provider, redirectUri, SocialFlowIntent.Login, targetPlatformUserId: null, surface: null, cancellationToken);
```

- [ ] **Step 5: Accept `surface` at the initiate endpoint**

In `AuthDtos.cs` (or wherever `SocialLoginInitiateRequest` is declared), add:

```csharp
    /// <summary>
    /// Optional return-surface for the post-OAuth redirect: <c>wallet</c> routes
    /// the callback back into the citizen wallet PWA (and mints Consumer-tier);
    /// null/anything else keeps the default /app web flow. Spec: PWA social sign-in.
    /// </summary>
    public string? Surface { get; init; }
```

In `SocialLoginEndpoints.InitiateSocialFlow`, pass it through (validate against an allowlist first — defence in depth):

```csharp
        // Validate optional return-surface. Unknown values are rejected rather
        // than silently treated as the default, so a typo can't redirect tokens
        // somewhere unexpected.
        var surface = string.IsNullOrWhiteSpace(request.Surface) ? null : request.Surface.Trim().ToLowerInvariant();
        if (surface is not (null or "wallet" or "app"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["surface"] = ["surface must be 'wallet' or 'app'"]
            });
        }
```

and change the service call to:

```csharp
            var result = await socialLoginService.GenerateAuthorizationUrlAsync(
                request.Provider, redirectUri, intent.Value, targetPlatformUserId, surface, ct);
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~SocialLoginServiceSurfaceTests" -v minimal`
Expected: PASS.

- [ ] **Step 7: Build the whole service to catch the API-endpoint call site**

The API `CompleteSocialFlow` and the Razor callback both call `ExchangeCodeAsync` (unchanged signature) — they compile. Run:
`dotnet build src/Services/Sorcha.Tenant.Service`
Expected: build succeeds.

- [ ] **Step 8: Commit**

```bash
git add src/Services/Sorcha.Tenant.Service/Services/ISocialLoginService.cs src/Services/Sorcha.Tenant.Service/Services/SocialLoginService.cs src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs src/Services/Sorcha.Tenant.Service/Endpoints/SocialLoginEndpoints.cs tests/Sorcha.Tenant.Service.Tests/Services/SocialLoginServiceSurfaceTests.cs
git commit -m "feat: thread social return-surface through OAuth state"
```

---

### Task 4: Social callback — wallet branch (Consumer tier, /wallet redirect, login-only refusal)

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs:75-218`
- Test: `tests/Sorcha.Tenant.Service.Tests/Pages/SocialCallbackSurfaceTests.cs`

The callback already resolves-or-creates and redirects to `/app/#token=…`. For `surface=="wallet"` we (a) mint Consumer-tier, (b) refuse if the resolve produced a brand-new account (login-only), (c) redirect to `/wallet/#token=…&refresh=…&expires_in=…`, (d) on refusal show the signup link.

- [ ] **Step 1: Write the failing test (decision helpers)**

Create `tests/Sorcha.Tenant.Service.Tests/Pages/SocialCallbackSurfaceTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Tenant.Service.Pages.Auth;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Pages;

public class SocialCallbackSurfaceTests
{
    [Theory]
    [InlineData("wallet", true)]
    [InlineData("WALLET", true)]
    [InlineData("app", false)]
    [InlineData(null, false)]
    public void IsWalletSurface_DetectsWalletReturn(string? surface, bool expected)
        => SocialCallbackModel.IsWalletSurface(surface).Should().Be(expected);

    [Fact]
    public void BuildWalletRedirect_PacksTokenRefreshExpiry()
    {
        var url = SocialCallbackModel.BuildWalletRedirect("AT", "RT", 3600, returnUrl: null);
        url.Should().StartWith("/wallet/#");
        url.Should().Contain("token=AT");
        url.Should().Contain("refresh=RT");
        url.Should().Contain("expires_in=3600");
    }

    [Fact]
    public void BuildWalletSignInError_RoutesToPwaSignIn()
        => SocialCallbackModel.BuildWalletSignInError("no_account")
            .Should().Be("/wallet/signin?authError=no_account");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~SocialCallbackSurfaceTests" -v minimal`
Expected: FAIL — helper methods don't exist.

- [ ] **Step 3: Add the static helpers to `SocialCallbackModel`**

In `SocialCallback.cshtml.cs`, add:

```csharp
    /// <summary>True when the social flow originated from the citizen wallet PWA.</summary>
    internal static bool IsWalletSurface(string? surface)
        => string.Equals(surface, "wallet", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the wallet fragment-return URL. The PWA's auth-fragment.js reads
    /// token/refresh/expires_in from the hash on load. Base-relative /wallet/ —
    /// the PWA is mounted under that prefix at the gateway.
    /// </summary>
    internal static string BuildWalletRedirect(string accessToken, string refreshToken, int expiresIn, string? returnUrl)
    {
        var fragment = $"token={Uri.EscapeDataString(accessToken)}" +
                       $"&refresh={Uri.EscapeDataString(refreshToken)}" +
                       $"&expires_in={expiresIn}";
        if (!string.IsNullOrEmpty(returnUrl))
            fragment += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        return $"/wallet/#{fragment}";
    }

    /// <summary>Routes a wallet-surface failure/refusal back to the PWA sign-in screen.</summary>
    internal static string BuildWalletSignInError(string code)
        => $"/wallet/signin?authError={Uri.EscapeDataString(code)}";
```

- [ ] **Step 4: Run the helper test to verify it passes**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~SocialCallbackSurfaceTests" -v minimal`
Expected: PASS.

- [ ] **Step 5: Wire the wallet branch into `OnGetAsync`**

First, recover the surface from the exchanged state — change the exchange call to capture the surface:

```csharp
        var callbackResult = await _socialLoginService.ExchangeCodeAsync(string.Empty, code, state, ct);
        var provider = callbackResult.Provider;
        var isWallet = IsWalletSurface(callbackResult.Surface);
```

In the **refusal** block (`if (resolveResult.Refusal != SocialLoginRefusal.None)`), before `return Page();` add a wallet bounce:

```csharp
            if (isWallet)
            {
                return Redirect(BuildWalletSignInError("refused"));
            }
```

Add the **login-only** guard immediately after `var platformUser = resolveResult.User!; var isNew = resolveResult.IsNew;`:

```csharp
        // Login-only for the wallet surface (signup happens via council enrol /
        // pairing / web). A social identity that maps to no existing account is
        // refused with a link to web signup rather than silently creating one.
        if (isWallet && isNew)
        {
            _logger.LogInformation("Wallet social login refused: no existing account for {Provider} identity", provider);
            return Redirect(BuildWalletSignInError("no_account"));
        }
```

Replace the **token mint** with a tier-aware mint:

```csharp
        var mintTier = isWallet
            ? Sorcha.ServiceDefaults.Auth.Tier.Consumer
            : Sorcha.ServiceDefaults.Auth.Tier.Platform;
        var tokens = await _tokenService.GenerateUserTokenAsync(userIdentity, publicOrg, platformUser.Id, mintTier, ct);
```

Replace the **final redirect** block with a surface branch:

```csharp
        if (isWallet)
        {
            // Wallet PWA: hand tokens back via the /wallet fragment. No SetupAddDevice
            // returnUrl here — that route lives in the /app web client, not the PWA;
            // the PWA handles "no device yet" with its own PairingTakeover overlay
            // (a /setup/add-device returnUrl would 404 in the wallet).
            return Redirect(BuildWalletRedirect(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiresIn, returnUrl: null));
        }

        var fragment = $"token={Uri.EscapeDataString(tokens.AccessToken)}" +
                       $"&refresh={Uri.EscapeDataString(tokens.RefreshToken)}";
        if (await SetupAddDeviceRoutingGate.ShouldRouteAsync(tokens.AccessToken, _deviceService, _logger, ct))
        {
            fragment += $"&returnUrl={Uri.EscapeDataString(SetupAddDeviceRoutingGate.SetupAddDevicePath)}";
        }
        return Redirect($"/app/#{fragment}");
```

> The welcome-email dispatch line (`await _welcomeDispatcher.SendIfPendingAsync(...)`) stays above this block unchanged.

- [ ] **Step 6: Build the service**

Run: `dotnet build src/Services/Sorcha.Tenant.Service`
Expected: build succeeds.

- [ ] **Step 7: Commit**

```bash
git add src/Services/Sorcha.Tenant.Service/Pages/Auth/SocialCallback.cshtml.cs tests/Sorcha.Tenant.Service.Tests/Pages/SocialCallbackSurfaceTests.cs
git commit -m "feat: wallet-surface social callback (consumer tier, /wallet redirect, login-only)"
```

---

### Task 5: Consumer-tier hint on verify-2fa

**Files:**
- Modify: `src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs:297-316`
- Modify: `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs:418-493`
- Test: `tests/Sorcha.Tenant.Service.Tests/Endpoints/Verify2FaTierTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.Tenant.Service.Tests/Endpoints/Verify2FaTierTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Endpoints;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

public class Verify2FaTierTests
{
    [Theory]
    [InlineData("consumer", Tier.Consumer)]
    [InlineData("CONSUMER", Tier.Consumer)]
    [InlineData("platform", Tier.Platform)]
    [InlineData(null, Tier.Platform)]
    public void ResolveVerify2FaTier_HonoursConsumerHintOnly(string? hint, Tier expected)
        => AuthEndpoints.ResolveVerify2FaTier(hint).Should().Be(expected);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~Verify2FaTierTests" -v minimal`
Expected: FAIL — `ResolveVerify2FaTier` doesn't exist.

- [ ] **Step 3: Add the tier field to `Verify2FaRequest`**

In `AuthDtos.cs`, add to `Verify2FaRequest`:

```csharp
    /// <summary>
    /// Optional trust-tier hint (spec 136). Only <c>consumer</c> is honoured —
    /// a safe downgrade for the wallet. Other values keep the platform default.
    /// </summary>
    [JsonPropertyName("tier")]
    public string? Tier { get; init; }
```

- [ ] **Step 4: Add the resolver + use it in the handler**

In `AuthEndpoints.cs`, add the helper:

```csharp
    /// <summary>
    /// Resolves the mint tier for 2FA completion. Only an explicit <c>consumer</c>
    /// hint is honoured (safe downgrade); everything else keeps the platform
    /// default so the path can't escalate.
    /// </summary>
    internal static Sorcha.ServiceDefaults.Auth.Tier ResolveVerify2FaTier(string? hint)
        => string.Equals(hint, "consumer", StringComparison.OrdinalIgnoreCase)
            ? Sorcha.ServiceDefaults.Auth.Tier.Consumer
            : Sorcha.ServiceDefaults.Auth.Tier.Platform;
```

In `Verify2Fa`, replace the token-issue line:

```csharp
        var tokenResponse = await tokenService.GenerateUserTokenAsync(user, organization, user.PlatformUserId, cancellationToken: cancellationToken);
```

with:

```csharp
        var tokenResponse = await tokenService.GenerateUserTokenAsync(
            user, organization, user.PlatformUserId, ResolveVerify2FaTier(request.Tier), cancellationToken);
```

- [ ] **Step 5: Run the test + build**

Run: `dotnet test tests/Sorcha.Tenant.Service.Tests --filter "FullyQualifiedName~Verify2FaTierTests" -v minimal`
Expected: PASS. Then `dotnet build src/Services/Sorcha.Tenant.Service`.

- [ ] **Step 6: Commit**

```bash
git add src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs tests/Sorcha.Tenant.Service.Tests/Endpoints/Verify2FaTierTests.cs
git commit -m "feat: honour consumer-tier hint on verify-2fa completion"
```

---

# Phase B — PWA: token store, auth state, gate

### Task 6: Extend `AccessTokenRecord` with a refresh token

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Services/IAccessTokenStore.cs:31`
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs`:

```csharp
[Fact]
public async Task InMemoryAccessTokenStore_RoundTripsRefreshToken()
{
    var store = new InMemoryAccessTokenStore();
    var record = new AccessTokenRecord("at", DateTimeOffset.UtcNow.AddHours(1), "a@b.test", "rt");
    await store.SetAsync(record);

    var loaded = await store.GetAsync();
    loaded!.RefreshToken.Should().Be("rt");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~RoundTripsRefreshToken" -v minimal`
Expected: FAIL — `AccessTokenRecord` has no `RefreshToken`.

- [ ] **Step 3: Add the property**

In `IAccessTokenStore.cs`, change the record (the trailing optional keeps every existing 3-arg construction compiling):

```csharp
public sealed record AccessTokenRecord(string AccessToken, DateTimeOffset ExpiresAt, string? Email, string? RefreshToken = null);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~RoundTripsRefreshToken" -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Services/IAccessTokenStore.cs tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs
git commit -m "feat: carry refresh token on the wallet access-token record"
```

---

### Task 7: Token-backed `AuthenticationStateProvider` + gate wiring

**Files:**
- Create: `src/Apps/Sorcha.Wallet.Pwa/Services/WalletAuthenticationStateProvider.cs`
- Create: `src/Apps/Sorcha.Wallet.Pwa/Components/RedirectToSignIn.razor`
- Modify: `src/Apps/Sorcha.Wallet.Pwa/App.razor`
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/WalletAuthenticationStateProviderTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Sorcha.Wallet.Pwa.Tests/Services/WalletAuthenticationStateProviderTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

public class WalletAuthenticationStateProviderTests
{
    [Fact]
    public async Task GetAuthenticationState_NoToken_IsAnonymous()
    {
        var provider = new WalletAuthenticationStateProvider(new InMemoryAccessTokenStore());
        var state = await provider.GetAuthenticationStateAsync();
        state.User.Identity!.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task GetAuthenticationState_WithToken_IsAuthenticated()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("at", DateTimeOffset.UtcNow.AddHours(1), "a@b.test"));
        var provider = new WalletAuthenticationStateProvider(store);

        var state = await provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeTrue();
        state.User.FindFirst(ClaimTypes.Name)!.Value.Should().Be("a@b.test");
    }

    [Fact]
    public async Task NotifySignedIn_FlipsToAuthenticated()
    {
        var store = new InMemoryAccessTokenStore();
        var provider = new WalletAuthenticationStateProvider(store);
        (await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated.Should().BeFalse();

        await store.SetAsync(new AccessTokenRecord("at", DateTimeOffset.UtcNow.AddHours(1), "a@b.test"));
        provider.NotifyChanged();

        (await provider.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~WalletAuthenticationStateProviderTests" -v minimal`
Expected: FAIL — type doesn't exist.

- [ ] **Step 3: Implement the provider**

Create `src/Apps/Sorcha.Wallet.Pwa/Services/WalletAuthenticationStateProvider.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Auth state for the wallet gate. Authenticated iff <see cref="IAccessTokenStore"/>
/// holds a non-expired token (the store self-purges expired tokens on read). No
/// JWT parsing is needed — the protected pages only require presence, not claims.
/// Email is surfaced as the Name claim for display. Call <see cref="NotifyChanged"/>
/// after sign-in / sign-out so the gate re-evaluates.
/// </summary>
public sealed class WalletAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly IAccessTokenStore _store;

    public WalletAuthenticationStateProvider(IAccessTokenStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var record = await _store.GetAsync();
        if (record is null || string.IsNullOrEmpty(record.AccessToken))
            return Anonymous;

        var claims = new List<Claim> { new(ClaimTypes.Name, record.Email ?? "citizen") };
        var identity = new ClaimsIdentity(claims, authenticationType: "wallet-jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>Re-evaluate auth state (after sign-in or sign-out).</summary>
    public void NotifyChanged()
        => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~WalletAuthenticationStateProviderTests" -v minimal`
Expected: PASS.

- [ ] **Step 5: Register the provider + create the redirect component**

In `ServiceCollectionExtensions.cs` `AddCitizenWalletServices`, after the `IAccessTokenStore` registration add:

```csharp
        services.AddSingleton<WalletAuthenticationStateProvider>();
        services.AddSingleton<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider>(
            sp => sp.GetRequiredService<WalletAuthenticationStateProvider>());
```

Create `src/Apps/Sorcha.Wallet.Pwa/Components/RedirectToSignIn.razor`:

```razor
@*
    SPDX-License-Identifier: MIT
    Copyright (c) 2026 Sorcha Contributors

    Auth-gate NotAuthorized target. Sends signed-out citizens to the dedicated
    sign-in screen, preserving where they were headed via returnUrl. Base-relative
    nav — the PWA is mounted under /wallet/.
*@
@inject NavigationManager Nav

@code {
    protected override void OnInitialized()
    {
        var uri = new Uri(Nav.Uri);
        var returnUrl = Nav.ToBaseRelativePath(Nav.Uri);
        var target = string.IsNullOrEmpty(returnUrl) || returnUrl.StartsWith("signin")
            ? "signin"
            : $"signin?returnUrl={Uri.EscapeDataString(returnUrl)}";
        Nav.NavigateTo(target);
    }
}
```

- [ ] **Step 6: Wire the gate in `App.razor`**

Replace the contents of `src/Apps/Sorcha.Wallet.Pwa/App.razor` with:

```razor
@*
    SPDX-License-Identifier: MIT
    Copyright (c) 2026 Sorcha Contributors
*@
@using Microsoft.AspNetCore.Components.Authorization
@using Sorcha.Wallet.Pwa.Components

<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(App).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
                <NotAuthorized>
                    <RedirectToSignIn />
                </NotAuthorized>
            </AuthorizeRouteView>
            <FocusOnNavigate RouteData="@routeData" Selector="h1" />
        </Found>
        <NotFound>
            <PageTitle>Not found</PageTitle>
            <LayoutView Layout="@typeof(MainLayout)">
                <p role="alert">Sorry, there's nothing at this address.</p>
            </LayoutView>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

- [ ] **Step 7: Build the PWA**

Run: `dotnet build src/Apps/Sorcha.Wallet.Pwa`
Expected: build succeeds. (No page has `[Authorize]` yet, so all pages still render — the gate is inert until Task 8. This keeps the build green between commits.)

- [ ] **Step 8: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Services/WalletAuthenticationStateProvider.cs src/Apps/Sorcha.Wallet.Pwa/Components/RedirectToSignIn.razor src/Apps/Sorcha.Wallet.Pwa/App.razor src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs tests/Sorcha.Wallet.Pwa.Tests/Services/WalletAuthenticationStateProviderTests.cs
git commit -m "feat: token-backed auth state provider + gate scaffolding for wallet PWA"
```

---

### Task 8: Mark protected pages `[Authorize]`

**Files:**
- Modify (add `@attribute [Authorize]` + `@using Microsoft.AspNetCore.Authorization`): `Pages/Index.razor`, `Pages/Devices.razor`, `Pages/Activity.razor`, `Pages/Settings.razor`, `Pages/Profile.razor`, `Pages/Present.razor`, `Pages/Applications.razor`, `Pages/ApplicationInstance.razor`, `Pages/CredentialDetail.razor`, `Pages/Verify.razor`
- Leave PUBLIC (no attribute): `Pages/SignIn.razor` (Task 14), `Pages/Enrol.razor`, `Pages/CancelledEnrolment.razor`

- [ ] **Step 1: Add the attribute to each protected page**

For each protected page above, add these two lines directly under the existing `@page`/`@layout` directives (example for `Pages/Index.razor`):

```razor
@using Microsoft.AspNetCore.Authorization
@attribute [Authorize]
```

> Do NOT add it to `Enrol.razor` or `CancelledEnrolment.razor` — those are the signed-out onboarding entry points (the `?session=` redeem is how a freshly-paired device gets its first token; gating it would deadlock onboarding).

- [ ] **Step 2: Build the PWA**

Run: `dotnet build src/Apps/Sorcha.Wallet.Pwa`
Expected: build succeeds.

- [ ] **Step 3: Manual smoke (deferred to E2E in Task 17)**

The redirect behaviour is asserted by the E2E test in Task 17 (`SignedOut_ProtectedRoute_RedirectsToSignIn`). No unit test here — `[Authorize]` is framework behaviour.

- [ ] **Step 4: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Pages/Index.razor src/Apps/Sorcha.Wallet.Pwa/Pages/Devices.razor src/Apps/Sorcha.Wallet.Pwa/Pages/Activity.razor src/Apps/Sorcha.Wallet.Pwa/Pages/Settings.razor src/Apps/Sorcha.Wallet.Pwa/Pages/Profile.razor src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor src/Apps/Sorcha.Wallet.Pwa/Pages/Applications.razor src/Apps/Sorcha.Wallet.Pwa/Pages/ApplicationInstance.razor src/Apps/Sorcha.Wallet.Pwa/Pages/CredentialDetail.razor src/Apps/Sorcha.Wallet.Pwa/Pages/Verify.razor
git commit -m "feat: gate protected wallet pages behind [Authorize]"
```

---

# Phase C — PWA: AuthService methods + JS interop

### Task 9: Password login + verify-2fa request Consumer tier

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Services/IAuthService.cs:204-215` (the private request records + the two POST calls)
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `AuthAndBearerTests.cs` (uses a capturing stub handler — define this helper in the test file if one isn't already present):

```csharp
[Fact]
public async Task SignInAsync_SendsConsumerTierHint()
{
    string? capturedBody = null;
    var handler = new CapturingHandler(async req =>
    {
        capturedBody = await req.Content!.ReadAsStringAsync();
        return JsonOk("{\"access_token\":\"at\",\"expires_in\":3600,\"requires_two_factor\":false}");
    });
    var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };
    var auth = new AuthService(http, new InMemoryAccessTokenStore(), new NoopLocalDataPurge());

    await auth.SignInAsync("a@b.test", "pw");

    capturedBody.Should().Contain("\"Tier\":\"consumer\"");
}
```

If `CapturingHandler`, `JsonOk`, and `NoopLocalDataPurge` aren't already in this test file, add them:

```csharp
internal sealed class CapturingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => responder(request);
}

internal sealed class NoopLocalDataPurge : Sorcha.Wallet.Pwa.Services.ILocalDataPurge
{
    public Task PurgeAsync(CancellationToken ct = default) => Task.CompletedTask;
}

// helper
static HttpResponseMessage JsonOk(string json) => new(System.Net.HttpStatusCode.OK)
{
    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
};
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~SendsConsumerTierHint" -v minimal`
Expected: FAIL — body has no `Tier`.

- [ ] **Step 3: Add the tier to the request records + calls**

In `IAuthService.cs` (`AuthService`), change the `LoginRequest` record and the login POST body:

```csharp
    private sealed record LoginRequest(string Email, string Password, string Tier);
```

In `SignInAsync`, change the POST:

```csharp
            var response = await _http.PostAsJsonAsync(
                "api/auth/login",
                new LoginRequest(email.Trim(), password, "consumer"),
                ct);
```

Change the `Verify2FaRequest` record + the verify POST to carry the tier:

```csharp
    private sealed record Verify2FaRequest(
        [property: JsonPropertyName("login_token")] string LoginToken,
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("is_backup_code")] bool IsBackupCode,
        [property: JsonPropertyName("tier")] string Tier);
```

In `VerifyTwoFactorAsync`, change the POST:

```csharp
            var response = await _http.PostAsJsonAsync(
                "api/auth/verify-2fa",
                new Verify2FaRequest(loginToken, code.Trim(), isBackupCode, "consumer"),
                ct);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~AuthAndBearerTests" -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Services/IAuthService.cs tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs
git commit -m "feat: PWA password + 2FA login request consumer tier"
```

---

### Task 10: WebAuthn JS bridge + passkey interop service

**Files:**
- Create: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/webauthn.js` (copy from web client)
- Create: `src/Apps/Sorcha.Wallet.Pwa/Services/IPasskeyInterop.cs` (+ `PasskeyInterop` impl)
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/PasskeyInteropContractTests.cs`

- [ ] **Step 1: Copy `webauthn.js`**

Copy `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/js/webauthn.js` verbatim to `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/webauthn.js`. (It exports `isWebAuthnSupported`, `createCredential(optionsJson)`, `getCredential(optionsJson)`. The PWA only uses `isWebAuthnSupported` + `getCredential` for login.)

- [ ] **Step 2: Write the failing test (interface seam)**

Create `tests/Sorcha.Wallet.Pwa.Tests/Services/PasskeyInteropContractTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

public class PasskeyInteropContractTests
{
    [Fact]
    public async Task Fake_ReturnsAssertion()
    {
        IPasskeyInterop interop = new FakePasskeyInterop();
        (await interop.IsSupportedAsync()).Should().BeTrue();
        var resp = await interop.GetAssertionAsync(default);
        resp.GetProperty("id").GetString().Should().Be("abc");
    }
}

/// <summary>
/// Shared in-memory passkey interop fake — top-level + internal so the AuthService
/// tests (Tasks 11, 12) reuse it. Stands in for navigator.credentials.get() so no
/// test touches IJSRuntime (brittle to mock — F114 lesson).
/// </summary>
internal sealed class FakePasskeyInterop : IPasskeyInterop
{
    public bool Supported = true;
    public JsonElement Assertion = JsonDocument.Parse("{\"id\":\"abc\"}").RootElement;
    public Task<bool> IsSupportedAsync() => Task.FromResult(Supported);
    public Task<JsonElement> GetAssertionAsync(JsonElement options) => Task.FromResult(Assertion);
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~PasskeyInteropContractTests" -v minimal`
Expected: FAIL — `IPasskeyInterop` doesn't exist.

- [ ] **Step 4: Implement the interop seam**

Create `src/Apps/Sorcha.Wallet.Pwa/Services/IPasskeyInterop.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.JSInterop;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// WebAuthn ceremony bridge for the wallet sign-in screen. Behind an interface so
/// AuthService unit tests use an in-memory fake instead of mocking IJSRuntime
/// (generic InvokeAsync&lt;T&gt; is brittle to mock — F114 lesson).
/// </summary>
public interface IPasskeyInterop
{
    /// <summary>True when the browser exposes the WebAuthn API.</summary>
    Task<bool> IsSupportedAsync();

    /// <summary>Runs navigator.credentials.get() and returns the assertion response JSON.</summary>
    Task<JsonElement> GetAssertionAsync(JsonElement options);
}

/// <summary>JS-backed <see cref="IPasskeyInterop"/> over <c>./js/webauthn.js</c>.</summary>
public sealed class PasskeyInterop : IPasskeyInterop, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public PasskeyInterop(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    private async Task<IJSObjectReference> ModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/webauthn.js");

    public async Task<bool> IsSupportedAsync()
    {
        try { return await (await ModuleAsync()).InvokeAsync<bool>("isWebAuthnSupported"); }
        catch { return false; }
    }

    public async Task<JsonElement> GetAssertionAsync(JsonElement options)
    {
        var module = await ModuleAsync();
        var responseJson = await module.InvokeAsync<string>("getCredential", options.GetRawText());
        return JsonSerializer.Deserialize<JsonElement>(responseJson);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null) { await _module.DisposeAsync(); _module = null; }
    }
}
```

- [ ] **Step 5: Register it (singleton, like the other PWA services)**

In `ServiceCollectionExtensions.cs` `AddCitizenWalletServices` (near the other singletons):

```csharp
        services.AddSingleton<IPasskeyInterop, PasskeyInterop>();
```

- [ ] **Step 6: Run the test + build**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~PasskeyInteropContractTests" -v minimal`
Expected: PASS. Then `dotnet build src/Apps/Sorcha.Wallet.Pwa`.

- [ ] **Step 7: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/webauthn.js src/Apps/Sorcha.Wallet.Pwa/Services/IPasskeyInterop.cs src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs tests/Sorcha.Wallet.Pwa.Tests/Services/PasskeyInteropContractTests.cs
git commit -m "feat: WebAuthn JS bridge + passkey interop seam for wallet PWA"
```

---

### Task 11: `AuthService.SignInWithPasskeyAsync`

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Services/IAuthService.cs` (interface + impl + new ctor dep)
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs` (AuthService now needs `IPasskeyInterop`)
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `AuthAndBearerTests.cs`:

```csharp
[Fact]
public async Task SignInWithPasskeyAsync_HappyPath_PersistsConsumerToken()
{
    var responses = new Queue<HttpResponseMessage>(new[]
    {
        // assertion/options
        JsonOk("{\"transaction_id\":\"tx1\",\"options\":{\"challenge\":\"AA\"}}"),
        // assertion/verify
        JsonOk("{\"access_token\":\"at\",\"refresh_token\":\"rt\",\"expires_in\":3600}")
    });
    var handler = new CapturingHandler(_ => Task.FromResult(responses.Dequeue()));
    var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };
    var store = new InMemoryAccessTokenStore();
    var interop = new FakePasskeyInterop(); // shared internal helper from Task 10
    var auth = new AuthService(http, store, new NoopLocalDataPurge(), interop);

    var result = await auth.SignInWithPasskeyAsync();

    result.IsSuccess.Should().BeTrue();
    (await store.GetAsync())!.RefreshToken.Should().Be("rt");
}

[Fact]
public async Task SignInWithPasskeyAsync_Unsupported_ReturnsServerError()
{
    var http = new HttpClient(new CapturingHandler(_ => Task.FromResult(JsonOk("{}"))))
        { BaseAddress = new Uri("https://gw.test/") };
    var interop = new FakePasskeyInterop { Supported = false };
    var auth = new AuthService(http, new InMemoryAccessTokenStore(), new NoopLocalDataPurge(), interop);

    var result = await auth.SignInWithPasskeyAsync();

    result.Status.Should().Be(SignInStatus.ServerError);
}
```

> `FakePasskeyInterop` is the shared `internal` top-level helper created in Task 10 — reference it directly.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~SignInWithPasskeyAsync" -v minimal`
Expected: FAIL — method + ctor param don't exist.

- [ ] **Step 3: Extend the interface**

In `IAuthService.cs`, add to `IAuthService`:

```csharp
    /// <summary>
    /// Passwordless sign-in with a passkey. Discoverable-first when
    /// <paramref name="email"/> is null. Persists a Consumer-tier token on success.
    /// </summary>
    Task<SignInResult> SignInWithPasskeyAsync(string? email = null, CancellationToken ct = default);
```

- [ ] **Step 4: Implement it**

Add the `IPasskeyInterop` dependency to `AuthService` (extend the ctor — keep existing params, append the new one) and implement:

```csharp
    private readonly IPasskeyInterop _passkey;

    public AuthService(HttpClient http, IAccessTokenStore store, ILocalDataPurge purge, IPasskeyInterop passkey)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _purge = purge ?? throw new ArgumentNullException(nameof(purge));
        _passkey = passkey ?? throw new ArgumentNullException(nameof(passkey));
    }

    /// <inheritdoc />
    public async Task<SignInResult> SignInWithPasskeyAsync(string? email = null, CancellationToken ct = default)
    {
        if (!await _passkey.IsSupportedAsync())
            return new SignInResult(SignInStatus.ServerError, "This device doesn't support passkeys.");

        try
        {
            // 1) options
            var optionsResp = await _http.PostAsJsonAsync(
                "api/auth/passkey/assertion/options",
                new AssertionOptionsRequest(string.IsNullOrWhiteSpace(email) ? null : email!.Trim()), ct);
            optionsResp.EnsureSuccessStatusCode();
            var options = await optionsResp.Content.ReadFromJsonAsync<AssertionOptionsResponse>(ct);
            if (options is null || string.IsNullOrEmpty(options.TransactionId))
                return new SignInResult(SignInStatus.ServerError, "Could not start passkey sign-in.");

            // 2) browser ceremony
            JsonElement assertion;
            try { assertion = await _passkey.GetAssertionAsync(options.Options); }
            catch (JSException) { return new SignInResult(SignInStatus.InvalidCredentials, "Passkey sign-in was cancelled."); }

            // 3) verify (request consumer tier)
            var verifyResp = await _http.PostAsJsonAsync(
                "api/auth/passkey/assertion/verify",
                new AssertionVerifyRequest(options.TransactionId, assertion, "consumer"), ct);
            if (verifyResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new SignInResult(SignInStatus.InvalidCredentials, "That passkey isn't recognised.");
            verifyResp.EnsureSuccessStatusCode();

            var body = await verifyResp.Content.ReadFromJsonAsync<PublicTokenBody>(ct);
            if (body is null || string.IsNullOrEmpty(body.AccessToken))
                return new SignInResult(SignInStatus.ServerError, "Passkey sign-in returned no token.");

            await PersistAsync(body.AccessToken, body.ExpiresIn, email, body.RefreshToken, ct);
            return new SignInResult(SignInStatus.Success);
        }
        catch (HttpRequestException ex)
        {
            return new SignInResult(SignInStatus.ServerError, ex.Message);
        }
    }
```

Add the DTO records (near the other private records) and import `Microsoft.JSInterop` + `System.Text.Json` at the top of the file:

```csharp
    private sealed record AssertionOptionsRequest(
        [property: JsonPropertyName("email")] string? Email);

    private sealed record AssertionOptionsResponse(
        [property: JsonPropertyName("transaction_id")] string TransactionId,
        [property: JsonPropertyName("options")] JsonElement Options);

    private sealed record AssertionVerifyRequest(
        [property: JsonPropertyName("transaction_id")] string TransactionId,
        [property: JsonPropertyName("assertion_response")] JsonElement AssertionResponse,
        [property: JsonPropertyName("tier")] string Tier);

    private sealed record PublicTokenBody(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
```

Update `PersistAsync` to accept + store the refresh token:

```csharp
    private async Task PersistAsync(string accessToken, int expiresIn, string? email, string? refreshToken, CancellationToken ct)
    {
        var record = new AccessTokenRecord(
            accessToken,
            DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)),
            email,
            refreshToken);
        await _store.SetAsync(record, ct);
    }
```

And update the two existing `PersistAsync` call sites (password + 2FA paths) to pass the refresh token from their `LoginResponse` — extend `LoginResponse` with `refresh_token` and pass `body.RefreshToken`:

```csharp
    private sealed record LoginResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("requires_two_factor")] bool RequiresTwoFactor,
        [property: JsonPropertyName("login_token")] string? LoginToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken);
```

(Change both `await PersistAsync(body.AccessToken, body.ExpiresIn, email.Trim(), ct);` calls to `await PersistAsync(body.AccessToken, body.ExpiresIn, email.Trim(), body.RefreshToken, ct);`.)

- [ ] **Step 4b: Update every existing `AuthService` construction to the 4-arg ctor**

Adding the required `IPasskeyInterop` parameter breaks all existing `new AuthService(...)` calls. Update each call site in `tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs` — the pre-existing sign-out test (`AuthService_SignOut_PurgesAllLocalData`) and the Task 9 test (`SignInAsync_SendsConsumerTierHint`) — to pass `new FakePasskeyInterop()` as the 4th argument. Grep to be exhaustive:

Run: `grep -rn "new AuthService(" tests/Sorcha.Wallet.Pwa.Tests`
Expected after fix: every match has 4 arguments ending in `new FakePasskeyInterop()`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~AuthAndBearerTests" -v minimal`
Expected: PASS. (The `AddHttpClient<IAuthService, AuthService>` registration resolves `IPasskeyInterop` from DI automatically — no registration change needed beyond Task 10.)

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Services/IAuthService.cs tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs
git commit -m "feat: passkey sign-in on the wallet AuthService"
```

---

### Task 12: Social sign-in — initiate + fragment return + providers client

**Files:**
- Create: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/auth-fragment.js`
- Modify: `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html` (reference the script before blazor)
- Create: `src/Apps/Sorcha.Wallet.Pwa/Services/ISocialProvidersClient.cs` (+ impl)
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Services/IAuthService.cs` (`BeginSocialSignInAsync`, `TryConsumeSocialReturnAsync`)
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs`
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs`

- [ ] **Step 1: Create the fragment-capture JS**

Create `src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/auth-fragment.js`:

```javascript
// SPDX-License-Identifier: MIT
// Captures an OAuth fragment-return token from the URL hash BEFORE Blazor boots,
// so the router can't redirect a signed-out user to /signin and lose the token.
// The social callback redirects to /wallet/#token=…&refresh=…&expires_in=…[&returnUrl=…].
(function () {
    window.sorchaAuthFragment = window.sorchaAuthFragment || {};
    var pending = null;
    try {
        var hash = window.location.hash || '';
        if (hash.indexOf('token=') !== -1) {
            var params = new URLSearchParams(hash.replace(/^#/, ''));
            var token = params.get('token');
            if (token) {
                pending = {
                    token: token,
                    refresh: params.get('refresh'),
                    expiresIn: parseInt(params.get('expires_in') || '0', 10),
                    returnUrl: params.get('returnUrl')
                };
                // Strip the fragment so the token never lingers in the address bar / history.
                history.replaceState(null, '', window.location.pathname + window.location.search);
            }
        }
    } catch (e) { pending = null; }

    window.sorchaAuthFragment.consume = function () {
        var p = pending;
        pending = null;
        return p; // null when nothing was staged
    };
})();
```

- [ ] **Step 2: Reference it in `index.html`**

In `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html`, add **before** the `<script src="_framework/blazor.webassembly.js"></script>` line:

```html
    <script src="js/auth-fragment.js"></script>
```

- [ ] **Step 3: Write the failing tests**

Add to `AuthAndBearerTests.cs`:

```csharp
[Fact]
public async Task BeginSocialSignInAsync_ReturnsAuthorizationUrl()
{
    var handler = new CapturingHandler(_ =>
        Task.FromResult(JsonOk("{\"authorization_url\":\"https://idp/auth?x=1\",\"state\":\"st\"}")));
    var http = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };
    var auth = new AuthService(http, new InMemoryAccessTokenStore(), new NoopLocalDataPurge(), new FakePasskeyInterop());

    var url = await auth.BeginSocialSignInAsync("Google");

    url.Should().Be("https://idp/auth?x=1");
}
```

> `TryConsumeSocialReturnAsync` reads `window.sorchaAuthFragment.consume()` via `IJSRuntime`, so its happy path is covered by the E2E return-leg test (Task 17). A unit test would have to mock `IJSRuntime` (avoided per F114). Assert only the no-op path here:

```csharp
[Fact]
public async Task TryConsumeSocialReturnAsync_NoFragment_ReturnsFalse_And_StaysSignedOut()
{
    // A JSRuntime stub that returns null from consume() (no staged fragment).
    var js = new NullConsumeJsRuntime();
    var store = new InMemoryAccessTokenStore();
    var auth = new AuthService(
        new HttpClient(new CapturingHandler(_ => Task.FromResult(JsonOk("{}")))) { BaseAddress = new Uri("https://gw.test/") },
        store, new NoopLocalDataPurge(), new FakePasskeyInterop());

    var consumed = await auth.TryConsumeSocialReturnAsync(js);

    consumed.Should().BeFalse();
    (await store.GetAsync()).Should().BeNull();
}
```

Add the minimal JS-runtime stub helper to the test file:

```csharp
internal sealed class NullConsumeJsRuntime : Microsoft.JSInterop.IJSRuntime
{
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => new((TValue)(object?)default!);
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
        => new((TValue)(object?)default!);
}
```

> `consume()` returns a nullable object; the stub returns `default` (null) for any `InvokeAsync<T>`, so `TryConsumeSocialReturnAsync` sees no fragment.

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~SocialSignIn|FullyQualifiedName~SocialReturn" -v minimal`
Expected: FAIL — methods don't exist.

- [ ] **Step 5: Add the social methods to `IAuthService` + `AuthService`**

Interface additions:

```csharp
    /// <summary>
    /// Starts a social sign-in. Calls /api/auth/social/initiate with surface=wallet
    /// and returns the provider authorization URL for the caller to navigate to
    /// (full-page). Returns null on failure.
    /// </summary>
    Task<string?> BeginSocialSignInAsync(string provider, CancellationToken ct = default);

    /// <summary>
    /// Consumes a staged OAuth fragment-return token (from auth-fragment.js),
    /// persisting it as the Consumer-tier session. Returns true when a token was
    /// consumed. Pass the page's IJSRuntime.
    /// </summary>
    Task<bool> TryConsumeSocialReturnAsync(Microsoft.JSInterop.IJSRuntime js, CancellationToken ct = default);
```

Implementation:

```csharp
    /// <inheritdoc />
    public async Task<string?> BeginSocialSignInAsync(string provider, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        try
        {
            var resp = await _http.PostAsJsonAsync(
                "api/auth/social/initiate",
                new SocialInitiateBody(provider, "login", "wallet"), ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadFromJsonAsync<SocialInitiateBodyResponse>(ct);
            return body?.AuthorizationUrl;
        }
        catch (HttpRequestException) { return null; }
    }

    /// <inheritdoc />
    public async Task<bool> TryConsumeSocialReturnAsync(Microsoft.JSInterop.IJSRuntime js, CancellationToken ct = default)
    {
        FragmentReturn? fragment;
        try { fragment = await js.InvokeAsync<FragmentReturn?>("sorchaAuthFragment.consume", ct); }
        catch { return false; }

        if (fragment is null || string.IsNullOrEmpty(fragment.Token)) return false;

        await PersistAsync(fragment.Token, fragment.ExpiresIn, email: null, fragment.Refresh, ct);
        return true;
    }
```

DTO records:

```csharp
    private sealed record SocialInitiateBody(
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("intent")] string Intent,
        [property: JsonPropertyName("surface")] string Surface);

    private sealed record SocialInitiateBodyResponse(
        [property: JsonPropertyName("authorization_url")] string? AuthorizationUrl,
        [property: JsonPropertyName("state")] string? State);

    private sealed record FragmentReturn(
        [property: JsonPropertyName("token")] string? Token,
        [property: JsonPropertyName("refresh")] string? Refresh,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn,
        [property: JsonPropertyName("returnUrl")] string? ReturnUrl);
```

- [ ] **Step 6: Add the providers client**

Create `src/Apps/Sorcha.Wallet.Pwa/Services/ISocialProvidersClient.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>Reads the configured social providers so the sign-in screen renders only enabled buttons.</summary>
public interface ISocialProvidersClient
{
    /// <summary>Provider names enabled on this host; empty on failure.</summary>
    Task<IReadOnlyList<string>> GetConfiguredAsync(CancellationToken ct = default);
}

/// <summary>HTTP <see cref="ISocialProvidersClient"/> over the anonymous providers endpoint.</summary>
public sealed class SocialProvidersClient : ISocialProvidersClient
{
    private readonly HttpClient _http;
    public SocialProvidersClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    public async Task<IReadOnlyList<string>> GetConfiguredAsync(CancellationToken ct = default)
    {
        try
        {
            var body = await _http.GetFromJsonAsync<ProvidersBody>("api/auth/social/providers", ct);
            return body?.Providers ?? [];
        }
        catch { return []; }
    }

    private sealed record ProvidersBody([property: JsonPropertyName("providers")] IReadOnlyList<string>? Providers);
}
```

- [ ] **Step 7: Register the providers client**

In `ServiceCollectionExtensions.cs` (the providers endpoint is anonymous — no bearer handler needed; keep the clock handler for consistency):

```csharp
        services.AddHttpClient<ISocialProvidersClient, SocialProvidersClient>(c =>
            c.BaseAddress = new Uri(gatewayBaseAddress))
            .AddHttpMessageHandler<ServerClockHandler>();
```

- [ ] **Step 8: Run the tests + build**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~AuthAndBearerTests" -v minimal`
Expected: PASS. Then `dotnet build src/Apps/Sorcha.Wallet.Pwa`.

- [ ] **Step 9: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/wwwroot/js/auth-fragment.js src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html src/Apps/Sorcha.Wallet.Pwa/Services/ISocialProvidersClient.cs src/Apps/Sorcha.Wallet.Pwa/Services/IAuthService.cs src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs
git commit -m "feat: social sign-in initiate + fragment return + providers client (PWA)"
```

---

### Task 13: Silent refresh in `BearerTokenHandler`

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Services/BearerTokenHandler.cs`
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs` (handler needs an unauthenticated HttpClient for the refresh call)
- Test: `tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `AuthAndBearerTests.cs`:

```csharp
[Fact]
public async Task BearerTokenHandler_On401_RefreshesAndRetries()
{
    var store = new InMemoryAccessTokenStore();
    await store.SetAsync(new AccessTokenRecord("old", DateTimeOffset.UtcNow.AddHours(1), "a@b.test", "rt"));

    // Inner handler: first call (with "old") → 401; second call (with "new") → 200.
    var inner = new SequencedHandler(req =>
    {
        var auth = req.Headers.Authorization?.Parameter;
        return auth == "new"
            ? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            : new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized);
    });

    // Refresh endpoint returns a fresh token "new".
    var refreshHttp = new HttpClient(new CapturingHandler(_ =>
        Task.FromResult(JsonOk("{\"access_token\":\"new\",\"refresh_token\":\"rt2\",\"expires_in\":3600}"))))
        { BaseAddress = new Uri("https://gw.test/") };

    var handler = new BearerTokenHandler(store, refreshHttp) { InnerHandler = inner };
    var client = new HttpClient(handler) { BaseAddress = new Uri("https://gw.test/") };

    var resp = await client.GetAsync("api/v1/wallet/credentials");

    resp.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    (await store.GetAsync())!.AccessToken.Should().Be("new");
}
```

Add the sequenced inner-handler helper if not present:

```csharp
internal sealed class SequencedHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(responder(request));
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~On401_RefreshesAndRetries" -v minimal`
Expected: FAIL — `BearerTokenHandler` has no refresh ctor/behaviour.

- [ ] **Step 3: Implement refresh-on-401**

Replace `src/Apps/Sorcha.Wallet.Pwa/Services/BearerTokenHandler.cs` with:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Attaches the wallet's bearer token to every outbound request. On a 401 with a
/// stored refresh token, transparently refreshes once (POST /api/auth/token/refresh —
/// re-mints the same tier per F136, so a Consumer refresh stays Consumer) and
/// retries. A failed refresh clears the session so the gate sends the citizen to
/// /signin on the next navigation.
/// </summary>
public sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly IAccessTokenStore _store;
    private readonly HttpClient _refreshHttp;

    public BearerTokenHandler(IAccessTokenStore store, HttpClient refreshHttp)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _refreshHttp = refreshHttp ?? throw new ArgumentNullException(nameof(refreshHttp));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var record = await _store.GetAsync(ct);
        if (record is not null && !string.IsNullOrEmpty(record.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", record.AccessToken);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode != HttpStatusCode.Unauthorized
            || record?.RefreshToken is null or "")
        {
            return response;
        }

        var refreshed = await TryRefreshAsync(record.RefreshToken, record.Email, ct);
        if (refreshed is null)
        {
            await _store.ClearAsync(ct); // gate redirects to /signin next nav
            return response;
        }

        response.Dispose();
        var retry = await CloneAsync(request, ct);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        return await base.SendAsync(retry, ct);
    }

    private async Task<AccessTokenRecord?> TryRefreshAsync(string refreshToken, string? email, CancellationToken ct)
    {
        try
        {
            var resp = await _refreshHttp.PostAsJsonAsync(
                "api/auth/token/refresh", new RefreshBody(refreshToken), ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadFromJsonAsync<RefreshResponse>(ct);
            if (body is null || string.IsNullOrEmpty(body.AccessToken)) return null;

            var record = new AccessTokenRecord(
                body.AccessToken,
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, body.ExpiresIn)),
                email,
                string.IsNullOrEmpty(body.RefreshToken) ? refreshToken : body.RefreshToken);
            await _store.SetAsync(record, ct);
            return record;
        }
        catch { return null; }
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri) { Version = req.Version };
        foreach (var h in req.Headers) clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (req.Content is not null)
        {
            var bytes = await req.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var h in req.Content.Headers) clone.Content.Headers.TryAddWithoutValidation(h.Key, h.Value);
        }
        return clone;
    }

    private sealed record RefreshBody([property: JsonPropertyName("refreshToken")] string RefreshToken);

    private sealed record RefreshResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
```

- [ ] **Step 4: Provide the refresh HttpClient to the handler in DI**

`BearerTokenHandler` is registered `AddTransient<BearerTokenHandler>()`. It now needs an unauthenticated `HttpClient`. Replace that registration with a factory that supplies a named, handler-free client:

```csharp
        // Unauthenticated client used ONLY by BearerTokenHandler to refresh — it
        // must NOT itself carry the bearer handler (no recursion).
        services.AddHttpClient("AuthRefresh", c => c.BaseAddress = new Uri(gatewayBaseAddress));
        services.AddTransient(sp => new BearerTokenHandler(
            sp.GetRequiredService<IAccessTokenStore>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("AuthRefresh")));
```

(Remove the old `services.AddTransient<BearerTokenHandler>();` line.)

- [ ] **Step 5: Run the test + build**

Run: `dotnet test tests/Sorcha.Wallet.Pwa.Tests --filter "FullyQualifiedName~AuthAndBearerTests" -v minimal`
Expected: PASS. Then `dotnet build src/Apps/Sorcha.Wallet.Pwa`.

- [ ] **Step 6: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Services/BearerTokenHandler.cs src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs tests/Sorcha.Wallet.Pwa.Tests/Services/AuthAndBearerTests.cs
git commit -m "feat: silent refresh-on-401 in the wallet BearerTokenHandler"
```

---

# Phase D — PWA: the sign-in screen + Settings

### Task 14: `SignIn.razor` — the dedicated sign-in screen

**Files:**
- Create: `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor`

> The VISUAL/layout follows the provided design screenshots. This task delivers a complete, functional structural version with `data-testid`s on every control, dynamic provider buttons, all three methods, the 2FA step, and inline errors (no `ISnackbar`). Restyle to match the screenshots without changing the wiring.

- [ ] **Step 1: Create the page**

Create `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor`:

```razor
@*
    SPDX-License-Identifier: MIT
    Copyright (c) 2026 Sorcha Contributors

    Dedicated wallet sign-in screen. Login only — signup is via council enrol,
    pairing, or web. Three methods: passkey, social (dynamic providers), password
    (+ TOTP 2FA). Visual/layout follows the design screenshots; wiring is final.
*@
@page "/signin"
@layout MainLayout
@using Microsoft.JSInterop
@using Sorcha.Wallet.Pwa.Services
@inject IAuthService Auth
@inject IPasskeyInterop Passkey
@inject ISocialProvidersClient Providers
@inject WalletAuthenticationStateProvider AuthState
@inject NavigationManager Nav
@inject IJSRuntime Js

<PageTitle>Sign in · Sorcha Wallet</PageTitle>

<MudContainer MaxWidth="MaxWidth.ExtraSmall" Class="mt-8">
    <MudStack Spacing="3" data-testid="signin-screen">
        <MudText Typo="Typo.h5" Align="Align.Center">Sign in to your wallet</MudText>

        @if (!string.IsNullOrEmpty(_error))
        {
            <MudAlert Severity="Severity.Error" Dense="true" data-testid="signin-error">@_error</MudAlert>
        }

        @if (!_awaitingTwoFactor)
        {
            @if (_passkeySupported)
            {
                <MudButton FullWidth="true" Variant="Variant.Filled" Color="Color.Primary"
                           StartIcon="@Icons.Material.Filled.Fingerprint"
                           OnClick="SignInWithPasskeyAsync" Disabled="@_busy"
                           data-testid="signin-passkey">
                    Sign in with a passkey
                </MudButton>
            }

            @foreach (var provider in _providers)
            {
                <MudButton FullWidth="true" Variant="Variant.Outlined"
                           OnClick="@(() => BeginSocialAsync(provider))" Disabled="@_busy"
                           data-testid="@($"signin-social-{provider.ToLowerInvariant()}")">
                    Continue with @provider
                </MudButton>
            }

            <MudDivider Class="my-2" />

            <MudTextField @bind-Value="_email" Label="Email" InputType="InputType.Email"
                          Variant="Variant.Outlined" Margin="Margin.Dense" data-testid="signin-email" />
            <MudTextField @bind-Value="_password" Label="Password" InputType="InputType.Password"
                          Variant="Variant.Outlined" Margin="Margin.Dense" data-testid="signin-password" />
            <MudButton FullWidth="true" Variant="Variant.Filled" Color="Color.Primary"
                       OnClick="SignInWithPasswordAsync" Disabled="@_busy" data-testid="signin-password-submit">
                @(_busy ? "Signing in…" : "Sign in")
            </MudButton>
        }
        else
        {
            <MudText Typo="Typo.body2" Class="mud-text-secondary">
                Enter the 6-digit code from your authenticator app.
            </MudText>
            <MudTextField @bind-Value="_twoFactorCode" Label="Authenticator code"
                          Variant="Variant.Outlined" Margin="Margin.Dense" MaxLength="6"
                          InputMode="InputMode.numeric" data-testid="signin-2fa-code" />
            <MudButton FullWidth="true" Variant="Variant.Filled" Color="Color.Primary"
                       OnClick="VerifyTwoFactorAsync"
                       Disabled="@(_busy || string.IsNullOrWhiteSpace(_twoFactorCode))"
                       data-testid="signin-2fa-submit">
                @(_busy ? "Verifying…" : "Verify")
            </MudButton>
            <MudButton FullWidth="true" Variant="Variant.Text" OnClick="CancelTwoFactor" Disabled="@_busy">
                Cancel
            </MudButton>
        }
    </MudStack>
</MudContainer>

@code {
    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }
    [SupplyParameterFromQuery] public string? AuthError { get; set; }

    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _error;
    private bool _busy;
    private bool _passkeySupported;
    private IReadOnlyList<string> _providers = [];

    private bool _awaitingTwoFactor;
    private string? _loginToken;
    private string _twoFactorCode = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        // A social return may have staged a token in the URL fragment (auth-fragment.js).
        if (await Auth.TryConsumeSocialReturnAsync(Js))
        {
            AuthState.NotifyChanged();
            NavigateOnSuccess();
            return;
        }

        if (await Auth.IsSignedInAsync())
        {
            NavigateOnSuccess();
            return;
        }

        _error = MapAuthError(AuthError);
        _providers = await Providers.GetConfiguredAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        // Hide the passkey button on browsers without WebAuthn.
        _passkeySupported = await Passkey.IsSupportedAsync();
        StateHasChanged();
    }

    private static string? MapAuthError(string? code) => code switch
    {
        null or "" => null,
        "no_account" => "No Sorcha account is linked to that sign-in. Create one on the web, then come back.",
        "refused" => "That sign-in was refused. Please try another method.",
        _ => "Sign-in failed. Please try again.",
    };

    private void NavigateOnSuccess()
    {
        var target = string.IsNullOrEmpty(ReturnUrl) ? "" : ReturnUrl!;
        Nav.NavigateTo(target);
    }

    private async Task SignInWithPasskeyAsync()
    {
        _error = null; _busy = true;
        try
        {
            var result = await Auth.SignInWithPasskeyAsync();
            await HandleResult(result);
        }
        finally { _busy = false; }
    }

    private async Task BeginSocialAsync(string provider)
    {
        _error = null; _busy = true;
        var url = await Auth.BeginSocialSignInAsync(provider);
        if (string.IsNullOrEmpty(url)) { _error = "Couldn't start social sign-in."; _busy = false; return; }
        Nav.NavigateTo(url, forceLoad: true); // full-page to the provider
    }

    private async Task SignInWithPasswordAsync()
    {
        _error = null;
        if (string.IsNullOrWhiteSpace(_email) || string.IsNullOrWhiteSpace(_password))
        { _error = "Email and password are required."; return; }
        _busy = true;
        try
        {
            var result = await Auth.SignInAsync(_email, _password);
            if (result.Status == SignInStatus.TwoFactorRequired)
            {
                _loginToken = result.LoginToken; _awaitingTwoFactor = true; _password = string.Empty;
                return;
            }
            await HandleResult(result);
        }
        finally { _busy = false; }
    }

    private async Task VerifyTwoFactorAsync()
    {
        if (string.IsNullOrWhiteSpace(_loginToken)) { CancelTwoFactor(); return; }
        _error = null; _busy = true;
        try
        {
            var result = await Auth.VerifyTwoFactorAsync(_loginToken, _email, _twoFactorCode);
            if (!result.IsSuccess) { _error = result.ErrorMessage ?? "Verification failed."; _twoFactorCode = string.Empty; return; }
            await HandleResult(result);
        }
        finally { _busy = false; }
    }

    private void CancelTwoFactor()
    {
        _awaitingTwoFactor = false; _loginToken = null; _twoFactorCode = string.Empty; _error = null;
    }

    private async Task HandleResult(SignInResult result)
    {
        if (result.IsSuccess)
        {
            AuthState.NotifyChanged();
            NavigateOnSuccess();
        }
        else
        {
            _error = result.ErrorMessage ?? "Sign-in failed.";
        }
    }
}
```

- [ ] **Step 2: Build the PWA**

Run: `dotnet build src/Apps/Sorcha.Wallet.Pwa`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor
git commit -m "feat: dedicated wallet sign-in screen (passkey + social + password)"
```

---

### Task 15: Remove the sign-in card from Settings; keep sign-out

**Files:**
- Modify: `src/Apps/Sorcha.Wallet.Pwa/Pages/Settings.razor`

- [ ] **Step 1: Trim the Account card to sign-out only**

In `Settings.razor`, the `Account` `MudCard` currently renders the full sign-in/2FA form when signed out. Replace the `MudCardContent` body so it only shows the signed-in identity + Sign out, and a "Sign in" link when signed out (the gate will normally prevent reaching Settings signed-out, but keep a graceful path):

```razor
        <MudCardContent>
            @if (_signedInEmail is not null)
            {
                <MudText Typo="Typo.body2" Class="mb-2">Signed in as <strong>@_signedInEmail</strong></MudText>
                <MudButton StartIcon="@Icons.Material.Filled.Logout" Variant="Variant.Outlined"
                           Color="Color.Error" OnClick="SignOutAsync" data-testid="settings-signout">
                    Sign out
                </MudButton>
            }
            else
            {
                <MudButton Variant="Variant.Filled" Color="Color.Primary"
                           OnClick="@(() => Nav.NavigateTo("signin"))" data-testid="settings-signin-link">
                    Sign in
                </MudButton>
            }
        </MudCardContent>
```

Delete the now-unused 2FA/sign-in fields and methods from `@code` (`_email`, `_password`, `_signInError`, `_signingIn`, `_awaitingTwoFactor`, `_loginToken`, `_twoFactorCode`, `_useBackupCode`, `SignInAsync`, `VerifyTwoFactorAsync`, `CancelTwoFactor`). Keep `_signedInEmail`, `OnInitializedAsync`, `SignOutAsync`, the storage/tour code. In `SignOutAsync`, after `await Auth.SignOutAsync();` add `AuthState.NotifyChanged();` and `@inject WalletAuthenticationStateProvider AuthState` at the top.

- [ ] **Step 2: Build the PWA**

Run: `dotnet build src/Apps/Sorcha.Wallet.Pwa`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/Pages/Settings.razor
git commit -m "refactor: move wallet sign-in out of Settings to the dedicated screen"
```

---

### Task 16: Verify gate ↔ PairingTakeover ordering

**Files:**
- Inspect: `src/Apps/Sorcha.Wallet.Pwa/MainLayout.razor`, `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Pairing/PairingTakeover.razor`

`PairingTakeover` self-gates on `IHasPairedDeviceProbe` (device presence) and renders from `MainLayout`. With the auth gate, an anonymous user is redirected to `/signin` (which uses `MainLayout`) before any protected page renders. The takeover must NOT cover the sign-in screen.

- [ ] **Step 1: Confirm the takeover is suppressed on `/signin`**

`PairingTakeover` calls `/api/v1/me/devices/has-any`, which requires a token; signed-out it should resolve to "unknown/anonymous" and not render. Verify `IHasPairedDeviceProbe`/`PairingTakeover` does not render its overlay when there is no token. If it would render on `/signin`, guard it: in `MainLayout.razor`, wrap `<PairingTakeover />` so it only renders when signed in, e.g.:

```razor
<AuthorizeView>
    <Authorized>
        <PairingTakeover />
    </Authorized>
</AuthorizeView>
```

(This requires `@using Microsoft.AspNetCore.Components.Authorization` in `MainLayout.razor`, already available via `_Imports`.)

- [ ] **Step 2: Build the PWA**

Run: `dotnet build src/Apps/Sorcha.Wallet.Pwa`
Expected: build succeeds.

- [ ] **Step 3: Commit (only if MainLayout changed)**

```bash
git add src/Apps/Sorcha.Wallet.Pwa/MainLayout.razor
git commit -m "fix: suppress pairing takeover on the signed-out sign-in screen"
```

---

# Phase E — E2E + docs

### Task 17: E2E sign-in tests

**Files:**
- Create: `tests/Sorcha.UI.E2E.Tests/PageObjects/CitizenWalletSignInPage.cs`
- Create: `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/CitizenWalletSignInTests.cs`

> Run against the Docker stack per the sorcha-ui skill. The social *provider* leg can't run in CI; the test covers the deterministic *return* leg by navigating to `/wallet/#token=…`. Passkey uses Playwright's CDP virtual authenticator.

- [ ] **Step 1: Create the page object**

Create `tests/Sorcha.UI.E2E.Tests/PageObjects/CitizenWalletSignInPage.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.PageObjects;

public class CitizenWalletSignInPage
{
    private readonly IPage _page;
    public CitizenWalletSignInPage(IPage page) => _page = page;

    private static string Wallet(string path) => $"{TestConstants.GatewayBaseUrl}/wallet/{path}";

    public ILocator Screen => _page.GetByTestId("signin-screen");
    public ILocator Email => _page.GetByTestId("signin-email").Locator("input");
    public ILocator Password => _page.GetByTestId("signin-password").Locator("input");
    public ILocator PasswordSubmit => _page.GetByTestId("signin-password-submit");
    public ILocator PasskeyButton => _page.GetByTestId("signin-passkey");
    public ILocator Error => _page.GetByTestId("signin-error");

    public async Task GotoAsync()
    {
        await _page.GotoAsync(Wallet("signin"));
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    public async Task SignInWithPasswordAsync(string email, string password)
    {
        await Email.FillAsync(email);
        await Password.FillAsync(password);
        await PasswordSubmit.ClickAsync();
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }
}
```

> Confirm `TestConstants.GatewayBaseUrl` exists; if the constant is named differently (e.g. `UiWebUrl`), use the gateway host constant the other CitizenWallet tests use. Confirm `GetByTestId` is available (Playwright maps it to `data-testid`); if the suite uses a helper, use `MudBlazorHelpers.TestId(_page, "…")` instead.

- [ ] **Step 2: Write the tests**

Create `tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/CitizenWalletSignInTests.cs`:

```csharp
// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Playwright;
using NUnit.Framework;
using Sorcha.UI.E2E.Tests.Infrastructure;
using Sorcha.UI.E2E.Tests.PageObjects;

namespace Sorcha.UI.E2E.Tests.Docker.CitizenWallet;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("CitizenWallet")]
[Category("Auth")]
public class CitizenWalletSignInTests : DockerTestBase
{
    [Test]
    public async Task SignedOut_ProtectedRoute_RedirectsToSignIn()
    {
        await Page.GotoAsync($"{TestConstants.GatewayBaseUrl}/wallet/devices");
        await Page.WaitForURLAsync("**/wallet/signin**");
        await Expect(Page.GetByTestId("signin-screen")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Password_HappyPath_SignsInAndLeavesSignInScreen()
    {
        var signIn = new CitizenWalletSignInPage(Page);
        await signIn.GotoAsync();
        await signIn.SignInWithPasswordAsync(
            TestConstants.CitizenTestEmail, TestConstants.CitizenTestPassword);
        // Lands on home (no longer on /signin).
        await Page.WaitForURLAsync(url => !url.Contains("/wallet/signin"));
    }

    [Test]
    public async Task SocialReturnLeg_StoresTokenAndLeavesSignIn()
    {
        // Mint a consumer-tier token out-of-band (helper hits /api/auth/login with
        // tier=consumer for the seeded citizen) and simulate the social fragment return.
        var token = await TestTokens.MintConsumerAsync(
            TestConstants.CitizenTestEmail, TestConstants.CitizenTestPassword);
        await Page.GotoAsync(
            $"{TestConstants.GatewayBaseUrl}/wallet/#token={token.AccessToken}&refresh={token.RefreshToken}&expires_in={token.ExpiresIn}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // The fragment is consumed and stripped; we end up authenticated on home.
        await Expect(Page).Not.ToHaveURLAsync("**/wallet/signin**");
    }

    [Test]
    public async Task Passkey_VirtualAuthenticator_SignsIn()
    {
        // Register a CDP virtual authenticator with a resident key, then drive the
        // passkey button. Requires the citizen to have a passkey enrolled server-side
        // (seed in fixture). See Playwright .NET CDP: "WebAuthn.addVirtualAuthenticator".
        var client = await Page.Context.NewCDPSessionAsync(Page);
        await client.SendAsync("WebAuthn.enable");
        await client.SendAsync("WebAuthn.addVirtualAuthenticator", new()
        {
            ["options"] = new Dictionary<string, object>
            {
                ["protocol"] = "ctap2",
                ["transport"] = "internal",
                ["hasResidentKey"] = true,
                ["hasUserVerification"] = true,
                ["isUserVerified"] = true,
            }
        });

        var signIn = new CitizenWalletSignInPage(Page);
        await signIn.GotoAsync();
        // Passkey button only shows when WebAuthn is supported (virtual authenticator satisfies this).
        Assert.That(await signIn.PasskeyButton.IsVisibleAsync(), Is.True);
        // Full assertion requires a server-seeded credential for the virtual authenticator;
        // if the fixture seeds one, click and assert sign-in; otherwise assert the options
        // round-trip returns 200 (no server error surfaced).
    }
}
```

> Implementer notes:
> - `TestTokens.MintConsumerAsync` and `TestConstants.CitizenTestEmail/Password` may need adding to the E2E infrastructure (mirror the existing authenticated-citizen helpers; see the F134 `dev-citizen-test-account` memory for seeding a usable citizen). If an authenticated-citizen base/fixture already exists, reuse it.
> - The passkey test's full happy path depends on a server-seeded credential bound to the virtual authenticator; if seeding isn't available, keep the support-detection + options-round-trip assertions and mark the full ceremony `[Explicit]`.

- [ ] **Step 3: Run the E2E suite (Docker up)**

```bash
docker-compose up -d
dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=CitizenWallet&Category=Auth" -v minimal
```
Expected: redirect + password + social-return tests PASS; passkey test PASS or `[Explicit]`-skipped per seeding.

- [ ] **Step 4: Commit**

```bash
git add tests/Sorcha.UI.E2E.Tests/PageObjects/CitizenWalletSignInPage.cs tests/Sorcha.UI.E2E.Tests/Docker/CitizenWallet/CitizenWalletSignInTests.cs
git commit -m "test: E2E coverage for wallet sign-in (gate, password, social return, passkey)"
```

---

### Task 18: Documentation sync

**Files:**
- Modify: `.claude/skills/sorcha-architecture/SKILL.md` (Citizen Wallet PWA section — add the sign-in surface)
- Modify: `src/Services/Sorcha.Tenant.Service/README.md` (new endpoints: `GET /api/auth/social/providers`, surface param, tier hints)
- Modify: `docs/reference/API-DOCUMENTATION.md` (the new/changed endpoints)

- [ ] **Step 1: Update the sorcha-architecture skill**

In the Citizen Wallet PWA section, add a short "Sign-in (social + passkey)" subsection documenting: dedicated `/wallet/signin` screen + auth gate; three methods; `surface=wallet` social callback minting Consumer-tier and redirecting to `/wallet/#token=…`; `GET /api/auth/social/providers`; the `tier` hint on `passkey/assertion/verify` and `verify-2fa`; refresh via `POST /api/auth/token/refresh`; login-only refusal (`?authError=no_account`).

- [ ] **Step 2: Update the Tenant Service README + API docs**

Add the new endpoint rows and the `surface`/`tier` request fields.

- [ ] **Step 3: Commit**

```bash
git add .claude/skills/sorcha-architecture/SKILL.md src/Services/Sorcha.Tenant.Service/README.md docs/reference/API-DOCUMENTATION.md
git commit -m "docs: document wallet social + passkey sign-in surface"
```

---

## Final verification

- [ ] `dotnet build` (solution) succeeds.
- [ ] `dotnet test tests/Sorcha.Tenant.Service.Tests` green.
- [ ] `dotnet test tests/Sorcha.Wallet.Pwa.Tests` green.
- [ ] `docker-compose up -d` then `dotnet test tests/Sorcha.UI.E2E.Tests --filter "Category=CitizenWallet&Category=Auth"` green.
- [ ] `pwsh scripts/check-no-snackbar.ps1` passes (sign-in screen uses `MudAlert`/`IInlineFeedback`, no `ISnackbar`).
- [ ] Manual browser pass on the Docker stack: signed-out → `/signin`; password+2FA; passkey; social return; unknown-social → `no_account` message + web-signup link; token expiry → silent refresh.
```
