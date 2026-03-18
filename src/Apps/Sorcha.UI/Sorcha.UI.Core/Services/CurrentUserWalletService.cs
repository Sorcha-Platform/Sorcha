// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Components.Authorization;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Provides the current authenticated user's wallet address.
/// </summary>
public interface ICurrentUserWalletService
{
    /// <summary>
    /// Gets the wallet address from the current user's claims.
    /// Returns null if the user is not authenticated or has no linked wallet.
    /// </summary>
    Task<string?> GetWalletAddressAsync();
}

/// <summary>
/// Retrieves the current user's wallet address from the authentication state.
/// Caches the result for the lifetime of the scoped instance (wallet doesn't change during session).
/// </summary>
public class CurrentUserWalletService : ICurrentUserWalletService
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private string? _cachedWalletAddress;
    private bool _hasCached;

    /// <summary>
    /// Initializes a new instance of <see cref="CurrentUserWalletService"/>.
    /// </summary>
    public CurrentUserWalletService(AuthenticationStateProvider authStateProvider)
    {
        _authStateProvider = authStateProvider;
    }

    /// <inheritdoc />
    public async Task<string?> GetWalletAddressAsync()
    {
        if (_hasCached)
        {
            return _cachedWalletAddress;
        }

        var authState = await _authStateProvider.GetAuthenticationStateAsync();
        var user = authState.User;

        if (user.Identity?.IsAuthenticated != true)
        {
            _hasCached = true;
            _cachedWalletAddress = null;
            return null;
        }

        _cachedWalletAddress = user.FindFirst("wallet_address")?.Value;
        _hasCached = true;
        return _cachedWalletAddress;
    }
}
