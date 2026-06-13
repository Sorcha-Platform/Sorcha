// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Trust;

/// <summary>
/// Tests for InternalCaTrustProvider — provisioning, enrolment, idempotency.
/// </summary>
public class InternalCaTrustProviderTests
{
    private readonly InternalCaTrustProvider _provider;

    public InternalCaTrustProviderTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trust:DefaultCaAlgorithm"] = "ES256",
                ["Trust:DefaultCaValidityYears"] = "10",
                ["Trust:DefaultOrgCertValidityYears"] = "3",
                ["Trust:BaseUrl"] = "https://test.example/api/v1/trust"
            })
            .Build();

        _provider = new InternalCaTrustProvider(
            Mock.Of<ILogger<InternalCaTrustProvider>>(),
            config);
    }

    [Fact]
    public async Task Provision_CreatesNewRootCa()
    {
        var root = await _provider.ProvisionTrustAnchorAsync("tenant-1");

        root.Should().NotBeNull();
        root.TenantId.Should().Be("tenant-1");
        root.CertificateDer.Should().NotBeEmpty();
        root.SerialNumber.Should().NotBeNullOrWhiteSpace();
        root.Algorithm.Should().Be("ES256");
        root.NotAfter.Should().BeAfter(DateTimeOffset.UtcNow.AddYears(9));
    }

    [Fact]
    public async Task Provision_IsIdempotent_ReturnsExistingRoot()
    {
        var root1 = await _provider.ProvisionTrustAnchorAsync("tenant-1");
        var root2 = await _provider.ProvisionTrustAnchorAsync("tenant-1");

        root1.Id.Should().Be(root2.Id);
        root1.SerialNumber.Should().Be(root2.SerialNumber);
    }

    [Fact]
    public async Task GetTrustAnchor_NotProvisioned_ReturnsNull()
    {
        var root = await _provider.GetTrustAnchorAsync("nonexistent");
        root.Should().BeNull();
    }

    [Fact]
    public async Task IssueOrgCert_WithProvisionedRoot_Succeeds()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");

        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        var enrolment = await _provider.IssueOrgCertAsync(
            "tenant-1", "ws1qorg123", orgPublicKey, "Test Organisation");

        enrolment.Should().NotBeNull();
        enrolment.OrgWalletAddress.Should().Be("ws1qorg123");
        enrolment.SanUri.Should().Be("did:sorcha:org:ws1qorg123");
        enrolment.CertificateDer.Should().NotBeEmpty();
    }

    [Fact]
    public async Task IssueOrgCert_IsIdempotent_ReturnsExistingCert()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");

        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        var cert1 = await _provider.IssueOrgCertAsync(
            "tenant-1", "ws1qorg123", orgPublicKey, "Test Organisation");
        var cert2 = await _provider.IssueOrgCertAsync(
            "tenant-1", "ws1qorg123", orgPublicKey, "Test Organisation");

        cert1.Id.Should().Be(cert2.Id);
        cert1.SerialNumber.Should().Be(cert2.SerialNumber);
    }

    [Fact]
    public async Task IssueOrgCert_WithoutProvisionedRoot_Throws()
    {
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        var act = () => _provider.IssueOrgCertAsync(
            "tenant-1", "ws1qorg123", orgPublicKey, "Test Organisation");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no provisioned trust anchor*");
    }

    // -----------------------------------------------------------------------
    // Feature 096 US4 — CRL + revocation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetOrPublishCrl_BeforeProvisioning_ReturnsNull()
    {
        var crl = await _provider.GetOrPublishCrlAsync("never-provisioned");
        crl.Should().BeNull("cannot sign a CRL without a root CA");
    }

    [Fact]
    public async Task GetOrPublishCrl_AfterProvision_ReturnsSignedEmptyCrl()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");

        var crl = await _provider.GetOrPublishCrlAsync("tenant-1");

        crl.Should().NotBeNull();
        crl!.CrlDer.Should().NotBeEmpty();
        crl.Version.Should().Be(1);
        crl.NextUpdate.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetOrPublishCrl_SecondCallWithinTtl_ReturnsCached()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");
        var first = await _provider.GetOrPublishCrlAsync("tenant-1");
        var second = await _provider.GetOrPublishCrlAsync("tenant-1");

        first!.Version.Should().Be(second!.Version,
            "CRL must be cached until nextUpdate — no unnecessary regeneration");
        first.LastUpdated.Should().Be(second.LastUpdated);
    }

    [Fact]
    public async Task RevokeOrgCert_MarksRevoked_AndCrlVersionIncrements()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();
        var enrolment = await _provider.IssueOrgCertAsync(
            "tenant-1", "ws1qorg123", orgPublicKey, "Test Organisation");
        var crlBefore = await _provider.GetOrPublishCrlAsync("tenant-1");

        var revoked = await _provider.RevokeOrgCertAsync(
            "tenant-1", "ws1qorg123", reason: "keyCompromise");

        revoked.RevokedAt.Should().NotBeNull();
        revoked.RevocationReason.Should().Be("keyCompromise");

        var crlAfter = await _provider.GetOrPublishCrlAsync("tenant-1");
        crlAfter!.Version.Should().Be(crlBefore!.Version + 1,
            "revocation must bump the CRL number so strict verifiers detect the update");
    }

    [Fact]
    public async Task RevokeOrgCert_Idempotent_SecondCallNoOp()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();
        await _provider.IssueOrgCertAsync("tenant-1", "ws1qorg123", orgPublicKey, "Test Org");

        var first = await _provider.RevokeOrgCertAsync("tenant-1", "ws1qorg123", "keyCompromise");
        var second = await _provider.RevokeOrgCertAsync("tenant-1", "ws1qorg123", "different-reason");

        second.RevokedAt.Should().Be(first.RevokedAt, "second revoke must not reset the timestamp");
        second.RevocationReason.Should().Be("keyCompromise", "reason is frozen on first revoke");
    }

    [Fact]
    public async Task RevokeOrgCert_NoEnrolment_Throws()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");

        var act = () => _provider.RevokeOrgCertAsync("tenant-1", "never-enrolled");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrgCertChain_AfterRevoke_ReturnsNull()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();
        await _provider.IssueOrgCertAsync("tenant-1", "ws1qorg123", orgPublicKey, "Test Org");

        await _provider.RevokeOrgCertAsync("tenant-1", "ws1qorg123");

        var chain = await _provider.GetOrgCertChainAsync("tenant-1", "ws1qorg123");
        chain.Should().BeNull("revoked certs MUST NOT be served to the Wallet Service for new credentials");
    }

    [Fact]
    public async Task GetOrPublishCrl_AfterRevocation_IncludesSerialNumber()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();
        var enrolment = await _provider.IssueOrgCertAsync(
            "tenant-1", "ws1qorg123", orgPublicKey, "Test Org");

        await _provider.RevokeOrgCertAsync("tenant-1", "ws1qorg123");
        var crl = await _provider.GetOrPublishCrlAsync("tenant-1");

        // Decode the CRL and confirm it carries the revoked serial.
        crl.Should().NotBeNull();
        var decoded = new System.Security.Cryptography.X509Certificates.CertificateRevocationListBuilder();
        var parsed = System.Security.Cryptography.X509Certificates.CertificateRevocationListBuilder
            .Load(crl!.CrlDer, out var currentCrlNumber);
        parsed.Should().NotBeNull();
        currentCrlNumber.Should().Be(crl.Version);

        // The revoked serial must appear in the signed CRL bytes. Compare against the serial's
        // CANONICAL DER INTEGER content (two's-complement big-endian), not the raw hex bytes:
        // DER prepends a 0x00 sign byte when the leading bit is set and strips non-canonical
        // leading zeros, so a raw-byte scan is non-deterministic across random serials (the source
        // of an intermittent flake). Normalising to the DER INTEGER content makes this exact.
        var serial = new BigInteger(Convert.FromHexString(enrolment.SerialNumber), isUnsigned: true, isBigEndian: true);
        var derSerial = serial.ToByteArray(isUnsigned: false, isBigEndian: true);
        IndexOfSequence(crl.CrlDer, derSerial).Should().BeGreaterThanOrEqualTo(0,
            "revoked serial number must be present in the signed CRL bytes");
    }

    private static int IndexOfSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    [Fact]
    public async Task GetOrgCertChain_AfterEnrolment_ReturnsBothCerts()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");

        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        await _provider.IssueOrgCertAsync("tenant-1", "ws1qorg123", orgPublicKey, "Test Org");

        var chain = await _provider.GetOrgCertChainAsync("tenant-1", "ws1qorg123");

        chain.Should().NotBeNull();
        chain!.Value.OrgCertDer.Should().NotBeEmpty();
        chain.Value.RootCertDer.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetOrgCertChain_NotEnrolled_ReturnsNull()
    {
        await _provider.ProvisionTrustAnchorAsync("tenant-1");

        var chain = await _provider.GetOrgCertChainAsync("tenant-1", "ws1qnonexistent");
        chain.Should().BeNull();
    }
}
