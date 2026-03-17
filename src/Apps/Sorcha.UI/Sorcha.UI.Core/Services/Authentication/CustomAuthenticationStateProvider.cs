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
    private Task<AuthenticationState>? _pendingAuthTask;

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
        // Deduplicate concurrent calls to prevent multiple callers from racing
        // to consume the one-time fragment token
        return _pendingAuthTask ??= GetAuthenticationStateCoreAsync();
    }

    private async Task<AuthenticationState> GetAuthenticationStateCoreAsync()
    {
        try
        {
            var activeProfileName = await _configurationService.GetActiveProfileNameAsync();
            var entry = await _tokenCache.GetTokenAsync(activeProfileName);

            if (entry == null || entry.IsExpired)
            {
                // Check if there's a pending fragment token from the login redirect.
                // The fragment-handoff.js eagerly extracts the token on load (before Blazor boots),
                // so we can consume it here before the AuthorizeRouteView evaluates auth state.
                entry = await TryConsumeFragmentTokenAsync(activeProfileName);
            }

            if (entry == null || entry.IsExpired)
            {
                return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
            }

            var jwtToken = _jwtHandler.ReadJwtToken(entry.AccessToken);
            var claims = jwtToken.Claims.ToList();

            // Ensure we have name and role claims
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
            // Resilience: invalid or corrupt JWT returns anonymous identity rather than crashing
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }

    /// <summary>
    /// Notifies the authentication state has changed (e.g., after login/logout)
    /// </summary>
    public void NotifyAuthenticationStateChanged()
    {
        _pendingAuthTask = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>
    /// Checks for a pending fragment token from login redirect and caches it if found.
    /// This prevents the race condition where AuthorizeRouteView fires before
    /// FragmentTokenHandler.OnAfterRenderAsync can extract the token.
    /// </summary>
    private async Task<TokenCacheEntry?> TryConsumeFragmentTokenAsync(string profileName)
    {
        try
        {
            // Check localStorage for a staged fragment token.
            // The fragment-handoff.js IIFE stores the token here on page load (before Blazor boots)
            // using the same localStorage.getItem pattern that BrowserTokenCache already uses.
            var json = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "sorcha:fragment-pending");

            // Fallback: check window global (localStorage may not be readable from WASM context)
            if (string.IsNullOrEmpty(json))
            {
                json = await _jsRuntime.InvokeAsync<string?>("eval", "window.__sorcha_fragment_token || null");
            }

            if (string.IsNullOrEmpty(json))
                return null;

            // Clear all staging locations (localStorage, window global, and JS closure)
            // so FragmentTokenHandler.OnAfterRenderAsync doesn't double-process the token
            await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "sorcha:fragment-pending");
            await _jsRuntime.InvokeVoidAsync("eval", "window.__sorcha_fragment_token = null");
            await _jsRuntime.InvokeAsync<object?>("sorcha.fragmentHandoff.getAndClear");

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
            // JS interop may not be available in certain contexts; fall through gracefully
            return null;
        }
    }

    private sealed class FragmentTokenResult
    {
        public string? Token { get; set; }
        public string? Refresh { get; set; }
    }
}
