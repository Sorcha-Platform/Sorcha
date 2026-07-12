// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using FluentAssertions;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Trust;

/// <summary>
/// Feature 181 US5 (T047 / FR-024) — the ASN.1-500 elimination: <see cref="X509CertificateBuilder.BuildOrgCert"/>
/// surfaces a TYPED <see cref="CertKeyNotEligibleException"/> for a non-P-256 org key rather than leaking a
/// raw <see cref="CryptographicException"/> ("ASN1 corrupted data") as an unhandled 500.
/// </summary>
public class CertEligibilityTests
{
    private static (byte[] RootDer, byte[] RootKey) NewRoot()
    {
        var (der, key, _) = X509CertificateBuilder.BuildSelfSignedRoot("ES256", "CN=Test Root", 10);
        return (der, key);
    }

    [Fact]
    public void BuildOrgCert_WithEd25519Spki_ThrowsTyped_NotRawCryptographicException()
    {
        var (rootDer, rootKey) = NewRoot();

        // An Ed25519 SPKI is not a P-256 key — ImportSubjectPublicKeyInfo would throw "ASN1 corrupted data".
        var ed25519Spki = BuildEd25519Spki();

        var act = () => X509CertificateBuilder.BuildOrgCert(
            rootDer, rootKey, ed25519Spki, "CN=Org", "did:sorcha:org:ws1qx", null, 3);

        act.Should().Throw<CertKeyNotEligibleException>()
            .Which.Message.Should().Contain("P-256");
    }

    [Fact]
    public void BuildOrgCert_WithGarbageKey_ThrowsTyped()
    {
        var (rootDer, rootKey) = NewRoot();
        var act = () => X509CertificateBuilder.BuildOrgCert(
            rootDer, rootKey, new byte[] { 1, 2, 3, 4 }, "CN=Org", "did:sorcha:org:ws1qx", null, 3);

        act.Should().Throw<CertKeyNotEligibleException>();
    }

    [Fact]
    public void BuildOrgCert_WithValidP256Spki_Succeeds()
    {
        var (rootDer, rootKey) = NewRoot();
        using var org = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var (certDer, serial) = X509CertificateBuilder.BuildOrgCert(
            rootDer, rootKey, org.ExportSubjectPublicKeyInfo(), "CN=Org", "did:sorcha:org:ws1qx", null, 3);

        certDer.Should().NotBeEmpty();
        serial.Should().NotBeNullOrWhiteSpace();
    }

    // A minimal Ed25519 SubjectPublicKeyInfo (RFC 8410 OID 1.3.101.112) with a zero 32-byte key.
    private static byte[] BuildEd25519Spki()
    {
        var writer = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier("1.3.101.112");
            }
            writer.WriteBitString(new byte[32]);
        }
        return writer.Encode();
    }
}
