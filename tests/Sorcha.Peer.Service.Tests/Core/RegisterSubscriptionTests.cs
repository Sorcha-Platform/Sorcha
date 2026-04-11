// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Peer.Service.Core;

namespace Sorcha.Peer.Service.Tests.Core;

public class RegisterSubscriptionTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        var sub = new RegisterSubscription();

        sub.Id.Should().NotBeEmpty();
        sub.RegisterId.Should().BeEmpty();
        sub.Mode.Should().Be(ReplicationMode.ForwardOnly);
        sub.SyncState.Should().Be(RegisterSyncState.Subscribing);
        sub.LastSyncedDocketVersion.Should().Be(-1); // -1 = never synced
        sub.LastSyncedTransactionVersion.Should().Be(0);
        sub.TotalDocketsInChain.Should().Be(0);
        sub.SourcePeerIds.Should().NotBeNull().And.BeEmpty();
        sub.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        sub.LastSyncAt.Should().BeNull();
        sub.ErrorMessage.Should().BeNull();
        sub.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void CanParticipateInValidation_ShouldBeFalse_WhenNotFullyReplicated()
    {
        var sub = new RegisterSubscription { SyncState = RegisterSyncState.Subscribing };
        sub.CanParticipateInValidation.Should().BeFalse();

        sub.SyncState = RegisterSyncState.Syncing;
        sub.CanParticipateInValidation.Should().BeFalse();

        sub.SyncState = RegisterSyncState.Active;
        sub.CanParticipateInValidation.Should().BeFalse();

        sub.SyncState = RegisterSyncState.Error;
        sub.CanParticipateInValidation.Should().BeFalse();
    }

    [Fact]
    public void CanParticipateInValidation_ShouldBeTrue_WhenFullyReplicated()
    {
        var sub = new RegisterSubscription { SyncState = RegisterSyncState.FullyReplicated };
        sub.CanParticipateInValidation.Should().BeTrue();
    }

    [Theory]
    [InlineData(RegisterSyncState.FullyReplicated)]
    [InlineData(RegisterSyncState.Active)]
    public void IsReceiving_ShouldBeTrue_WhenActiveOrFullyReplicated(RegisterSyncState state)
    {
        var sub = new RegisterSubscription { SyncState = state };
        sub.IsReceiving.Should().BeTrue();
    }

    [Theory]
    [InlineData(RegisterSyncState.Subscribing)]
    [InlineData(RegisterSyncState.Syncing)]
    [InlineData(RegisterSyncState.Error)]
    public void IsReceiving_ShouldBeFalse_WhenNotActiveOrFullyReplicated(RegisterSyncState state)
    {
        var sub = new RegisterSubscription { SyncState = state };
        sub.IsReceiving.Should().BeFalse();
    }

    [Fact]
    public void SyncProgressPercent_ShouldReturn100_WhenForwardOnlyAndActive()
    {
        var sub = new RegisterSubscription
        {
            Mode = ReplicationMode.ForwardOnly,
            SyncState = RegisterSyncState.Active
        };
        sub.SyncProgressPercent.Should().Be(100.0);
    }

    [Fact]
    public void SyncProgressPercent_ShouldReturn0_WhenForwardOnlyAndNotActive()
    {
        var sub = new RegisterSubscription
        {
            Mode = ReplicationMode.ForwardOnly,
            SyncState = RegisterSyncState.Subscribing
        };
        sub.SyncProgressPercent.Should().Be(0.0);
    }

    [Fact]
    public void SyncProgressPercent_ShouldCalculateCorrectly_ForFullReplica()
    {
        var sub = new RegisterSubscription
        {
            Mode = ReplicationMode.FullReplica,
            TotalDocketsInChain = 100,
            LastSyncedDocketVersion = 50
        };
        sub.SyncProgressPercent.Should().Be(50.0);
    }

    [Fact]
    public void SyncProgressPercent_ShouldReturn0_WhenTotalDocketsIsZero()
    {
        var sub = new RegisterSubscription
        {
            Mode = ReplicationMode.FullReplica,
            TotalDocketsInChain = 0,
            LastSyncedDocketVersion = 0
        };
        sub.SyncProgressPercent.Should().Be(0.0);
    }

    [Fact]
    public void SyncProgressPercent_ShouldNotExceed100()
    {
        var sub = new RegisterSubscription
        {
            Mode = ReplicationMode.FullReplica,
            TotalDocketsInChain = 100,
            LastSyncedDocketVersion = 150 // Should cap at 100
        };
        sub.SyncProgressPercent.Should().Be(100.0);
    }

    [Fact]
    public void RecordSyncSuccess_ShouldUpdateState()
    {
        var sub = new RegisterSubscription();

        sub.RecordSyncSuccess(docketVersion: 42, transactionVersion: 100);

        sub.LastSyncedDocketVersion.Should().Be(42);
        sub.LastSyncedTransactionVersion.Should().Be(100);
        sub.LastSyncAt.Should().NotBeNull();
        sub.LastSyncAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1));
        sub.ConsecutiveFailures.Should().Be(0);
        sub.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RecordSyncSuccess_ShouldResetFailureCount()
    {
        var sub = new RegisterSubscription { ConsecutiveFailures = 5, ErrorMessage = "previous error" };

        sub.RecordSyncSuccess(docketVersion: 10, transactionVersion: 20);

        sub.ConsecutiveFailures.Should().Be(0);
        sub.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RecordSyncFailure_ShouldIncrementFailures()
    {
        var sub = new RegisterSubscription();

        sub.RecordSyncFailure("test error");

        sub.ConsecutiveFailures.Should().Be(1);
        sub.ErrorMessage.Should().Be("test error");
    }

    [Fact]
    public void RecordSyncFailure_ShouldTransitionToError_After10Failures()
    {
        var sub = new RegisterSubscription { ConsecutiveFailures = 9, SyncState = RegisterSyncState.Syncing };

        sub.RecordSyncFailure("critical error");

        sub.ConsecutiveFailures.Should().Be(10);
        sub.SyncState.Should().Be(RegisterSyncState.Error);
    }

    [Fact]
    public void RecordSyncFailure_ShouldNotTransitionToError_Before10Failures()
    {
        var sub = new RegisterSubscription { ConsecutiveFailures = 8, SyncState = RegisterSyncState.Syncing };

        sub.RecordSyncFailure("error");

        sub.ConsecutiveFailures.Should().Be(9);
        sub.SyncState.Should().Be(RegisterSyncState.Syncing);
    }

    [Fact]
    public void TransitionToNextState_SubscribingFullReplica_ShouldGoToSyncing()
    {
        var sub = new RegisterSubscription
        {
            SyncState = RegisterSyncState.Subscribing,
            Mode = ReplicationMode.FullReplica
        };

        sub.TransitionToNextState();

        sub.SyncState.Should().Be(RegisterSyncState.Syncing);
    }

    [Fact]
    public void TransitionToNextState_SubscribingForwardOnly_ShouldGoToActive()
    {
        var sub = new RegisterSubscription
        {
            SyncState = RegisterSyncState.Subscribing,
            Mode = ReplicationMode.ForwardOnly
        };

        sub.TransitionToNextState();

        sub.SyncState.Should().Be(RegisterSyncState.Active);
    }

    [Fact]
    public void TransitionToNextState_Syncing_ShouldGoToFullyReplicated()
    {
        var sub = new RegisterSubscription
        {
            SyncState = RegisterSyncState.Syncing,
            Mode = ReplicationMode.FullReplica
        };

        sub.TransitionToNextState();

        sub.SyncState.Should().Be(RegisterSyncState.FullyReplicated);
    }

    [Fact]
    public void TransitionToNextState_FullyReplicated_ShouldRemainSame()
    {
        var sub = new RegisterSubscription { SyncState = RegisterSyncState.FullyReplicated };

        sub.TransitionToNextState();

        sub.SyncState.Should().Be(RegisterSyncState.FullyReplicated);
    }

    [Fact]
    public void TransitionToNextState_Error_ShouldRemainSame()
    {
        var sub = new RegisterSubscription { SyncState = RegisterSyncState.Error };

        sub.TransitionToNextState();

        sub.SyncState.Should().Be(RegisterSyncState.Error);
    }
}
