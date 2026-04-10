// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
    public async Task IssueOrgCert_WithoutProvisionedRoot_Throws()
    {
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        var act = () => _provider.IssueOrgCertAsync(
            "tenant-1", "ws1qorg123", orgPublicKey, "Test Organisation");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no provisioned trust anchor*");
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
