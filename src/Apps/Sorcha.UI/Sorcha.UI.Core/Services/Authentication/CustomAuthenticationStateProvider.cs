// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Sorcha.UI.Core.Models.Authentication;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Blazor authentication state provider that uses JWT tokens from ITokenCache.
/// Also handles eager fragment token extraction to prevent the "token bounce"
/// race condition during login redirects.
/// </summary>
public class CustomAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly JsonSerializerOptions CaseInsensitiveJson = new() { PropertyNameCaseInsensitive = true };

    private readonly ITokenCache _tokenCache;
    private readonly IConfigurationService _configurationService;
    private readonly IJSRuntime _jsRuntime;
    private readonly JwtSecurityTokenHandler _jwtHandler = new() { MapInboundClaims = false };

    // Caches the in-flight or completed auth state task to prevent concurrent callers
    // from racing to consume the one-time fragment token
    private Task<AuthenticationState>? _authStateTask;

    public CustomAuthenticationStateProvider(
        ITokenCache tokenCache,
        IConfigurationService configurationService,
        IJSRuntime jsRuntime)
    {
        _tokenCache = tokenCache;
        _configurationService = configurationService;
        _jsRuntime = jsRuntime;
    }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return _authStateTask ??= GetAuthenticationStateCoreAsync();
    }

    private async Task<AuthenticationState> GetAuthenticationStateCoreAsync()
    {
        try
        {
            var activeProfileName = await _configurationService.GetActiveProfileNameAsync();
            var entry = await _tokenCache.GetTokenAsync(activeProfileName);

            if (entry == null || entry.IsExpired)
            {
                entry = await TryConsumeFragmentTokenAsync(activeProfileName);
            }

            if (entry == null || entry.IsExpired)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var jwtToken = _jwtHandler.ReadJwtToken(entry.AccessToken);
            var claims = jwtToken.Claims.ToList();

            if (!claims.Any(c => c.Type == ClaimTypes.Name))
            {
                var subClaim = claims.FirstOrDefault(c => c.Type == "sub");
                if (subClaim != null)
                {
                    claims.Add(new Claim(ClaimTypes.Name, subClaim.Value));
                }
            }

            var identity = new ClaimsIdentity(claims, "jwt", ClaimTypes.Name, "role");
            var user = new ClaimsPrincipal(identity);

            return new AuthenticationState(user);
        }
        catch (Exception)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }

    /// <summary>
    /// Notifies the authentication state has changed (e.g., after login/logout)
    /// </summary>
    public void NotifyAuthenticationStateChanged()
    {
        _authStateTask = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>
    /// Checks for a pending fragment token from login redirect and caches it if found.
    /// The fragment-handoff.js IIFE eagerly extracts the token on page load and stages it
    /// in both localStorage and a window global. This method reads from those staging
    /// locations and clears them atomically to prevent double-processing.
    /// </summary>
    private async Task<TokenCacheEntry?> TryConsumeFragmentTokenAsync(string profileName)
    {
        try
        {
            // Try localStorage first (primary staging location)
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "sorcha:fragment-pending");

            // Fallback: window global (localStorage may not be readable across WASM/page contexts)
            if (string.IsNullOrEmpty(json))
            {
                json = await _jsRuntime.InvokeAsync<string?>("sorcha.fragmentHandoff.getWindowToken");
            }

            if (string.IsNullOrEmpty(json))
                return null;

            // Clear all staging locations in a single JS interop call
            await _jsRuntime.InvokeVoidAsync("sorcha.fragmentHandoff.clearAll");

            var result = JsonSerializer.Deserialize<FragmentTokenResult>(json, CaseInsensitiveJson);
            if (result?.Token is null)
                return null;

            var jwt = _jwtHandler.ReadJwtToken(result.Token);
            var expiresIn = (int)(jwt.ValidTo - DateTime.UtcNow).TotalSeconds;
            if (expiresIn <= 0)
                return null;

            var entry = new TokenCacheEntry
            {
                AccessToken = result.Token,
                RefreshToken = result.Refresh,
                ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn),
                ProfileName = profileName,
                IssuedAt = DateTime.UtcNow
            };

            await _tokenCache.StoreTokenAsync(profileName, entry);
            return entry;
        }
        catch
        {
            // JS interop may not be available in certain contexts (e.g. prerendering);
            // fall through gracefully to allow normal auth flow
            return null;
        }
    }
}
