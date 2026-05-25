// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Peer.Service.Core;

namespace Sorcha.Peer.Service.Tests.Core;

public class RegisterSyncConfigurationTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        var config = new RegisterSyncConfiguration();

        config.PeriodicSyncIntervalSeconds.Should().Be(90);
        config.RelayPollIntervalSeconds.Should().Be(20);
        config.HeartbeatIntervalSeconds.Should().Be(30);
        config.HeartbeatTimeoutSeconds.Should().Be(30);
        config.MaxRetryAttempts.Should().Be(10);
        config.MaxMissedHeartbeats.Should().Be(2);
        config.ConnectionTimeoutSeconds.Should().Be(30);
        config.MaxConcurrentDocketPulls.Should().Be(3);
        config.DocketPullBatchSize.Should().Be(100);
    }

    [Fact]
    public void ShouldAllowCustomConfiguration()
    {
        var config = new RegisterSyncConfiguration
        {
            PeriodicSyncIntervalSeconds = 120,
            HeartbeatIntervalSeconds = 60,
            MaxConcurrentDocketPulls = 5,
            DocketPullBatchSize = 500
        };

        config.PeriodicSyncIntervalSeconds.Should().Be(120);
        config.HeartbeatIntervalSeconds.Should().Be(60);
        config.MaxConcurrentDocketPulls.Should().Be(5);
        config.DocketPullBatchSize.Should().Be(500);
    }
}
