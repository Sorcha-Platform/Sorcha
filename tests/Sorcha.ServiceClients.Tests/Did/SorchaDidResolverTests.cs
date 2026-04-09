// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using SimpleBase;
using Sorcha.ServiceClients.Did;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.ServiceClients.Tests.Did;

public class SorchaDidResolverTests
{
    // Realistic test public keys in base64 (the canonical storage format per
    // WalletEndpoints.cs) so the Feature 093 US3 multibase fix can round-trip them.
    private static readonly byte[] Ed25519KeyBytes = new byte[32]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20
    };
    private static readonly string Ed25519KeyBase64 = Convert.ToBase64String(Ed25519KeyBytes);

    private static readonly byte[] P256KeyBytes = new byte[33]
    {
        0x02,
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20
    };
    private static readonly string P256KeyBase64 = Convert.ToBase64String(P256KeyBytes);

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
    public async Task ResolveAsync_WalletDid_ReturnsDidDocumentWithValidMultibase()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("addr-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = "addr-123",
                Name = "Test Wallet",
                PublicKey = Ed25519KeyBase64,
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

        // Feature 093 US3: publicKeyMultibase MUST be "z" + Base58btc(0xed 0x01 || rawKey)
        var multibase = doc.VerificationMethod[0].PublicKeyMultibase;
        multibase.Should().NotBeNull();
        multibase!.Should().StartWith("z", because: "W3C base58btc multibase prefix");

        var decoded = Base58.Bitcoin.Decode(multibase[1..]);
        decoded[0].Should().Be(0xed, because: "Ed25519 multicodec varint first byte");
        decoded[1].Should().Be(0x01, because: "Ed25519 multicodec varint second byte");
        decoded.Skip(2).Should().Equal(Ed25519KeyBytes);

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
    public async Task ResolveAsync_OrgDid_ReturnsDidDocumentWithValidMultibase()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("org-addr-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = "org-addr-456",
                Name = "org-acme-signing",
                PublicKey = Ed25519KeyBase64,
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

        // Feature 093 US3 FR-015: org DIDs produce the same valid multibase as wallet DIDs.
        var multibase = doc.VerificationMethod[0].PublicKeyMultibase;
        multibase.Should().NotBeNull();
        multibase!.Should().StartWith("z");

        var decoded = Base58.Bitcoin.Decode(multibase[1..]);
        decoded[0].Should().Be(0xed);
        decoded[1].Should().Be(0x01);
        decoded.Skip(2).Should().Equal(Ed25519KeyBytes);

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
    public async Task ResolveAsync_P256Algorithm_ReturnsJsonWebKey2020_WithValidMultibase()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("p256-addr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = "p256-addr",
                Name = "P256 Wallet",
                PublicKey = P256KeyBase64,
                Algorithm = "NIST-P256",
                Status = "Active",
                Owner = "user-2",
                Tenant = "tenant-1"
            });

        var doc = await _resolver.ResolveAsync("did:sorcha:w:p256-addr");

        doc.Should().NotBeNull();
        doc!.VerificationMethod[0].Type.Should().Be("JsonWebKey2020");

        // Feature 093 US3: NIST P-256 multicodec varint is 0x80 0x24.
        var multibase = doc.VerificationMethod[0].PublicKeyMultibase;
        multibase.Should().NotBeNull();
        multibase!.Should().StartWith("z");

        var decoded = Base58.Bitcoin.Decode(multibase[1..]);
        decoded[0].Should().Be(0x80);
        decoded[1].Should().Be(0x24);
        decoded.Skip(2).Should().Equal(P256KeyBytes);
    }

    // --- Feature 093 US3: unsupported algorithm fallback ---

    [Fact]
    public async Task ResolveAsync_UnsupportedAlgorithm_FallsBackToPublicKeyJwk()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("pqc-addr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = "pqc-addr",
                Name = "PQC Wallet",
                PublicKey = Ed25519KeyBase64, // any base64 bytes
                Algorithm = "ML-DSA-65",      // no multicodec assignment
                Status = "Active",
                Owner = "user-3",
                Tenant = "tenant-1"
            });

        var doc = await _resolver.ResolveAsync("did:sorcha:w:pqc-addr");

        doc.Should().NotBeNull();
        doc!.VerificationMethod[0].PublicKeyMultibase.Should().BeNull(
            because: "PQC algorithms have no multicodec identifier — the resolver must not emit malformed multibase");
        doc.VerificationMethod[0].PublicKeyJwk.Should().NotBeNull(
            because: "unsupported algorithms fall back to publicKeyJwk per FR-014");
    }

    [Fact]
    public async Task ResolveAsync_InvalidBase64PublicKey_EmitsJwkFallback_NotMalformedMultibase()
    {
        _walletClientMock
            .Setup(w => w.GetWalletAsync("bad-key-addr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletInfo
            {
                Address = "bad-key-addr",
                Name = "Bad Key Wallet",
                PublicKey = "!!!not-base64-or-hex!!!",
                Algorithm = "ED25519",
                Status = "Active",
                Owner = "user-4",
                Tenant = "tenant-1"
            });

        var doc = await _resolver.ResolveAsync("did:sorcha:w:bad-key-addr");

        doc.Should().NotBeNull();
        // Neither base64 nor hex decodes, so multibase falls back rather than emit garbage.
        doc!.VerificationMethod[0].PublicKeyMultibase.Should().BeNull();
        doc.VerificationMethod[0].PublicKeyJwk.Should().NotBeNull();
    }
}
