// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Tests for <see cref="InMemoryWalletFlagsStore"/> (Feature 124, T017). The
/// IndexedDB variant is exercised end-to-end by Playwright in the polish phase;
/// here we verify the round-trip contract.
/// </summary>
public sealed class WalletFlagsStoreTests
{
    [Fact]
    public async Task GetAsync_NeverSet_ReturnsNull()
    {
        var store = new InMemoryWalletFlagsStore();
        var record = await store.GetAsync();
        record.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsRecord()
    {
        var store = new InMemoryWalletFlagsStore();
        var welcomedAt = DateTimeOffset.UtcNow;

        await store.SetAsync(new WalletFlagsRecord(welcomedAt));

        var read = await store.GetAsync();
        read.Should().NotBeNull();
        read!.WelcomedAt.Should().Be(welcomedAt);
    }

    [Fact]
    public async Task SetAsync_Replace_OverwritesPriorValue()
    {
        var store = new InMemoryWalletFlagsStore();
        var first = DateTimeOffset.UtcNow.AddDays(-1);
        var second = DateTimeOffset.UtcNow;

        await store.SetAsync(new WalletFlagsRecord(first));
        await store.SetAsync(new WalletFlagsRecord(second));

        var read = await store.GetAsync();
        read!.WelcomedAt.Should().Be(second);
    }

    [Fact]
    public async Task SetAsync_NullWelcomedAt_IsPersisted()
    {
        // Edge case: the store accepts a record with null WelcomedAt — this
        // is the value used to materialise the default record on lazy reads.
        var store = new InMemoryWalletFlagsStore();
        await store.SetAsync(new WalletFlagsRecord(WelcomedAt: null));

        var read = await store.GetAsync();
        read.Should().NotBeNull();
        read!.WelcomedAt.Should().BeNull();
    }
}
