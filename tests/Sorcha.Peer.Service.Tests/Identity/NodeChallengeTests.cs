// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Peer.Service.Identity;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Identity;

/// <summary>
/// Unit tests for the Feature 175 node-identity challenge crypto: a node signs a nonce with its
/// identity key and a verifier confirms possession from the public key alone (works off-transport).
/// </summary>
public sealed class NodeChallengeTests
{
    [Fact]
    public void Sign_Then_Verify_RoundTrips_For_The_Node_Key()
    {
        var node = new NodeIdentityProvider(nodeId: "node-a");
        const string nonce = "DEADBEEFCAFE";

        var signature = node.SignChallenge(NodeChallenge.NonceBytes(nonce));

        NodeChallenge.Verify(node.ExportPublicKey(), nonce, signature).Should().BeTrue();
    }

    [Fact]
    public void Verify_Fails_For_A_Different_Nodes_Key()
    {
        var signer = new NodeIdentityProvider(nodeId: "node-a");
        var impostor = new NodeIdentityProvider(nodeId: "node-b");
        const string nonce = "0011223344";

        var signature = signer.SignChallenge(NodeChallenge.NonceBytes(nonce));

        NodeChallenge.Verify(impostor.ExportPublicKey(), nonce, signature)
            .Should().BeFalse("only the signing node's public key should verify");
    }

    [Fact]
    public void Verify_Fails_For_A_Tampered_Nonce()
    {
        var node = new NodeIdentityProvider(nodeId: "node-a");
        var signature = node.SignChallenge(NodeChallenge.NonceBytes("original-nonce"));

        NodeChallenge.Verify(node.ExportPublicKey(), "tampered-nonce", signature).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonce")]
    public void Verify_Returns_False_For_Malformed_Input_Without_Throwing(string nonce)
    {
        NodeChallenge.Verify([], nonce, [1, 2, 3]).Should().BeFalse();
        NodeChallenge.Verify([9, 9, 9], nonce, []).Should().BeFalse();
    }

    [Fact]
    public void IdentityFromPublicKey_Is_Stable_And_KeyBound()
    {
        var node = new NodeIdentityProvider(nodeId: "node-a");
        var other = new NodeIdentityProvider(nodeId: "node-b");

        var id1 = NodeChallenge.IdentityFromPublicKey(node.ExportPublicKey());
        var id2 = NodeChallenge.IdentityFromPublicKey(node.ExportPublicKey());
        var idOther = NodeChallenge.IdentityFromPublicKey(other.ExportPublicKey());

        id1.Should().Be(id2).And.NotBeNullOrEmpty();
        id1.Should().NotBe(idOther);
    }
}
