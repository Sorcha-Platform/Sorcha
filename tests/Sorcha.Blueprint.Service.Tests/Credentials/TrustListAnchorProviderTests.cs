// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Sorcha.Blueprint.Service.Credentials;
using Sorcha.ServiceClients.Trust;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Credentials;

/// <summary>
/// Feature 181 US3 (T035/T036) — the trustlist anchor adapter carries the snapshot identity into the
/// anchor set and applies the freshness gate: warn mode vouches with a stale snapshot; strict mode
/// fails closed.
/// </summary>
public sealed class TrustListAnchorProviderTests
{
    private sealed class StubProvider(TrustListSnapshot? snapshot) : ITrustListProvider
    {
        public Task<TrustListSnapshot?> GetSnapshotAsync(string trustListId, CancellationToken cancellationToken = default)
            => Task.FromResult(snapshot);
    }

    private static TrustListSnapshot Snapshot(DateTimeOffset? nextUpdate) => new()
    {
        Id = "eu-lotl",
        SequenceNumber = 7,
        Roots = [[1, 2, 3]],
        NextUpdate = nextUpdate,
        FreshnessState = "Fresh",
        Freshness = DateTimeOffset.UtcNow,
    };

    private static IConfiguration Config(bool strict) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Trust:TrustListStrictFreshness"] = strict ? "true" : "false" })
        .Build();

    [Fact]
    public async Task GetAnchorsAsync_FreshSnapshot_CarriesAnchorSetIdentity()
    {
        var provider = new TrustListAnchorProvider(new StubProvider(Snapshot(DateTimeOffset.UtcNow.AddDays(30))), Config(strict: false));

        var set = await provider.GetAnchorsAsync("eu-lotl");

        set.Should().NotBeNull();
        set!.AnchorSetId.Should().Be("eu-lotl#7");
        set.Roots.Should().ContainSingle();
    }

    [Fact]
    public async Task GetAnchorsAsync_StaleSnapshot_WarnMode_StillVouches()
    {
        var provider = new TrustListAnchorProvider(new StubProvider(Snapshot(DateTimeOffset.UtcNow.AddDays(-1))), Config(strict: false));

        var set = await provider.GetAnchorsAsync("eu-lotl");

        set.Should().NotBeNull("warn mode vouches with a stale-flagged evidence trail");
    }

    [Fact]
    public async Task GetAnchorsAsync_StaleSnapshot_StrictMode_FailsClosed()
    {
        var provider = new TrustListAnchorProvider(new StubProvider(Snapshot(DateTimeOffset.UtcNow.AddDays(-1))), Config(strict: true));

        var set = await provider.GetAnchorsAsync("eu-lotl");

        set.Should().BeNull("strict freshness fails closed on a stale snapshot (TRUSTLIST_STALE)");
    }

    [Fact]
    public async Task GetAnchorsAsync_NoSnapshot_ReturnsNull()
    {
        var provider = new TrustListAnchorProvider(new StubProvider(null), Config(strict: false));

        (await provider.GetAnchorsAsync("eu-lotl")).Should().BeNull();
    }
}
