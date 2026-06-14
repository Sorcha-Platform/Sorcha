// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Feature 153 (D) — the home/personal token slot on <see cref="IAccessTokenStore"/>: snapshot the
/// consumer token when leaving Personal so it can be restored on return (separate from the active
/// token slot).
/// </summary>
public sealed class HomeTokenStoreTests
{
    private static AccessTokenRecord Token(string jwt, DateTimeOffset expiresAt) =>
        new(jwt, expiresAt, Email: null);

    [Fact]
    public async Task SetHome_ThenGetHome_RoundTrips()
    {
        var store = new InMemoryAccessTokenStore();
        var home = Token("home.jwt", DateTimeOffset.UtcNow.AddHours(1));

        await store.SetHomeAsync(home);

        (await store.GetHomeAsync())!.AccessToken.Should().Be("home.jwt");
    }

    [Fact]
    public async Task HomeSlot_IsSeparateFromActiveSlot()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(Token("active.jwt", DateTimeOffset.UtcNow.AddHours(1)));
        await store.SetHomeAsync(Token("home.jwt", DateTimeOffset.UtcNow.AddHours(1)));

        (await store.GetAsync())!.AccessToken.Should().Be("active.jwt");
        (await store.GetHomeAsync())!.AccessToken.Should().Be("home.jwt");
    }

    [Fact]
    public async Task ClearHome_RemovesHome_ButLeavesActive()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetAsync(Token("active.jwt", DateTimeOffset.UtcNow.AddHours(1)));
        await store.SetHomeAsync(Token("home.jwt", DateTimeOffset.UtcNow.AddHours(1)));

        await store.ClearHomeAsync();

        (await store.GetHomeAsync()).Should().BeNull();
        (await store.GetAsync()).Should().NotBeNull();
    }

    [Fact]
    public async Task GetHome_Expired_ReturnsNull()
    {
        var store = new InMemoryAccessTokenStore();
        await store.SetHomeAsync(Token("home.jwt", DateTimeOffset.UtcNow.AddSeconds(-1)));

        (await store.GetHomeAsync()).Should().BeNull("an expired home token must not be restored");
    }
}
