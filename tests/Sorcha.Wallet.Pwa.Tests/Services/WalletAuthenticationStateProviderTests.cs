// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Security.Claims;
using System.Threading.Tasks;
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
