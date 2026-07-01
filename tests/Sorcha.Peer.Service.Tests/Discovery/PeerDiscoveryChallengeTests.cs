// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Identity;
using Sorcha.Peer.Service.Protos;
using FluentAssertions;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Discovery;

/// <summary>
/// Tests the Feature 175 node-identity registration challenge on <see cref="PeerDiscoveryServiceImpl"/>:
/// a valid proof marks the peer verified, a bad/replayed proof is refused fail-closed, and absent proof
/// still registers (backward compatibility for the cleartext intra-installation mesh — T013).
/// </summary>
public sealed class PeerDiscoveryChallengeTests
{
    private static PeerDiscoveryServiceImpl CreateService(out IPeerChallengeStore store)
    {
        var config = Options.Create(new PeerServiceConfiguration
        {
            NodeId = "server-node",
            ChallengeTtlSeconds = 30,
            PeerDiscovery = new PeerDiscoveryConfiguration { MaxPeersInList = 100 }
        });
        var peerListManager = new PeerListManager(new Mock<ILogger<PeerListManager>>().Object, config);
        store = new PeerChallengeStore(config);
        return new PeerDiscoveryServiceImpl(
            new Mock<ILogger<PeerDiscoveryServiceImpl>>().Object, config, peerListManager, store);
    }

    private static PeerInfo Peer(string id) => new()
    {
        PeerId = id,
        Address = "10.0.0.1",
        Port = 5001
    };

    [Fact]
    public async Task Valid_Proof_Registers_Peer_As_Node_Identity_Verified()
    {
        var service = CreateService(out _);
        var node = new NodeIdentityProvider(nodeId: "client-node");

        var challenge = await service.GetRegistrationChallenge(
            new RegistrationChallengeRequest { RequestingPeerId = "client-node" }, TestContext());

        var response = await service.RegisterPeer(new RegisterPeerRequest
        {
            PeerInfo = Peer("client-node"),
            ChallengeNonce = challenge.Nonce,
            NodeSignature = ByteString.CopyFrom(node.SignChallenge(NodeChallenge.NonceBytes(challenge.Nonce))),
            NodePublicKey = ByteString.CopyFrom(node.ExportPublicKey())
        }, TestContext());

        response.Success.Should().BeTrue();
        response.NodeIdentityVerified.Should().BeTrue();
    }

    [Fact]
    public async Task Absent_Proof_Still_Registers_But_Unverified()
    {
        var service = CreateService(out _);

        var response = await service.RegisterPeer(
            new RegisterPeerRequest { PeerInfo = Peer("legacy-node") }, TestContext());

        response.Success.Should().BeTrue("legacy/cleartext peers must keep registering");
        response.NodeIdentityVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Tampered_Signature_Is_Refused_FailClosed()
    {
        var service = CreateService(out _);
        var node = new NodeIdentityProvider(nodeId: "client-node");

        var challenge = await service.GetRegistrationChallenge(
            new RegistrationChallengeRequest { RequestingPeerId = "client-node" }, TestContext());

        var badSignature = node.SignChallenge(NodeChallenge.NonceBytes("a-different-nonce"));

        var response = await service.RegisterPeer(new RegisterPeerRequest
        {
            PeerInfo = Peer("client-node"),
            ChallengeNonce = challenge.Nonce,
            NodeSignature = ByteString.CopyFrom(badSignature),
            NodePublicKey = ByteString.CopyFrom(node.ExportPublicKey())
        }, TestContext());

        response.Success.Should().BeFalse("a present-but-invalid proof is never silently downgraded");
        response.NodeIdentityVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Replayed_Nonce_Is_Refused()
    {
        var service = CreateService(out _);
        var node = new NodeIdentityProvider(nodeId: "client-node");

        var challenge = await service.GetRegistrationChallenge(
            new RegistrationChallengeRequest { RequestingPeerId = "client-node" }, TestContext());

        RegisterPeerRequest Build() => new()
        {
            PeerInfo = Peer("client-node"),
            ChallengeNonce = challenge.Nonce,
            NodeSignature = ByteString.CopyFrom(node.SignChallenge(NodeChallenge.NonceBytes(challenge.Nonce))),
            NodePublicKey = ByteString.CopyFrom(node.ExportPublicKey())
        };

        (await service.RegisterPeer(Build(), TestContext())).NodeIdentityVerified.Should().BeTrue();
        (await service.RegisterPeer(Build(), TestContext())).Success
            .Should().BeFalse("the nonce is single-use; replay is refused");
    }

    [Fact]
    public async Task Unknown_Nonce_Is_Refused()
    {
        var service = CreateService(out _);
        var node = new NodeIdentityProvider(nodeId: "client-node");

        var response = await service.RegisterPeer(new RegisterPeerRequest
        {
            PeerInfo = Peer("client-node"),
            ChallengeNonce = "never-issued",
            NodeSignature = ByteString.CopyFrom(node.SignChallenge(NodeChallenge.NonceBytes("never-issued"))),
            NodePublicKey = ByteString.CopyFrom(node.ExportPublicKey())
        }, TestContext());

        response.Success.Should().BeFalse();
    }

    private static Grpc.Core.ServerCallContext TestContext() => new TestServerCallContext();

    /// <summary>Minimal <see cref="Grpc.Core.ServerCallContext"/> for direct handler invocation.</summary>
    private sealed class TestServerCallContext : Grpc.Core.ServerCallContext
    {
        protected override Task WriteResponseHeadersAsyncCore(Grpc.Core.Metadata responseHeaders) => Task.CompletedTask;
        protected override Grpc.Core.ContextPropagationToken CreatePropagationTokenCore(Grpc.Core.ContextPropagationOptions? options) => null!;
        protected override string MethodCore => "RegisterPeer";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "ipv4:10.0.0.1:5001";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Grpc.Core.Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Grpc.Core.Metadata ResponseTrailersCore => new();
        protected override Grpc.Core.Status StatusCore { get; set; }
        protected override Grpc.Core.WriteOptions? WriteOptionsCore { get; set; }
        protected override Grpc.Core.AuthContext AuthContextCore => new(null, new Dictionary<string, List<Grpc.Core.AuthProperty>>());
    }
}
