// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<CustomAuthenticationStateProvider> _logger;
    private readonly JwtSecurityTokenHandler _jwtHandler = new() { MapInboundClaims = false };

    // Caches the in-flight or completed auth state task to prevent concurrent callers
    // from racing to consume the one-time fragment token
    private Task<AuthenticationState>? _authStateTask;

    // Guards against raising AuthenticationStateChanged more than once per fresh consume
    private bool _alreadyBroadcast;

    public CustomAuthenticationStateProvider(
        ITokenCache tokenCache,
        IConfigurationService configurationService,
        IJSRuntime jsRuntime,
        ILogger<CustomAuthenticationStateProvider> logger)
    {
        _tokenCache = tokenCache;
        _configurationService = configurationService;
        _jsRuntime = jsRuntime;
        _logger = logger;
    }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        return _authStateTask ??= GetAuthenticationStateCoreAsync();
    }

    /// <summary>
    /// Resolves the current authentication state. Checks for a fresh fragment token first;
    /// if one is consumed, re-broadcasts the authenticated state to already-rendered subscribers
    /// exactly once so they update without a manual reload.
    /// </summary>
    private async Task<AuthenticationState> GetAuthenticationStateCoreAsync()
    {
        try
        {
            var activeProfileName = await _configurationService.GetActiveProfileNameAsync();

            // Always check for a pending fragment token first — it represents a fresh
            // login/org-switch and must take precedence over any cached token. Without
            // this, re-login with a different org would be ignored because the old
            // (still-valid) token would be returned from cache.
            var fragmentEntry = await TryConsumeFragmentTokenAsync(activeProfileName);
            var freshConsume = fragmentEntry != null && !fragmentEntry.IsExpired;

            TokenCacheEntry? entry;
            if (freshConsume)
            {
                entry = fragmentEntry;
            }
            else
            {
                entry = await _tokenCache.GetTokenAsync(activeProfileName);
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
            var state = new AuthenticationState(user);

            if (freshConsume && state.User.Identity?.IsAuthenticated == true && !_alreadyBroadcast)
            {
                _alreadyBroadcast = true;
                _logger.LogDebug("Auth state re-broadcast after fresh fragment-token consume");
                // Broadcast the already-resolved state directly — avoids re-entering
                // GetAuthenticationStateCoreAsync and causing infinite recursion.
                base.NotifyAuthenticationStateChanged(Task.FromResult(state));
            }

            return state;
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
        _alreadyBroadcast = false;
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

            var result = JsonSerializer.Deserialize<FragmentTokenResult>(json, CaseInsensitiveJson);
            if (result?.Token is null)
                return null;

            var jwt = _jwtHandler.ReadJwtToken(result.Token);
            if (jwt.ValidTo <= DateTime.UtcNow)
                return null;

            var entry = new TokenCacheEntry
            {
                AccessToken = result.Token,
                RefreshToken = result.Refresh,
                ExpiresAt = jwt.ValidTo,
                ProfileName = profileName,
                IssuedAt = jwt.IssuedAt
            };

            // Store token BEFORE clearing staging — if store fails, token is preserved
            // for FragmentTokenHandler fallback
            await _tokenCache.StoreTokenAsync(profileName, entry);

            // Clear token staging only after successful store
            await _jsRuntime.InvokeVoidAsync("sorcha.fragmentHandoff.clearTokenStaging");

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
