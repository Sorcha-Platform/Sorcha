// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using FluentAssertions;

using Sorcha.Peer.Service.Integration.Tests.Infrastructure;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Integration.Tests;

/// <summary>
/// Integration tests for peer-to-peer communication.
/// Tests gRPC Ping for basic peer communication and REST endpoints
/// for peer network status.
///
/// Note: The PeerCommunication gRPC service (SendMessage, Stream) and
/// TransactionDistribution gRPC service (NotifyTransaction, GetTransaction,
/// StreamTransaction) are not yet implemented as server-side gRPC services.
/// Tests for those will be added when Wave 3 gRPC servers are implemented.
/// </summary>
[Collection("PeerIntegration")]
public class PeerCommunicationTests : IClassFixture<PeerTestFixture>
{
    private readonly PeerTestFixture _fixture;

    public PeerCommunicationTests(PeerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Ping_Should_Return_Online_Status()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.GrpcClient.PingAsync(new PingRequest
        {
            PeerId = peer.PeerId
        });

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(PeerStatus.Online);
        response.Timestamp.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Ping_Between_Different_Peers_Should_Work()
    {
        // Arrange
        var peer1 = _fixture.Peers[0];
        var peer2 = _fixture.Peers[1];

        // Act
        var response1 = await peer1.GrpcClient.PingAsync(new PingRequest
        {
            PeerId = peer2.PeerId
        });
        var response2 = await peer2.GrpcClient.PingAsync(new PingRequest
        {
            PeerId = peer1.PeerId
        });

        // Assert
        response1.Should().NotBeNull();
        response1.Status.Should().Be(PeerStatus.Online);

        response2.Should().NotBeNull();
        response2.Status.Should().Be(PeerStatus.Online);
    }

    [Fact]
    public async Task Ping_With_Empty_PeerId_Should_Still_Respond()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.GrpcClient.PingAsync(new PingRequest
        {
            PeerId = ""
        });

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(PeerStatus.Online);
    }

    [Fact]
    public async Task Multiple_Sequential_Pings_Should_All_Succeed()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var pingCount = 10;

        // Act & Assert
        for (int i = 0; i < pingCount; i++)
        {
            var response = await peer.GrpcClient.PingAsync(new PingRequest
            {
                PeerId = $"ping-test-{i}"
            });

            response.Should().NotBeNull();
            response.Status.Should().Be(PeerStatus.Online);
        }
    }

    [Fact]
    public async Task Health_Endpoint_Should_Be_Accessible()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
    }

    [Fact]
    public async Task Peer_Quality_Endpoint_Should_Return_OK()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Act
        var response = await peer.HttpClient.GetAsync("/api/peers/quality");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task All_Peer_Instances_Should_Respond_To_Ping()
    {
        // Arrange & Act
        var pingResults = await Task.WhenAll(_fixture.Peers.Select(async peer =>
        {
            var response = await peer.GrpcClient.PingAsync(new PingRequest
            {
                PeerId = peer.PeerId
            });
            return (peer.PeerId, response);
        }));

        // Assert
        foreach (var (peerId, response) in pingResults)
        {
            response.Should().NotBeNull($"Ping response for {peerId} should not be null");
            response.Status.Should().Be(PeerStatus.Online, $"Peer {peerId} should report Online status");
        }
    }

    [Fact(Skip = "PeerCommunication gRPC server not yet implemented (Wave 3)")]
    public async Task SendMessage_Should_Be_Acknowledged()
    {
        // This test will be implemented when the PeerCommunication gRPC service
        // (SendMessage, Stream) is wired up in Program.cs
        await Task.CompletedTask;
    }

    [Fact(Skip = "PeerCommunication gRPC server not yet implemented (Wave 3)")]
    public async Task Bidirectional_Stream_Should_Work_Correctly()
    {
        // This test will be implemented when the PeerCommunication gRPC service
        // bidirectional streaming is available
        await Task.CompletedTask;
    }

    [Fact(Skip = "TransactionDistribution gRPC server not yet implemented (Wave 3)")]
    public async Task Transaction_Notification_Should_Be_Delivered()
    {
        // This test will be implemented when the TransactionDistribution gRPC service
        // (NotifyTransaction, GetTransaction, StreamTransaction) is wired up in Program.cs
        await Task.CompletedTask;
    }
}
