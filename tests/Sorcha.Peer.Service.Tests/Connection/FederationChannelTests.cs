// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Identity;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Connection;

/// <summary>
/// Unit tests for <see cref="FederationChannel"/> (Feature 175) — the outbound peer gRPC transport
/// handler that presents the node identity certificate for mTLS and accepts a peer's self-signed
/// server certificate.
/// </summary>
public sealed class FederationChannelTests
{
    [Fact]
    public void CreateHandler_WithNodeCertificate_PresentsIt_AsClientCertificate()
    {
        var node = new NodeIdentityProvider(nodeId: "node-a").Certificate;

        using var handler = FederationChannel.CreateHandler(node);

        handler.SslOptions.ClientCertificates.Should().NotBeNull();
        handler.SslOptions.ClientCertificates!
            .Cast<X509Certificate>()
            .Should().ContainSingle()
            .Which.Should().BeSameAs(node, "the node identity cert is the mTLS client credential");
    }

    [Fact]
    public void CreateHandler_WithNodeCertificate_AcceptsSelfSignedPeerServerCertificate()
    {
        // A foreign-installation peer (e.g. n1) presents a self-signed server certificate that would
        // otherwise fail chain/name validation. Federation must accept it — data trust is cryptographic.
        var node = new NodeIdentityProvider(nodeId: "node-a").Certificate;
        var foreignPeer = new NodeIdentityProvider(nodeId: "n1.sorcha.dev").Certificate;

        using var handler = FederationChannel.CreateHandler(node);
        var callback = handler.SslOptions.RemoteCertificateValidationCallback;

        callback.Should().NotBeNull();
        callback!(this, foreignPeer, new X509Chain(), SslPolicyErrors.RemoteCertificateChainErrors)
            .Should().BeTrue();
    }

    [Fact]
    public void CreateHandler_WithoutNodeCertificate_LeavesTlsOptionsDefault()
    {
        // Backward-compatible fallback: no client cert, no custom server-cert acceptance (pre-federation
        // behaviour). Used by hosts/tests that construct the pool without a node identity.
        using var handler = FederationChannel.CreateHandler(nodeCertificate: null);

        handler.SslOptions.ClientCertificates.Should().BeNull();
        handler.SslOptions.RemoteCertificateValidationCallback.Should().BeNull();
    }

    [Fact]
    public void AcceptPeerServerCertificate_ReturnsTrue_ForAnyErrors()
    {
        FederationChannel.AcceptPeerServerCertificate(this, null, null, SslPolicyErrors.RemoteCertificateNotAvailable)
            .Should().BeTrue();
    }
}
