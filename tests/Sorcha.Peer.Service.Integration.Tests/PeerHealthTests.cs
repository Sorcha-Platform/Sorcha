// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Sorcha.Peer.Service.Integration.Tests.Infrastructure;

namespace Sorcha.Peer.Service.Integration.Tests;

/// <summary>
/// Integration tests for peer service health and metrics endpoints.
/// Tests the /api/health endpoint which returns service status and metrics.
/// </summary>
[Collection("PeerIntegration")]
public class PeerHealthTests : IClassFixture<PeerTestFixture>
{
    private readonly PeerTestFixture _fixture;

    public PeerHealthTests(PeerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Health_Endpoint_Should_Return_Healthy_Status()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
        health.Should().NotBeNull();
        health!.Status.Should().Be("healthy");
        health.Service.Should().Be("peer-service");
        health.Version.Should().NotBeNullOrEmpty();
        health.Uptime.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Health_Check_Should_Include_Metrics()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/health");
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert
        health.Should().NotBeNull();
        health!.Metrics.Should().NotBeNull();
        health.Metrics!.TotalPeers.Should().BeGreaterThanOrEqualTo(0);
        health.Metrics.HealthyPeers.Should().BeGreaterThanOrEqualTo(0);
        health.Metrics.UnhealthyPeers.Should().BeGreaterThanOrEqualTo(0);
        health.Metrics.QueueSize.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Health_Metrics_Should_Reflect_Peer_State()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/health");
        var health = await response.Content.ReadFromJsonAsync<HealthResponse>();

        // Assert
        health.Should().NotBeNull();
        health!.Metrics.Should().NotBeNull();

        // UnhealthyPeers should equal TotalPeers minus HealthyPeers
        health.Metrics!.UnhealthyPeers.Should().Be(health.Metrics.TotalPeers - health.Metrics.HealthyPeers);
    }

    [Fact]
    public async Task All_Peers_Should_Report_Healthy()
    {
        // Arrange & Act
        var healthChecks = await Task.WhenAll(_fixture.Peers.Select(async peer =>
        {
            var response = await peer.HttpClient.GetAsync("/api/health");
            var health = await response.Content.ReadFromJsonAsync<HealthResponse>();
            return (peer.PeerId, health);
        }));

        // Assert
        foreach (var (peerId, health) in healthChecks)
        {
            health.Should().NotBeNull($"Health check for {peerId} should return data");
            health!.Status.Should().Be("healthy", $"Peer {peerId} should be healthy");
        }
    }

    [Fact]
    public async Task Stats_Endpoint_Should_Return_Statistics()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/peers/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PeerHealth_Endpoint_Should_Return_Peer_Health()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/peers/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var peerHealth = await response.Content.ReadFromJsonAsync<PeerHealthResponse>();
        peerHealth.Should().NotBeNull();
        peerHealth!.TotalPeers.Should().BeGreaterThanOrEqualTo(0);
        peerHealth.HealthyPeers.Should().BeGreaterThanOrEqualTo(0);
        peerHealth.HealthPercentage.Should().BeGreaterThanOrEqualTo(0);
    }

    // Helper classes for deserialization
    private class HealthResponse
    {
        public string Status { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Uptime { get; set; } = string.Empty;
        public HealthMetrics? Metrics { get; set; }
    }

    private class HealthMetrics
    {
        public int TotalPeers { get; set; }
        public int HealthyPeers { get; set; }
        public int UnhealthyPeers { get; set; }
        public double AverageLatencyMs { get; set; }
        public long QueueSize { get; set; }
    }

    private class PeerHealthResponse
    {
        public int TotalPeers { get; set; }
        public int HealthyPeers { get; set; }
        public int UnhealthyPeers { get; set; }
        public double HealthPercentage { get; set; }
    }
}
