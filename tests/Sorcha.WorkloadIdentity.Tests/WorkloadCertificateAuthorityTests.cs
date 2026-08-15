// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Sorcha.WorkloadIdentity;

namespace Sorcha.WorkloadIdentity.Tests;

public class WorkloadCertificateAuthorityTests
{
    private const string Oid_BasicConstraints = "2.5.29.19";
    private const string Oid_KeyUsage = "2.5.29.15";
    private const string Oid_Eku = "2.5.29.37";
    private const string Oid_San = "2.5.29.17";
    private const string Oid_ClientAuth = "1.3.6.1.5.5.7.3.2";
    private const string Oid_ServerAuth = "1.3.6.1.5.5.7.3.1";

    [Fact]
    public void CreateCertificateAuthority_IsSelfSignedP256CaWithConstrainedPathLength()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");

        ca.Subject.Should().Be(ca.Issuer, "the CA is self-signed");
        ca.Subject.Should().Contain("sorcha");
        ca.HasPrivateKey.Should().BeTrue();
        ca.GetECDsaPublicKey().Should().NotBeNull("the workload rail is EC only");
        ca.GetECDsaPublicKey()!.KeySize.Should().Be(256);

        var basicConstraints = ca.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        basicConstraints.CertificateAuthority.Should().BeTrue();
        basicConstraints.HasPathLengthConstraint.Should().BeTrue();
        basicConstraints.PathLengthConstraint.Should().Be(0, "the CA signs leaves only, never intermediates");
        basicConstraints.Critical.Should().BeTrue();

        var keyUsage = ca.Extensions.OfType<X509KeyUsageExtension>().Single();
        keyUsage.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.KeyCertSign);
    }

    [Fact]
    public void CreateCertificateAuthority_DefaultLifetimeIsFiveYears()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");

        var lifetime = ca.NotAfter - ca.NotBefore;
        lifetime.Should().BeCloseTo(TimeSpan.FromDays(365 * 5), TimeSpan.FromDays(3));
        // Clock-skew allowance: the certificate must already be valid at issue time.
        // NB X509Certificate2.NotBefore is LOCAL time — normalise before comparing.
        ca.NotBefore.ToUniversalTime().Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void IssueServiceCertificate_CarriesSpiffeUriAndDnsSans()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        var id = SpiffeId.ForService("sorcha", "service-blueprint");

        using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(ca, id, "blueprint-service");

        leaf.Issuer.Should().Be(ca.Subject);
        leaf.HasPrivateKey.Should().BeTrue();
        leaf.GetECDsaPublicKey().Should().NotBeNull();

        var sanExtension = leaf.Extensions[Oid_San];
        sanExtension.Should().NotBeNull("the leaf must carry a SAN extension");
        var sanText = sanExtension!.Format(false);
        sanText.Should().Contain("spiffe://sorcha/service/service-blueprint");
        sanText.Should().Contain("blueprint-service");

        var basicConstraints = leaf.Extensions.OfType<X509BasicConstraintsExtension>().Single();
        basicConstraints.CertificateAuthority.Should().BeFalse();

        var keyUsage = leaf.Extensions.OfType<X509KeyUsageExtension>().Single();
        keyUsage.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.DigitalSignature);

        var eku = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        eku.EnhancedKeyUsages.OfType<Oid>().Select(o => o.Value)
            .Should().Contain(Oid_ClientAuth, "workload leaves authenticate as TLS clients");
    }

    [Fact]
    public void IssueServiceCertificate_DefaultLifetimeIsTwoYears()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        var id = SpiffeId.ForService("sorcha", "service-wallet");

        using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(ca, id, "wallet-service");

        (leaf.NotAfter - leaf.NotBefore).Should().BeCloseTo(TimeSpan.FromDays(365 * 2), TimeSpan.FromDays(3));
        leaf.NotBefore.ToUniversalTime().Should().BeBefore(DateTime.UtcNow);
    }

    [Fact]
    public void IssueServiceCertificate_SerialsAreUnique()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        var id = SpiffeId.ForService("sorcha", "service-peer");

        using var first = WorkloadCertificateAuthority.IssueServiceCertificate(ca, id, "peer-service");
        using var second = WorkloadCertificateAuthority.IssueServiceCertificate(ca, id, "peer-service");

        second.SerialNumber.Should().NotBe(first.SerialNumber);
        // Fresh keypair per issuance (renew semantics): public keys must differ too.
        second.GetPublicKeyString().Should().NotBe(first.GetPublicKeyString());
    }

    [Fact]
    public void IssueServiceCertificate_CarriesAuthorityKeyIdentifierMatchingIssuer()
    {
        // AKI→SKI binding is what lets OpenSSL chain-building pick the RIGHT root when the old
        // and new CA share a subject DN during rotate-ca overlap (Linux-only failure without it).
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(
            ca, SpiffeId.ForService("sorcha", "service-blueprint"), "blueprint-service");

        var caSki = ca.Extensions.OfType<X509SubjectKeyIdentifierExtension>().Single().SubjectKeyIdentifier;
        var leafAki = leaf.Extensions.OfType<X509AuthorityKeyIdentifierExtension>().Single();
        Convert.ToHexString(leafAki.KeyIdentifier!.Value.Span).Should().Be(caSki);
    }

    [Fact]
    public void IssueServerCertificate_CarriesDnsSanAndServerAuthEku()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");

        using var server = WorkloadCertificateAuthority.IssueServerCertificate(ca, "tenant-service");

        server.Issuer.Should().Be(ca.Subject);
        server.HasPrivateKey.Should().BeTrue();
        server.Extensions[Oid_San]!.Format(false).Should().Contain("tenant-service");

        var eku = server.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single();
        eku.EnhancedKeyUsages.OfType<Oid>().Select(o => o.Value)
            .Should().Contain(Oid_ServerAuth, "the mTLS listener presents this certificate as a TLS server");
    }

    [Fact]
    public void ExportPfx_RoundTripsWithPasswordAndPrivateKey()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        var id = SpiffeId.ForService("sorcha", "tenant-service");
        using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(ca, id, "tenant-service");

        var pfxBytes = leaf.Export(X509ContentType.Pfx, "test-password");
        using var reloaded = X509CertificateLoader.LoadPkcs12(pfxBytes, "test-password",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet);

        reloaded.HasPrivateKey.Should().BeTrue();
        reloaded.Thumbprint.Should().Be(leaf.Thumbprint);
    }

    [Fact]
    public void TryGetSpiffeId_ReadsBackTheIssuedIdentity()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        var id = SpiffeId.ForService("sorcha", "service-haip");
        using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(ca, id, "haip-service");

        WorkloadCertificateAuthority.TryGetSpiffeId(leaf, out var extracted).Should().BeTrue();
        extracted.Should().Be(id);
    }

    [Fact]
    public void TryGetSpiffeId_FalseWhenNoUriSan()
    {
        using var ca = WorkloadCertificateAuthority.CreateCertificateAuthority("sorcha");
        using var server = WorkloadCertificateAuthority.IssueServerCertificate(ca, "tenant-service");

        WorkloadCertificateAuthority.TryGetSpiffeId(server, out var extracted).Should().BeFalse();
        extracted.Should().BeNull();
    }
}
