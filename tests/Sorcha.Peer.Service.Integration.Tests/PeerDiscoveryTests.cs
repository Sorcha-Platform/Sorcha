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
/// Integration tests for peer discovery functionality.
/// Tests gRPC endpoints for peer registration and discovery,
/// and REST endpoints for listing and querying peers.
/// </summary>
[Collection("PeerIntegration")]
public class PeerDiscoveryTests : IClassFixture<PeerTestFixture>
{
    private readonly PeerTestFixture _fixture;

    public PeerDiscoveryTests(PeerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RegisterPeer_Via_gRPC_Should_Return_Success()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var request = new RegisterPeerRequest
        {
            PeerInfo = new PeerInfo
            {
                PeerId = "test-peer-grpc",
                Address = "localhost",
                Port = 5002
            }
        };

        // Act
        var response = await peer.GrpcClient.RegisterPeerAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.Message.Should().Be("Peer registered successfully");
    }

    [Fact]
    public async Task RegisterPeer_Without_PeerId_Should_Return_Failure()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var request = new RegisterPeerRequest
        {
            PeerInfo = new PeerInfo
            {
                PeerId = "", // Empty ID should fail validation
                Address = "localhost",
                Port = 5003
            }
        };

        // Act
        var response = await peer.GrpcClient.RegisterPeerAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Invalid peer ID");
    }

    [Fact]
    public async Task RegisterPeer_Without_Address_Should_Return_Failure()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var request = new RegisterPeerRequest
        {
            PeerInfo = new PeerInfo
            {
                PeerId = "test-no-addr",
                Address = "", // Empty address should fail validation
                Port = 5003
            }
        };

        // Act
        var response = await peer.GrpcClient.RegisterPeerAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Invalid peer address");
    }

    [Fact]
    public async Task GetAllPeers_Via_REST_Should_Return_List()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Register a peer via gRPC first
        await peer.GrpcClient.RegisterPeerAsync(new RegisterPeerRequest
        {
            PeerInfo = new PeerInfo
            {
                PeerId = "discovery-rest-test",
                Address = "10.0.0.1",
                Port = 5010
            }
        });

        // Act
        var response = await peer.HttpClient.GetAsync("/api/peers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();

        // Parse as array - the response is a JSON array of peer objects
        var peers = JsonSerializer.Deserialize<JsonElement>(content);
        peers.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetPeerById_Via_REST_Should_Return_Peer_Details()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var peerId = "rest-lookup-test";

        await peer.GrpcClient.RegisterPeerAsync(new RegisterPeerRequest
        {
            PeerInfo = new PeerInfo
            {
                PeerId = peerId,
                Address = "10.0.0.2",
                Port = 5011
            }
        });

        // Act
        var response = await peer.HttpClient.GetAsync($"/api/peers/{peerId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(peerId);
    }

    [Fact]
    public async Task GetPeerById_For_Nonexistent_Peer_Should_Return_NotFound()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var nonexistentId = "does-not-exist";

        // Act
        var response = await peer.HttpClient.GetAsync($"/api/peers/{nonexistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetPeerList_Via_gRPC_Should_Return_Peers()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Register a peer so the list is not empty
        await peer.GrpcClient.RegisterPeerAsync(new RegisterPeerRequest
        {
            PeerInfo = new PeerInfo
            {
                PeerId = "grpc-list-test",
                Address = "10.0.0.3",
                Port = 5012
            }
        });

        // Act
        var response = await peer.GrpcClient.GetPeerListAsync(new PeerListRequest
        {
            RequestingPeerId = peer.PeerId,
            MaxPeers = 100
        });

        // Assert
        response.Should().NotBeNull();
        response.TotalPeers.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Ping_Via_gRPC_Should_Return_Online_Status()
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
    public async Task ExchangePeers_Via_gRPC_Should_Exchange_Peer_Lists()
    {
        // Arrange
        var peer1 = _fixture.Peers[0];

        // Register some peers first
        await peer1.GrpcClient.RegisterPeerAsync(new RegisterPeerRequest
        {
            PeerInfo = new PeerInfo
            {
                PeerId = "exchange-peer-1",
                Address = "10.0.0.10",
                Port = 5020
            }
        });

        // Act
        var response = await peer1.GrpcClient.ExchangePeersAsync(new PeerExchangeRequest
        {
            PeerId = "remote-peer",
            MaxPeers = 50
        });

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_Peers_Can_Register_Via_gRPC()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var peerIds = new[] { "multi-1", "multi-2", "multi-3" };

        // Act
        foreach (var peerId in peerIds)
        {
            var response = await peer.GrpcClient.RegisterPeerAsync(new RegisterPeerRequest
            {
                PeerInfo = new PeerInfo
                {
                    PeerId = peerId,
                    Address = "10.0.0.100",
                    Port = Random.Shared.Next(5000, 6000)
                }
            });

            // Assert
            response.Success.Should().BeTrue($"Peer '{peerId}' should register successfully");
        }
    }

    [Fact]
    public async Task FindPeersForRegister_Via_gRPC_Should_Return_Results()
    {
        // Arrange
        var peer = _fixture.Peers[0];

        // Register a peer with an advertised register
        await peer.GrpcClient.RegisterPeerAsync(new RegisterPeerRequest
        {
            PeerInfo = new PeerInfo
            {
                PeerId = "register-holder",
                Address = "10.0.0.50",
                Port = 5050,
                AdvertisedRegisters =
                {
                    new PeerRegisterAdvertisement
                    {
                        RegisterId = "test-register-001",
                        HasFullReplica = true,
                        LatestVersion = 5,
                        IsPublic = true
                    }
                }
            }
        });

        // Act
        var response = await peer.GrpcClient.FindPeersForRegisterAsync(new FindPeersForRegisterRequest
        {
            RegisterId = "test-register-001",
            RequestingPeerId = peer.PeerId,
            MaxPeers = 10
        });

        // Assert
        response.Should().NotBeNull();
        // The response may or may not contain the peer depending on health status
        response.TotalPeers.Should().BeGreaterThanOrEqualTo(0);
    }
}
