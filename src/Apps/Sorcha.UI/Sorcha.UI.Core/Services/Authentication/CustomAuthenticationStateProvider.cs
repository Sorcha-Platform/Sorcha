// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services.Authentication;

/// <summary>
/// Blazor authentication state provider that uses JWT tokens from ITokenCache
/// </summary>
public class CustomAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly ITokenCache _tokenCache;
    private readonly IConfigurationService _configurationService;
    private readonly JwtSecurityTokenHandler _jwtHandler = new();

    public CustomAuthenticationStateProvider(
        ITokenCache tokenCache,
        IConfigurationService configurationService)
    {
        _tokenCache = tokenCache;
        _configurationService = configurationService;
    }

    /// <inheritdoc />
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        try
        {
            var activeProfileName = await _configurationService.GetActiveProfileNameAsync();
            var entry = await _tokenCache.GetTokenAsync(activeProfileName);

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
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }
}
