// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Tests for <see cref="InMemoryActiveContextStore"/> (Feature 125, T027).
/// IndexedDB variant is exercised by Playwright; this verifies the
/// in-memory round-trip contract used by unit-level consumers and tests.
/// </summary>
public sealed class ActiveContextStoreTests
{
    [Fact]
    public async Task GetAsync_NeverSet_ReturnsNull_DefaultsToPersonal()
    {
        var store = new InMemoryActiveContextStore();
        var record = await store.GetAsync();
        record.Should().BeNull("a null record means the caller falls back to Personal context.");
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_RoundTripsRecord()
    {
        var store = new InMemoryActiveContextStore();
        var orgId = Guid.NewGuid();
        var switchedAt = DateTimeOffset.UtcNow;

        await store.SetAsync(new ActiveContextRecord(orgId, switchedAt));

        var read = await store.GetAsync();
        read.Should().NotBeNull();
        read!.ContextOrgId.Should().Be(orgId);
        read.SwitchedAt.Should().Be(switchedAt);
    }

    [Fact]
    public async Task SetAsync_NullContextOrgId_RepresentsPersonal()
    {
        var store = new InMemoryActiveContextStore();
        var switchedAt = DateTimeOffset.UtcNow;

        await store.SetAsync(new ActiveContextRecord(null, switchedAt));

        var read = await store.GetAsync();
        read.Should().NotBeNull();
        read!.ContextOrgId.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_Twice_OverwritesPriorRecord()
    {
        var store = new InMemoryActiveContextStore();
        var firstOrg = Guid.NewGuid();
        var secondOrg = Guid.NewGuid();

        await store.SetAsync(new ActiveContextRecord(firstOrg, DateTimeOffset.UtcNow.AddDays(-1)));
        await store.SetAsync(new ActiveContextRecord(secondOrg, DateTimeOffset.UtcNow));

        var read = await store.GetAsync();
        read!.ContextOrgId.Should().Be(secondOrg);
    }

    [Fact]
    public async Task ClearAsync_RemovesRecord_NextReadIsNull()
    {
        var store = new InMemoryActiveContextStore();
        await store.SetAsync(new ActiveContextRecord(Guid.NewGuid(), DateTimeOffset.UtcNow));

        await store.ClearAsync();

        var read = await store.GetAsync();
        read.Should().BeNull("ClearAsync must surface as a null read so callers fall back to Personal.");
    }

    [Fact]
    public async Task SetAsync_Null_Throws()
    {
        var store = new InMemoryActiveContextStore();
        var act = async () => await store.SetAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
