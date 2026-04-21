// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Core.Observations;
using Sorcha.Register.Models.Observations;
using Xunit;

namespace Sorcha.Register.Core.Tests.Observations;

public class ObservationStoreTests
{
    private const string RegisterA = "7c4ebed1dc2b444f87782e58b424e8d3";

    [Fact]
    public void RecordPeerHeight_NewPeer_AddsObservation()
    {
        var store = new ObservationStore();
        var obs = new PeerHeightObservation(RegisterA, "n1.sorcha.dev", 42, DateTimeOffset.UtcNow);

        store.RecordPeerHeight(obs);

        var recent = store.GetRecentPeerHeights(RegisterA, TimeSpan.FromMinutes(1));
        recent.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(obs);
    }

    [Fact]
    public void RecordPeerHeight_SamePeerAgain_UpsertsNotAppends()
    {
        var store = new ObservationStore();
        var now = DateTimeOffset.UtcNow;

        store.RecordPeerHeight(new(RegisterA, "n1.sorcha.dev", 1, now));
        store.RecordPeerHeight(new(RegisterA, "n1.sorcha.dev", 2, now.AddSeconds(1)));
        store.RecordPeerHeight(new(RegisterA, "n1.sorcha.dev", 3, now.AddSeconds(2)));

        var recent = store.GetRecentPeerHeights(RegisterA, TimeSpan.FromMinutes(1));
        recent.Should().ContainSingle()
            .Which.NetworkHeight.Should().Be(3);
    }

    [Fact]
    public void GetRecentPeerHeights_DropsObservationsOutsideStalenessWindow()
    {
        var store = new ObservationStore();
        var now = DateTimeOffset.UtcNow;

        store.RecordPeerHeight(new(RegisterA, "fresh.example", 10, now));
        store.RecordPeerHeight(new(RegisterA, "stale.example", 7, now - TimeSpan.FromMinutes(5)));

        var recent = store.GetRecentPeerHeights(RegisterA, TimeSpan.FromMinutes(1));

        recent.Should().HaveCount(1);
        recent[0].SourcePeerId.Should().Be("fresh.example");
    }

    [Fact]
    public void RecordPeerHeight_OverCap_EvictsOldestDistinctPeer()
    {
        var store = new ObservationStore();
        var baseTime = DateTimeOffset.UtcNow;

        // Fill to the cap with distinct peers, each with an increasing ObservedAt.
        for (var i = 0; i < ObservationStore.MaxDistinctPeersPerRegister; i++)
        {
            store.RecordPeerHeight(new(RegisterA, $"peer-{i}", NetworkHeight: i, baseTime.AddSeconds(i)));
        }

        store.GetRecentPeerHeights(RegisterA, TimeSpan.FromHours(1))
            .Should().HaveCount(ObservationStore.MaxDistinctPeersPerRegister);

        // One more distinct peer should push out the oldest ("peer-0").
        store.RecordPeerHeight(new(RegisterA, "peer-new", 999, baseTime.AddMinutes(5)));

        var after = store.GetRecentPeerHeights(RegisterA, TimeSpan.FromHours(1));
        after.Should().HaveCount(ObservationStore.MaxDistinctPeersPerRegister);
        after.Select(x => x.SourcePeerId).Should().Contain("peer-new");
        after.Select(x => x.SourcePeerId).Should().NotContain("peer-0");
    }

    [Fact]
    public void RecordValidatorSealing_Overwrites()
    {
        var store = new ObservationStore();

        store.RecordValidatorSealing(new(RegisterA, 1, 5, DateTimeOffset.UtcNow));
        store.RecordValidatorSealing(new(RegisterA, 2, 3, DateTimeOffset.UtcNow));

        var latest = store.GetLatestValidatorSealing(RegisterA);
        latest.Should().NotBeNull();
        latest!.LastSealedHeight.Should().Be(2);
        latest.MempoolDepth.Should().Be(3);
    }

    [Fact]
    public void GetTrackedRegisterIds_IncludesBothPeerAndValidatorBuckets()
    {
        var store = new ObservationStore();
        store.RecordPeerHeight(new(RegisterA, "n1", 1, DateTimeOffset.UtcNow));
        store.RecordValidatorSealing(new("other-register", 1, 0, DateTimeOffset.UtcNow));

        store.GetTrackedRegisterIds().Should().BeEquivalentTo(new[] { RegisterA, "other-register" });
    }

    [Fact]
    public void Evict_RemovesBothBuckets()
    {
        var store = new ObservationStore();
        store.RecordPeerHeight(new(RegisterA, "n1", 1, DateTimeOffset.UtcNow));
        store.RecordValidatorSealing(new(RegisterA, 1, 0, DateTimeOffset.UtcNow));

        store.Evict(RegisterA);

        store.GetRecentPeerHeights(RegisterA, TimeSpan.FromMinutes(1)).Should().BeEmpty();
        store.GetLatestValidatorSealing(RegisterA).Should().BeNull();
        store.GetTrackedRegisterIds().Should().NotContain(RegisterA);
    }

    [Fact]
    public async Task RecordPeerHeight_UnderConcurrentWrites_StaysWithinCap()
    {
        var store = new ObservationStore();
        var baseTime = DateTimeOffset.UtcNow;

        var tasks = Enumerable.Range(0, 64).Select(i => Task.Run(() =>
        {
            store.RecordPeerHeight(new(RegisterA, $"peer-{i}", NetworkHeight: i, baseTime.AddMilliseconds(i)));
        })).ToArray();

        await Task.WhenAll(tasks);

        // One final synchronous write forces the eviction path to run after any lingering races.
        store.RecordPeerHeight(new(RegisterA, "peer-final", 999, baseTime.AddMinutes(1)));

        var recent = store.GetRecentPeerHeights(RegisterA, TimeSpan.FromHours(1));
        recent.Should().HaveCountLessThanOrEqualTo(ObservationStore.MaxDistinctPeersPerRegister);
    }
}
