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
