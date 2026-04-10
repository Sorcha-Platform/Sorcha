// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Trust;

/// <summary>
/// Tests for X509CertificateBuilder — self-signed root CA and org cert generation.
/// </summary>
public class X509CertificateBuilderTests
{
    [Fact]
    public void BuildSelfSignedRoot_Es256_ValidCertDerOutput()
    {
        var (certDer, privateKey, serialNumber) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA, O=Sorcha, C=IE");

        certDer.Should().NotBeEmpty();
        privateKey.Should().NotBeEmpty();
        serialNumber.Should().NotBeNullOrWhiteSpace();

        // Parse the DER to validate it's a real X.509 cert
        using var cert = X509CertificateLoader.LoadCertificate(certDer);
        cert.Subject.Should().Contain("CN=Test Root CA");
        cert.Issuer.Should().Contain("CN=Test Root CA"); // self-signed
    }

    [Fact]
    public void BuildSelfSignedRoot_ValidityPeriod_RespectsInput()
    {
        var (certDer, _, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA", validityYears: 5);

        using var cert = X509CertificateLoader.LoadCertificate(certDer);
        var expectedExpiry = DateTimeOffset.UtcNow.AddYears(5);

        // Allow 5 minutes tolerance for UTC/local time conversion and test execution time
        cert.NotAfter.ToUniversalTime().Should().BeCloseTo(expectedExpiry.UtcDateTime, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void BuildSelfSignedRoot_HasCaBasicConstraints()
    {
        var (certDer, _, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA");

        using var cert = X509CertificateLoader.LoadCertificate(certDer);

        var basicConstraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        basicConstraints.Should().NotBeNull();
        basicConstraints!.CertificateAuthority.Should().BeTrue();
    }

    [Fact]
    public void BuildSelfSignedRoot_HasKeyUsageCertSignAndCrlSign()
    {
        var (certDer, _, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA");

        using var cert = X509CertificateLoader.LoadCertificate(certDer);

        var keyUsage = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        keyUsage.Should().NotBeNull();
        keyUsage!.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.KeyCertSign);
        keyUsage.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.CrlSign);
    }

    [Fact]
    public void BuildSelfSignedRoot_UnsupportedAlgorithm_Throws()
    {
        var act = () => X509CertificateBuilder.BuildSelfSignedRoot(
            "ML-DSA-65", "CN=Test Root CA");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Unsupported CA algorithm*");
    }

    [Fact]
    public void BuildOrgCert_SignedByRootCa()
    {
        // Create root CA
        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA");

        // Generate an org key
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        // Issue org cert
        var (orgCertDer, orgSerial) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, orgPublicKey,
            "CN=Test Org, O=Sorcha Org, C=IE",
            "did:sorcha:org:ws1qtest123");

        orgCertDer.Should().NotBeEmpty();
        orgSerial.Should().NotBeNullOrWhiteSpace();

        using var orgCert = X509CertificateLoader.LoadCertificate(orgCertDer);
        orgCert.Subject.Should().Contain("CN=Test Org");
        orgCert.Issuer.Should().Contain("CN=Test Root CA"); // signed by root
    }

    [Fact]
    public void BuildOrgCert_IsNotCA()
    {
        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA");
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, orgPublicKey,
            "CN=Test Org", "did:sorcha:org:ws1qtest123");

        using var orgCert = X509CertificateLoader.LoadCertificate(orgCertDer);
        var bc = orgCert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        bc.Should().NotBeNull();
        bc!.CertificateAuthority.Should().BeFalse();
    }

    [Fact]
    public void BuildOrgCert_EmbedsSanUri_WithDidSorchaOrgPrefix()
    {
        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA");
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, orgPublicKey,
            "CN=Test Org", "did:sorcha:org:ws1qtest123");

        using var orgCert = X509CertificateLoader.LoadCertificate(orgCertDer);

        // Check SAN extension exists and contains the DID URI
        var san = orgCert.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        san.Should().NotBeNull("org cert must have SAN extension");
        // The SAN extension's formatted string should contain the DID URI
        var sanFormatted = san!.Format(true);
        sanFormatted.Should().Contain("did:sorcha:org:ws1qtest123",
            because: "SAN URI should contain the org's DID");
    }

    [Fact]
    public void BuildOrgCert_ChainVerifiesAgainstRoot()
    {
        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA");
        using var orgEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var orgPublicKey = orgEcdsa.ExportSubjectPublicKeyInfo();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, orgPublicKey,
            "CN=Test Org", "did:sorcha:org:ws1qtest123");

        using var rootCert = X509CertificateLoader.LoadCertificate(rootCertDer);
        using var orgCert = X509CertificateLoader.LoadCertificate(orgCertDer);

        // Build a chain with the root as trusted
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(rootCert);

        var isValid = chain.Build(orgCert);
        isValid.Should().BeTrue("org cert should verify against the root CA");
    }
}
