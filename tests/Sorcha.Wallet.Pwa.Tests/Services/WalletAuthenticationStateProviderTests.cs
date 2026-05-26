// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
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

    [Fact]
    public async Task NotifyChanged_RaisesAuthenticationStateChanged_WithAuthenticatedState()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("at", DateTimeOffset.UtcNow.AddHours(1), "a@b.test"));
        var provider = new WalletAuthenticationStateProvider(store);

        Task<AuthenticationState>? raised = null;
        provider.AuthenticationStateChanged += t => raised = t;

        provider.NotifyChanged();

        raised.Should().NotBeNull();
        var state = await raised!;
        state.User.Identity!.IsAuthenticated.Should().BeTrue();
    }

    [Fact]
    public async Task GetAuthenticationState_ExpiredToken_IsAnonymous()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(new AccessTokenRecord("at", DateTimeOffset.UtcNow.AddMinutes(-1), "a@b.test"));
        var provider = new WalletAuthenticationStateProvider(store);

        var state = await provider.GetAuthenticationStateAsync();

        state.User.Identity!.IsAuthenticated.Should().BeFalse();
    }
}
