// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Models;

namespace Sorcha.Blueprint.Service.Tests.Models;

/// <summary>
/// Tests for RecoveryState thread-safe properties and RecoveryOptions defaults.
/// </summary>
public class RecoveryStateTests
{
    [Fact]
    public void RecoveryState_DefaultValues_AreCorrect()
    {
        var state = new RecoveryState();

        state.IsComplete.Should().BeFalse();
        state.StartedAt.Should().Be(default);
        state.CompletedAt.Should().BeNull();
        state.LastRefreshedAt.Should().BeNull();
        state.RegisterStates.Should().BeEmpty();
    }

    [Fact]
    public void RecoveryState_SetProperties_RetainsValues()
    {
        var state = new RecoveryState();
        var now = DateTimeOffset.UtcNow;

        state.IsComplete = true;
        state.StartedAt = now;
        state.CompletedAt = now.AddSeconds(5);
        state.LastRefreshedAt = now.AddMinutes(1);

        state.IsComplete.Should().BeTrue();
        state.StartedAt.Should().Be(now);
        state.CompletedAt.Should().Be(now.AddSeconds(5));
        state.LastRefreshedAt.Should().Be(now.AddMinutes(1));
    }

    [Fact]
    public void RecoveryState_RegisterStates_SupportsConcurrentAccess()
    {
        var state = new RecoveryState();

        var regState = state.RegisterStates.GetOrAdd("reg-1", _ => new RegisterRecoveryState
        {
            RegisterId = "reg-1",
            RegisterName = "Test"
        });

        regState.RegisterId.Should().Be("reg-1");
        regState.RegisterName.Should().Be("Test");
        state.RegisterStates.Should().ContainKey("reg-1");
    }

    [Fact]
    public void RegisterRecoveryState_DefaultValues_AreCorrect()
    {
        var state = new RegisterRecoveryState();

        state.RegisterId.Should().BeEmpty();
        state.RegisterName.Should().BeEmpty();
        state.Status.Should().Be(RegisterHealthStatus.Unknown);
        state.Height.Should().Be(0);
        state.ConsecutiveFailures.Should().Be(0);
        state.RecoveredBlueprintCount.Should().Be(0);
        state.LastSuccessAt.Should().BeNull();
        state.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void RecoveryOptions_DefaultValues_AreCorrect()
    {
        var options = new RecoveryOptions();

        options.RefreshIntervalSeconds.Should().Be(60);
        options.MaxRetryAttempts.Should().Be(3);
        RecoveryOptions.SectionName.Should().Be("Recovery");
    }

    [Fact]
    public void RegisterHealthStatus_HasExpectedValues()
    {
        RegisterHealthStatus.Unknown.Should().Be(0);
        RegisterHealthStatus.Online.Should().Be((RegisterHealthStatus)1);
        RegisterHealthStatus.Offline.Should().Be((RegisterHealthStatus)2);
    }
}
