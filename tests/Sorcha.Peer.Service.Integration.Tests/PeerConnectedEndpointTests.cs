// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;

using FluentAssertions;

using Sorcha.Peer.Service.Integration.Tests.Infrastructure;

namespace Sorcha.Peer.Service.Integration.Tests;

/// <summary>
/// Integration tests for the connected peers endpoint.
/// Tests /api/peers/connected which is publicly accessible (AllowAnonymous).
/// </summary>
[Collection("PeerIntegration")]
public class PeerConnectedEndpointTests : IClassFixture<PeerTestFixture>
{
    private readonly PeerTestFixture _fixture;

    public PeerConnectedEndpointTests(PeerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConnectedPeers_Anonymous_Should_Return_Count_Only()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/peers/connected");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ConnectedPeersAnonymousResponse>();
        result.Should().NotBeNull();
        result!.ConnectedPeerCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ConnectedPeers_Count_Should_Be_NonNegative()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/peers/connected");
        var result = await response.Content.ReadFromJsonAsync<ConnectedPeersAnonymousResponse>();

        // Assert
        result.Should().NotBeNull();
        result!.ConnectedPeerCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ConnectedPeers_Endpoint_Should_Be_Accessible_Without_Authentication()
    {
        // Arrange - Use a fresh client from the factory (no auth headers)
        var peer = _fixture.Peers[0];
        using var client = peer.Factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/peers/connected");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ConnectedPeersAnonymousResponse>();
        result.Should().NotBeNull();
        result!.ConnectedPeerCount.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ConnectedPeers_Should_Reflect_Network_State()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act - Get connected peers count
        var response = await peer.HttpClient.GetAsync("/api/peers/connected");
        var connectedResult = await response.Content.ReadFromJsonAsync<ConnectedPeersAnonymousResponse>();

        // Act - Get health endpoint to compare
        var healthResponse = await peer.HttpClient.GetAsync("/api/peers/health");
        var healthResult = await healthResponse.Content.ReadFromJsonAsync<PeerHealthResponse>();

        // Assert - Connected peers should match healthy peers count
        connectedResult.Should().NotBeNull();
        healthResult.Should().NotBeNull();
        connectedResult!.ConnectedPeerCount.Should().Be(healthResult!.HealthyPeers);
    }

    [Fact]
    public async Task All_Peers_Should_Report_Connected_Peers()
    {
        // Arrange & Act
        var connectedCounts = await Task.WhenAll(_fixture.Peers.Select(async peer =>
        {
            var response = await peer.HttpClient.GetAsync("/api/peers/connected");
            var result = await response.Content.ReadFromJsonAsync<ConnectedPeersAnonymousResponse>();
            return (peer.PeerId, result);
        }));

        // Assert
        foreach (var (peerId, result) in connectedCounts)
        {
            result.Should().NotBeNull($"Connected peers result for {peerId} should not be null");
            result!.ConnectedPeerCount.Should().BeGreaterThanOrEqualTo(0, $"Peer {peerId} should report non-negative peer count");
        }
    }

    // Helper classes for deserialization
    private class ConnectedPeersAnonymousResponse
    {
        public int ConnectedPeerCount { get; set; }
    }

    private class ConnectedPeersAuthenticatedResponse
    {
        public int ConnectedPeerCount { get; set; }
        public List<PeerInfo>? Peers { get; set; }
    }

    private class PeerInfo
    {
        public string PeerId { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Port { get; set; }
        public List<string> SupportedProtocols { get; set; } = new();
        public DateTimeOffset LastSeen { get; set; }
        public int AverageLatencyMs { get; set; }
        public bool IsSeedNode { get; set; }
    }

    private class PeerHealthResponse
    {
        public int TotalPeers { get; set; }
        public int HealthyPeers { get; set; }
        public int UnhealthyPeers { get; set; }
        public double HealthPercentage { get; set; }
    }
}
