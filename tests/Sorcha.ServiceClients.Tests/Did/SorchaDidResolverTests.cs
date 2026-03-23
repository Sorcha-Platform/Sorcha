// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Did;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.ServiceClients.Tests.Did;

public class SorchaDidResolverTests
{
    private readonly Mock<IWalletServiceClient> _walletClientMock = new();
    private readonly Mock<ILogger<SorchaDidResolver>> _loggerMock = new();
    private readonly SorchaDidResolver _resolver;

    public SorchaDidResolverTests()
    {
        _resolver = new SorchaDidResolver(_walletClientMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void CanResolve_Sorcha_ReturnsTrue()
    {
        _resolver.CanResolve("sorcha").Should().BeTrue();
    }

    [Fact]
    public void CanResolve_OtherMethod_ReturnsFalse()
    {
        _resolver.CanResolve("web").Should().BeFalse();
        _resolver.CanResolve("key").Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_WalletDid_ReturnsDidDocument()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("addr-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = "addr-123",
                Name = "Test Wallet",
                PublicKey = "base64PublicKey",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "user-1",
                Tenant = "tenant-1"
            });

        var doc = await _resolver.ResolveAsync("did:sorcha:w:addr-123");

        doc.Should().NotBeNull();
        doc!.Id.Should().Be("did:sorcha:w:addr-123");
        doc.VerificationMethod.Should().HaveCount(1);
        doc.VerificationMethod[0].Type.Should().Be("Ed25519VerificationKey2020");
        doc.VerificationMethod[0].PublicKeyMultibase.Should().Be("zbase64PublicKey");
        doc.Authentication.Should().Contain("did:sorcha:w:addr-123#key-1");
    }

    [Fact]
    public async Task ResolveAsync_WalletNotFound_ReturnsNull()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("unknown", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

        var doc = await _resolver.ResolveAsync("did:sorcha:w:unknown");

        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WalletServiceThrows_ReturnsNull()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("fail", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var doc = await _resolver.ResolveAsync("did:sorcha:w:fail");

        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_RegisterDid_ReturnsServiceEndpoints()
    {
        var doc = await _resolver.ResolveAsync("did:sorcha:r:reg-001:t:abc123");

        doc.Should().NotBeNull();
        doc!.Id.Should().Be("did:sorcha:r:reg-001:t:abc123");
        doc.Service.Should().HaveCount(2);
        doc.Service.Should().Contain(s => s.Type == "SorchaRegister");
        doc.Service.Should().Contain(s => s.Type == "SorchaTransaction");
    }

    [Fact]
    public async Task ResolveAsync_RegisterDidWithoutTxId_ReturnsSingleEndpoint()
    {
        var doc = await _resolver.ResolveAsync("did:sorcha:r:reg-001");

        doc.Should().NotBeNull();
        doc!.Service.Should().HaveCount(1);
        doc.Service[0].Type.Should().Be("SorchaRegister");
    }

    [Fact]
    public async Task ResolveAsync_EmptyDid_ReturnsNull()
    {
        var doc = await _resolver.ResolveAsync("");
        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_UnrecognizedSorchaFormat_ReturnsNull()
    {
        var doc = await _resolver.ResolveAsync("did:sorcha:x:something");
        doc.Should().BeNull();
    }

    // --- Organization DID Resolution ---

    [Fact]
    public async Task ResolveAsync_OrgDid_ReturnsDidDocument()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("org-addr-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = "org-addr-456",
                Name = "org-acme-signing",
                PublicKey = "orgPublicKeyBase64",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "org:acme-id",
                Tenant = "acme-id"
            });

        var doc = await _resolver.ResolveAsync("did:sorcha:org:org-addr-456");

        doc.Should().NotBeNull();
        doc!.Id.Should().Be("did:sorcha:org:org-addr-456");
        doc.VerificationMethod.Should().HaveCount(1);
        doc.VerificationMethod[0].Type.Should().Be("Ed25519VerificationKey2020");
        doc.VerificationMethod[0].PublicKeyMultibase.Should().Be("zorgPublicKeyBase64");
        doc.Authentication.Should().Contain("did:sorcha:org:org-addr-456#key-1");
        doc.AssertionMethod.Should().Contain("did:sorcha:org:org-addr-456#key-1");
    }

    [Fact]
    public async Task ResolveAsync_OrgDid_WalletNotFound_ReturnsNull()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("unknown-org", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WalletInfo?)null);

        var doc = await _resolver.ResolveAsync("did:sorcha:org:unknown-org");

        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_OrgDid_EmptyAddress_ReturnsNull()
    {
        var doc = await _resolver.ResolveAsync("did:sorcha:org:");

        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_OrgDid_ServiceThrows_ReturnsNull()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("fail-org", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var doc = await _resolver.ResolveAsync("did:sorcha:org:fail-org");

        doc.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_P256Algorithm_ReturnsJsonWebKey2020()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("p256-addr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = "p256-addr",
                Name = "P256 Wallet",
                PublicKey = "compressedKey",
                Algorithm = "NIST-P256",
                Status = "Active",
                Owner = "user-2",
                Tenant = "tenant-1"
            });

        var doc = await _resolver.ResolveAsync("did:sorcha:w:p256-addr");

        doc.Should().NotBeNull();
        doc!.VerificationMethod[0].Type.Should().Be("JsonWebKey2020");
    }
}
