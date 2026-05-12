// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Options;
using Sorcha.Register.Core.SyncState;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.Observations;
using Xunit;

namespace Sorcha.Register.Core.Tests.SyncState;

public class RegisterSyncStateResolverTests
{
    private const string RegisterId = "7c4ebed1dc2b444f87782e58b424e8d3";

    private static RegisterSyncStateResolver BuildResolver(int quorum = 2, int stalenessSeconds = 60)
    {
        return new RegisterSyncStateResolver(Options.Create(new RegisterSyncStateOptions
        {
            CaughtUpQuorum = quorum,
            StalenessWindow = TimeSpan.FromSeconds(stalenessSeconds)
        }));
    }

    [Fact]
    public void NoObservations_ReturnsIndeterminate()
    {
        var view = BuildResolver().Resolve(
            RegisterId, localHeight: 0,
            observations: Array.Empty<PeerHeightObservation>(),
            validatorSealing: null,
            persistedState: null,
            lastErrorMessage: null);

        view.State.Should().Be(RegisterSyncState.Indeterminate);
        view.NetworkHeightHighWaterMark.Should().BeNull();
        view.DistinctPeerObservers.Should().Be(0);
        view.SinglePeerMode.Should().BeFalse();
    }

    [Fact]
    public void LocalBehind_WithOneObservation_ReturnsSyncing()
    {
        var obs = new[] { new PeerHeightObservation(RegisterId, "n1", 10, DateTimeOffset.UtcNow) };
        var view = BuildResolver().Resolve(RegisterId, 5, obs, null, null, null);

        view.State.Should().Be(RegisterSyncState.Syncing);
        view.NetworkHeightHighWaterMark.Should().Be(10);
        view.DistinctPeerObservers.Should().Be(1);
        view.SinglePeerMode.Should().BeTrue();
    }

    [Fact]
    public void LocalEqualsHwm_WithTwoAgreeingPeers_ReturnsCaughtUpQuorumMet()
    {
        var now = DateTimeOffset.UtcNow;
        var obs = new[]
        {
            new PeerHeightObservation(RegisterId, "n1", 10, now),
            new PeerHeightObservation(RegisterId, "n2", 10, now.AddSeconds(-1))
        };
        var view = BuildResolver().Resolve(RegisterId, 10, obs, null, null, null);

        view.State.Should().Be(RegisterSyncState.CaughtUp);
        view.NetworkHeightHighWaterMark.Should().Be(10);
        view.DistinctPeerObservers.Should().Be(2);
        view.SinglePeerMode.Should().BeFalse();
    }

    [Fact]
    public void LocalEqualsHwm_WithOnlyOnePeer_ReturnsCaughtUpSinglePeerMode()
    {
        var obs = new[] { new PeerHeightObservation(RegisterId, "n1", 10, DateTimeOffset.UtcNow) };
        var view = BuildResolver().Resolve(RegisterId, 10, obs, null, null, null);

        view.State.Should().Be(RegisterSyncState.CaughtUp);
        view.SinglePeerMode.Should().BeTrue();
    }

    [Fact]
    public void LocalAheadOfHwm_ReturnsCaughtUp()
    {
        var obs = new[] { new PeerHeightObservation(RegisterId, "n1", 10, DateTimeOffset.UtcNow) };
        var view = BuildResolver().Resolve(RegisterId, 15, obs, null, null, null);

        view.State.Should().Be(RegisterSyncState.CaughtUp);
    }

    [Fact]
    public void PrunedOwner_ReceivesStaleAdvertFromTrailingPeer_ReturnsCaughtUp()
    {
        // Scenario from the Feature 108 follow-up audit: a register owner has pruned
        // dockets locally (LocalHeight advanced past a docket that has since been
        // archived/replicated) and then receives a peer advert from a node that's
        // genuinely trailing. The advert is "old" in the sense that its NetworkHeight
        // is lower than the owner's LocalHeight. The owner must NOT regress to
        // Syncing/Indeterminate on the strength of a trailing peer's claim — the
        // resolver should report CaughtUp because local >= hwm.
        //
        // Constructed independently of LocalAheadOfHwm_ReturnsCaughtUp so a future
        // refactor that mistakenly clamps LocalHeight against hwm gets caught here
        // even if that test changes.
        var trailingPeerAdvert = new[]
        {
            new PeerHeightObservation(RegisterId, "trailing-peer", 100, DateTimeOffset.UtcNow.AddSeconds(-30))
        };
        var view = BuildResolver().Resolve(
            RegisterId,
            localHeight: 250,  // owner has pruned past height 100, retains tip 250
            observations: trailingPeerAdvert,
            validatorSealing: null,
            persistedState: null,
            lastErrorMessage: null);

        view.State.Should().Be(RegisterSyncState.CaughtUp);
        view.LocalHeight.Should().Be(250);
        view.NetworkHeightHighWaterMark.Should().Be(100);
        view.DistinctPeerObservers.Should().Be(1);
    }

    [Fact]
    public void PersistedError_IsSticky_EvenWhenObservationsFresh()
    {
        var obs = new[] { new PeerHeightObservation(RegisterId, "n1", 10, DateTimeOffset.UtcNow) };
        var view = BuildResolver().Resolve(
            RegisterId, 10, obs, null,
            persistedState: RegisterSyncState.Error,
            lastErrorMessage: "pull pipeline stopped");

        view.State.Should().Be(RegisterSyncState.Error);
        view.LastErrorMessage.Should().Be("pull pipeline stopped");
    }

    [Fact]
    public void ValidatorSealingObservation_IsPassedThroughInView()
    {
        var now = DateTimeOffset.UtcNow;
        var obs = new[] { new PeerHeightObservation(RegisterId, "n1", 10, now) };
        var sealing = new ValidatorSealingObservation(RegisterId, 10, 3, now);

        var view = BuildResolver().Resolve(RegisterId, 10, obs, sealing, null, null);

        view.State.Should().Be(RegisterSyncState.CaughtUp);
        view.ValidatorSnapshot.Should().NotBeNull();
        view.ValidatorSnapshot!.LastSealedHeight.Should().Be(10);
        view.ValidatorSnapshot.MempoolDepth.Should().Be(3);
    }

    [Fact]
    public void HwmIsTakenAsMaxAcrossPeers()
    {
        var now = DateTimeOffset.UtcNow;
        var obs = new[]
        {
            new PeerHeightObservation(RegisterId, "n1", 7, now),
            new PeerHeightObservation(RegisterId, "n2", 42, now),
            new PeerHeightObservation(RegisterId, "n3", 10, now)
        };
        var view = BuildResolver().Resolve(RegisterId, 5, obs, null, null, null);

        view.NetworkHeightHighWaterMark.Should().Be(42);
        view.State.Should().Be(RegisterSyncState.Syncing);
    }
}
