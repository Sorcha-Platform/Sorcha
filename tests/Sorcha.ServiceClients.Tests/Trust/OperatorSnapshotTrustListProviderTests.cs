// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.ServiceClients.Trust;

namespace Sorcha.ServiceClients.Tests.Trust;

/// <summary>
/// Feature 135 / T039 — the operator-snapshot trust-list provider stores and resolves snapshots by
/// id (live LOTL deferred per clarification A5).
/// </summary>
public class OperatorSnapshotTrustListProviderTests
{
    private static TrustListSnapshot Snapshot(string id) => new()
    {
        Id = id,
        Roots = [[1, 2, 3]],
        Source = "operator-upload",
        CreatedAt = DateTimeOffset.UtcNow,
        Freshness = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GetSnapshot_KnownId_ReturnsSnapshot()
    {
        var provider = new OperatorSnapshotTrustListProvider();
        provider.Upsert(Snapshot("snap-1"));

        var result = await provider.GetSnapshotAsync("snap-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("snap-1");
        result.Source.Should().Be("operator-upload");
    }

    [Fact]
    public async Task GetSnapshot_UnknownId_ReturnsNull()
    {
        var provider = new OperatorSnapshotTrustListProvider();
        (await provider.GetSnapshotAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task Upsert_ReplacesExisting_AndListReflectsState()
    {
        var provider = new OperatorSnapshotTrustListProvider();
        provider.Upsert(Snapshot("snap-1"));
        provider.Upsert(new TrustListSnapshot { Id = "snap-1", Source = "replaced", Roots = [] });

        (await provider.GetSnapshotAsync("snap-1"))!.Source.Should().Be("replaced");
        provider.List().Should().ContainSingle();

        provider.Remove("snap-1").Should().BeTrue();
        provider.List().Should().BeEmpty();
    }
}
