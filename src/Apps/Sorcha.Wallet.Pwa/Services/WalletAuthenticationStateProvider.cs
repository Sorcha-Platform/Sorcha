// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>
/// Auth state for the wallet gate. Authenticated iff <see cref="IAccessTokenStore"/>
/// holds a token that is both present and not yet expired. A missing token, an empty
/// access-token string, or a token whose <c>ExpiresAt</c> is in the past are all
/// treated as anonymous — the provider does not rely solely on the store self-purging
/// expired records. No JWT parsing is needed — the protected pages only require
/// presence, not claims. Email is surfaced as the Name claim for display. Call
/// <see cref="NotifyChanged"/> after sign-in / sign-out so the gate re-evaluates.
/// </summary>
public sealed class WalletAuthenticationStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly IAccessTokenStore _store;

    /// <summary>Initialises a new instance backed by <paramref name="store"/>.</summary>
    public WalletAuthenticationStateProvider(IAccessTokenStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var record = await _store.GetAsync();
        if (record is null || string.IsNullOrEmpty(record.AccessToken) || record.ExpiresAt <= DateTimeOffset.UtcNow)
            return Anonymous;

        var claims = new List<Claim> { new(ClaimTypes.Name, record.Email ?? "citizen") };
        var identity = new ClaimsIdentity(claims, authenticationType: "wallet-jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    /// <summary>Re-evaluate auth state (after sign-in or sign-out).</summary>
    public void NotifyChanged()
        => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}
