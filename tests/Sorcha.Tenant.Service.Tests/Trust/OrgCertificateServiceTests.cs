// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Trust;

/// <summary>
/// Feature 181 US4 (T042 + SC-005) — org-certificate lifecycle: eligibility, CSR, the FR-019 import
/// validation matrix (each failure a distinct code), supersede, chain resolution + fail-closed, and the
/// key-rotation KeyMismatch flag.
/// </summary>
public class OrgCertificateServiceTests
{
    private const string Tenant = "tenant-1";
    private const string Org = "ws1qorg";

    private readonly InMemoryCertificateStore _store = new();
    private readonly Mock<IWalletServiceClient> _wallet = new();
    private readonly Mock<ITrustProvider> _trustProvider = new();
    private readonly OrgCertificateService _service;

    // The org's real P-256 key, exposed so tests can build matching leaves + verify the CSR.
    private readonly ECDsa _orgKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly byte[] _orgSpki;

    public OrgCertificateServiceTests()
    {
        _orgSpki = _orgKey.ExportSubjectPublicKeyInfo();
        var orgD = _orgKey.ExportParameters(true).D!;

        _wallet.Setup(w => w.ResolveIssuerCertKeyAsync(Org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgIssuerCertKeyResolution(true, null, "HaipCoKey", _orgSpki));
        _wallet.Setup(w => w.SignIssuerCertPreHashedAsync(Org, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, byte[] digest, CancellationToken _) =>
            {
                using var s = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = orgD });
                return s.SignHash(digest);
            });

        _service = new OrgCertificateService(
            _store, _wallet.Object, _trustProvider.Object, Mock.Of<ILogger<OrgCertificateService>>());
    }

    // ---- eligibility + CSR ----

    [Fact]
    public async Task Eligibility_WhenP256Resolves_IsEligible()
    {
        var e = await _service.GetEligibilityAsync(Tenant, Org);
        e.Eligible.Should().BeTrue();
        e.BoundKeySource.Should().Be(OrgCertificateKeySource.HaipCoKey);
    }

    [Fact]
    public async Task GenerateCsr_WhenIneligible_ReturnsTypedCode()
    {
        _wallet.Setup(w => w.ResolveIssuerCertKeyAsync(Org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgIssuerCertKeyResolution(false, "CERT_KEY_NOT_ELIGIBLE", null, null));

        var result = await _service.GenerateCsrAsync(Tenant, Org, null, Guid.NewGuid());
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(CertErrorCodes.KeyNotEligible);
    }

    [Fact]
    public async Task GenerateCsr_ProducesValidCsr_BoundToOrgKey()
    {
        var result = await _service.GenerateCsrAsync(Tenant, Org, "CN=Acme", Guid.NewGuid());
        result.Success.Should().BeTrue();
        result.CsrPem.Should().Contain("BEGIN CERTIFICATE REQUEST");

        var csr = CertificateRequest.LoadSigningRequestPem(result.CsrPem!, HashAlgorithmName.SHA256);
        csr.PublicKey.ExportSubjectPublicKeyInfo().Should().Equal(_orgSpki);

        var latest = await _store.GetLatestCsrAsync(Tenant, Org);
        latest.Should().NotBeNull();
    }

    // ---- import validation matrix (FR-019) ----

    [Fact]
    public async Task Import_ValidChainWithRoot_Succeeds()
    {
        var (leaf, root) = IssueLeafForOrgKey(_orgKey);
        var result = await _service.ImportAsync(Tenant, Org, leaf, [root], Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Certificate!.Provenance.Should().Be(OrgCertificateProvenance.Imported);
        result.Certificate.Status.Should().Be(OrgCertificateStatus.Active);
        result.Certificate.ChainDer.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Import_ChainWithoutRoot_Succeeds()
    {
        var (leaf, _) = IssueLeafForOrgKey(_orgKey);
        var result = await _service.ImportAsync(Tenant, Org, leaf, [], Guid.NewGuid());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Import_KeyMismatch_IsTyped()
    {
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var (leaf, root) = IssueLeafForOrgKey(otherKey); // leaf binds a DIFFERENT key
        var result = await _service.ImportAsync(Tenant, Org, leaf, [root], Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(CertErrorCodes.KeyMismatch);
    }

    [Fact]
    public async Task Import_ExpiredLeaf_IsTyped()
    {
        var (leaf, root) = IssueLeafForOrgKey(_orgKey,
            notBefore: DateTimeOffset.UtcNow.AddYears(-2), notAfter: DateTimeOffset.UtcNow.AddYears(-1));
        var result = await _service.ImportAsync(Tenant, Org, leaf, [root], Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(CertErrorCodes.Expired);
    }

    [Fact]
    public async Task Import_KeyUsageWithoutSigning_IsUnsuitable()
    {
        var (leaf, root) = IssueLeafForOrgKey(_orgKey, keyUsage: X509KeyUsageFlags.KeyEncipherment);
        var result = await _service.ImportAsync(Tenant, Org, leaf, [root], Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(CertErrorCodes.Unsuitable);
    }

    [Fact]
    public async Task Import_Twice_SupersedesPrior()
    {
        var (leaf1, root1) = IssueLeafForOrgKey(_orgKey);
        await _service.ImportAsync(Tenant, Org, leaf1, [root1], Guid.NewGuid());
        var (leaf2, root2) = IssueLeafForOrgKey(_orgKey);
        await _service.ImportAsync(Tenant, Org, leaf2, [root2], Guid.NewGuid());

        var all = await _service.ListAsync(Tenant, Org);
        all.Count(c => c.Status == OrgCertificateStatus.Active).Should().Be(1);
        all.Count(c => c.Status == OrgCertificateStatus.Superseded).Should().Be(1);
    }

    // ---- chain resolution + fail-closed (SC-005) ----

    [Fact]
    public async Task ResolveActiveImportedChain_AfterImport_ReturnsLeafFirstChain()
    {
        var (leaf, root) = IssueLeafForOrgKey(_orgKey);
        await _service.ImportAsync(Tenant, Org, leaf, [root], Guid.NewGuid());

        var resolved = await _service.ResolveActiveImportedChainAsync(Tenant, Org);
        resolved.Available.Should().BeTrue();
        resolved.Chain![0].Should().Equal(leaf); // leaf first
    }

    [Fact]
    public async Task ResolveActiveImportedChain_WhenNoImport_FailsClosed()
    {
        var resolved = await _service.ResolveActiveImportedChainAsync(Tenant, Org);
        resolved.Available.Should().BeFalse();
        resolved.ErrorCode.Should().Be(CertErrorCodes.ExternalAnchorUnavailable);
    }

    [Fact]
    public async Task ResolveActiveImportedChain_AfterKeyRotation_FailsClosed_AndFlagsKeyMismatch()
    {
        var (leaf, root) = IssueLeafForOrgKey(_orgKey);
        await _service.ImportAsync(Tenant, Org, leaf, [root], Guid.NewGuid());

        // Org rotates to a new P-256 key.
        using var rotated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _wallet.Setup(w => w.ResolveIssuerCertKeyAsync(Org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgIssuerCertKeyResolution(true, null, "HaipCoKey", rotated.ExportSubjectPublicKeyInfo()));

        var resolved = await _service.ResolveActiveImportedChainAsync(Tenant, Org);
        resolved.Available.Should().BeFalse();
        resolved.ErrorCode.Should().Be(CertErrorCodes.ExternalAnchorUnavailable);

        var stored = await _store.GetByIdAsync(Tenant, Org, (await _store.ListForOrgAsync(Tenant, Org))[0].Id);
        stored!.Status.Should().Be(OrgCertificateStatus.KeyMismatch);
    }

    [Fact]
    public async Task RetireImported_ThenResolve_FailsClosed()
    {
        var (leaf, root) = IssueLeafForOrgKey(_orgKey);
        var imported = await _service.ImportAsync(Tenant, Org, leaf, [root], Guid.NewGuid());
        (await _service.RetireImportedAsync(Tenant, Org, imported.Certificate!.Id)).Should().BeTrue();

        var resolved = await _service.ResolveActiveImportedChainAsync(Tenant, Org);
        resolved.Available.Should().BeFalse();
    }

    // ---- helpers ----

    /// <summary>Build a leaf certificate whose public key is <paramref name="subjectKey"/>, signed by a
    /// freshly-minted test CA root. Returns (leafDer, rootDer).</summary>
    private static (byte[] Leaf, byte[] Root) IssueLeafForOrgKey(
        ECDsa subjectKey,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        X509KeyUsageFlags keyUsage = X509KeyUsageFlags.DigitalSignature)
    {
        using var caKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var caReq = new CertificateRequest("CN=Test External CA", caKey, HashAlgorithmName.SHA256);
        caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        caReq.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = caReq.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-1), DateTimeOffset.UtcNow.AddYears(10));

        using var subjectPublic = ECDsa.Create();
        subjectPublic.ImportSubjectPublicKeyInfo(subjectKey.ExportSubjectPublicKeyInfo(), out _);
        var leafReq = new CertificateRequest("CN=Acme Org Leaf", subjectPublic, HashAlgorithmName.SHA256);
        leafReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        leafReq.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, true));

        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        using var leaf = leafReq.Create(
            root.SubjectName,
            X509SignatureGenerator.CreateForECDsa(caKey),
            notBefore ?? DateTimeOffset.UtcNow.AddMinutes(-5),
            notAfter ?? DateTimeOffset.UtcNow.AddYears(1),
            serial);

        return (leaf.RawData, root.RawData);
    }
}
