// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Trust;

/// <summary>
/// Feature 181 US5 (T051 / SC-006) — the internal-cert enrol lifecycle end-to-end over the REAL
/// <see cref="InternalCaTrustProvider"/> + <see cref="OrgCertificateService"/>: a P-256-resolvable org
/// reaches an Active internal certificate with zero manual steps, re-enrol is idempotent, a key change
/// supersedes with history, and a non-P-256 org is typed-ineligible everywhere — never an ASN.1 500.
/// </summary>
public class CertLifecycleTests
{
    private const string Tenant = "org-1";
    private const string Org = "ws1qorg";

    private readonly InMemoryCertificateStore _store = new();
    private readonly Mock<IWalletServiceClient> _wallet = new();
    private readonly OrgCertificateService _service;
    private readonly ECDsa _orgKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public CertLifecycleTests()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Trust:DefaultCaAlgorithm"] = "ES256",
            ["Trust:BaseUrl"] = "https://test.example/api/v1/trust",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<ICertificateStore>(_store);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var provider = new InternalCaTrustProvider(
            Mock.Of<ILogger<InternalCaTrustProvider>>(), config, scopeFactory, new PassThroughSecretProtection());

        _service = new OrgCertificateService(_store, _wallet.Object, provider, Mock.Of<ILogger<OrgCertificateService>>());

        SetupResolve("HaipCoKey", _orgKey.ExportSubjectPublicKeyInfo());
    }

    private void SetupResolve(string boundKeySource, byte[]? spki)
        => _wallet.Setup(w => w.ResolveIssuerCertKeyAsync(Org, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrgIssuerCertKeyResolution(spki is not null, spki is null ? "CERT_KEY_NOT_ELIGIBLE" : null,
                spki is null ? null : boundKeySource, spki));

    [Fact]
    public async Task EnrolInternal_EligibleOrg_IssuesActiveInternalCert_BoundToResolvedKey()
    {
        var result = await _service.EnrolInternalAsync(Tenant, Org, "Acme", Guid.Empty);

        result.Success.Should().BeTrue();
        result.Reissued.Should().BeFalse();
        result.Certificate!.Provenance.Should().Be(OrgCertificateProvenance.Internal);
        result.Certificate.Status.Should().Be(OrgCertificateStatus.Active);
        result.Certificate.BoundKeySource.Should().Be(OrgCertificateKeySource.HaipCoKey);
        result.Certificate.BoundPublicKeySpki.Should().Equal(_orgKey.ExportSubjectPublicKeyInfo());

        // The leaf really is signed by the provisioned tenant root and binds the org key.
        using var leaf = X509CertificateLoader.LoadCertificate(result.Certificate.CertificateDer);
        leaf.PublicKey.ExportSubjectPublicKeyInfo().Should().Equal(_orgKey.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public async Task EnrolInternal_Twice_SameKey_IsIdempotent()
    {
        await _service.EnrolInternalAsync(Tenant, Org, "Acme", Guid.Empty);
        var second = await _service.EnrolInternalAsync(Tenant, Org, "Acme", Guid.Empty);

        second.Success.Should().BeTrue();
        second.Reissued.Should().BeFalse("the same key already has an Active internal cert");
        (await _store.ListForOrgAsync(Tenant, Org)).Count(c => c.Status == OrgCertificateStatus.Active).Should().Be(1);
    }

    [Fact]
    public async Task EnrolInternal_AfterKeyChange_Supersedes_WithHistory()
    {
        await _service.EnrolInternalAsync(Tenant, Org, "Acme", Guid.Empty);

        using var rotated = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        SetupResolve("HaipCoKey", rotated.ExportSubjectPublicKeyInfo());

        var reissue = await _service.EnrolInternalAsync(Tenant, Org, "Acme", Guid.Empty);
        reissue.Success.Should().BeTrue();
        reissue.Reissued.Should().BeTrue();

        var all = await _store.ListForOrgAsync(Tenant, Org);
        all.Count(c => c.Status == OrgCertificateStatus.Active).Should().Be(1);
        all.Count(c => c.Status == OrgCertificateStatus.Superseded).Should().Be(1);
    }

    [Fact]
    public async Task EnrolInternal_NonP256Org_IsTypedIneligible_NoAsn1Crash()
    {
        SetupResolve("", null); // no P-256 key resolves (e.g. an Ed25519 org with no derivable co-key)

        var result = await _service.EnrolInternalAsync(Tenant, Org, "Acme", Guid.Empty);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(CertErrorCodes.KeyNotEligible);
        (await _service.GetEligibilityAsync(Tenant, Org)).Eligible.Should().BeFalse();
    }

    private sealed class PassThroughSecretProtection : ISecretProtectionProvider
    {
        public string ProviderName => "TestPassThrough";
        public Task<(byte[] Ciphertext, string KeyId)> EncryptAsync(byte[] plaintext, CancellationToken ct = default)
            => Task.FromResult((plaintext, "k"));
        public Task<byte[]> DecryptAsync(byte[] ciphertext, string keyId, CancellationToken ct = default)
            => Task.FromResult(ciphertext);
    }
}
