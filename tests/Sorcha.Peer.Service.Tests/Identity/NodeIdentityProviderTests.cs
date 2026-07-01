// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Sorcha.Peer.Service.Identity;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Identity;

/// <summary>
/// Unit tests for <see cref="NodeIdentityProvider"/> (Feature 175) — the self-signed node identity
/// certificate whose thumbprint is the node's installation-neutral federation identity.
/// </summary>
public sealed class NodeIdentityProviderTests
{
    [Fact]
    public void Generates_SelfSigned_Cert_With_PrivateKey_And_NodeId_Subject()
    {
        var sut = new NodeIdentityProvider(nodeId: "Phaethon.sorcha.dev");

        sut.Certificate.Should().NotBeNull();
        sut.Certificate.HasPrivateKey.Should().BeTrue("the node must sign/negotiate mTLS with its private key");
        sut.Certificate.Subject.Should().Contain("Phaethon.sorcha.dev");
        sut.NodeIdentityThumbprint.Should().Be(sut.Certificate.Thumbprint).And.NotBeNullOrEmpty();
    }

    [Fact]
    public void Certificate_Advertises_Client_And_Server_Auth()
    {
        var sut = new NodeIdentityProvider(nodeId: "node-a");

        var eku = sut.Certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(e => e.EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>())
            .Select(o => o.Value)
            .ToArray();

        eku.Should().Contain("1.3.6.1.5.5.7.3.1", "serverAuth is needed for the peer server side");
        eku.Should().Contain("1.3.6.1.5.5.7.3.2", "clientAuth is needed for outbound mTLS federation");
    }

    [Fact]
    public void Persists_And_Reloads_Same_Identity_Across_Restarts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"node-{Guid.NewGuid():N}.pfx");
        try
        {
            var first = new NodeIdentityProvider(nodeId: "stable-node", certificatePath: path);
            File.Exists(path).Should().BeTrue("a configured path should persist the generated cert");

            var second = new NodeIdentityProvider(nodeId: "stable-node", certificatePath: path);

            second.NodeIdentityThumbprint.Should().Be(first.NodeIdentityThumbprint,
                "reloading from the persisted path must yield the SAME node identity");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Ephemeral_Identities_Differ_Between_Instances()
    {
        var a = new NodeIdentityProvider(nodeId: "n");
        var b = new NodeIdentityProvider(nodeId: "n");

        a.NodeIdentityThumbprint.Should().NotBe(b.NodeIdentityThumbprint,
            "with no persisted path each start generates a fresh identity");
    }
}
