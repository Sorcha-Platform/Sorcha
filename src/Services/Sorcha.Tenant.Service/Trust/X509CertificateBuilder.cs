// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Builds X.509 certificates using the .NET BCL <see cref="CertificateRequest"/> API.
/// Currently supports ES256 (P-256) for root CA and org certificates.
/// EdDSA (Ed25519) support deferred — BCL CertificateRequest requires an asymmetric algorithm adapter.
/// </summary>
public static class X509CertificateBuilder
{
    /// <summary>
    /// Builds a self-signed root CA certificate.
    /// </summary>
    /// <param name="algorithm">Signing algorithm ("ES256" or "EdDSA").</param>
    /// <param name="subjectDn">X.500 subject distinguished name (e.g., "CN=Sorcha Tenant Root CA").</param>
    /// <param name="validityYears">Certificate validity period in years.</param>
    /// <returns>Tuple of (DER-encoded certificate bytes, private key bytes, serial number hex).</returns>
    public static (byte[] CertificateDer, byte[] PrivateKey, string SerialNumber) BuildSelfSignedRoot(
        string algorithm,
        string subjectDn,
        int validityYears = 10)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectDn);
        if (validityYears < 1) throw new ArgumentOutOfRangeException(nameof(validityYears));

        var alg = algorithm.ToUpperInvariant();

        if (alg is "ES256" or "P-256" or "P256")
        {
            return BuildSelfSignedRootP256(subjectDn, validityYears);
        }

        throw new NotSupportedException(
            $"Unsupported CA algorithm: {algorithm}. Supported: ES256.");
    }

    /// <summary>
    /// Builds an organisation certificate signed by the root CA.
    /// </summary>
    /// <param name="rootCertDer">DER-encoded root CA certificate.</param>
    /// <param name="rootPrivateKey">Root CA private key bytes.</param>
    /// <param name="orgPublicKey">Organisation's public key (SPKI-encoded).</param>
    /// <param name="subjectDn">X.500 subject distinguished name.</param>
    /// <param name="sanUri">Subject Alternative Name URI (e.g., "did:sorcha:org:ws1q...").</param>
    /// <param name="crlDistributionPoint">URL of the CRL endpoint.</param>
    /// <param name="validityYears">Certificate validity period in years.</param>
    /// <returns>Tuple of (DER-encoded certificate bytes, serial number hex).</returns>
    public static (byte[] CertificateDer, string SerialNumber) BuildOrgCert(
        byte[] rootCertDer,
        byte[] rootPrivateKey,
        byte[] orgPublicKey,
        string subjectDn,
        string sanUri,
        string? crlDistributionPoint = null,
        int validityYears = 3)
    {
        ArgumentNullException.ThrowIfNull(rootCertDer);
        ArgumentNullException.ThrowIfNull(rootPrivateKey);
        ArgumentNullException.ThrowIfNull(orgPublicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectDn);
        ArgumentException.ThrowIfNullOrWhiteSpace(sanUri);

        using var rootCert = X509CertificateLoader.LoadCertificate(rootCertDer);

        // Extract root CA private key for signing
        using var rootEcdsa = ECDsa.Create();
        rootEcdsa.ImportECPrivateKey(rootPrivateKey, out _);

        // Import the org's public key
        using var orgEcdsa = ECDsa.Create();
        orgEcdsa.ImportSubjectPublicKeyInfo(orgPublicKey, out _);

        var request = new CertificateRequest(
            new X500DistinguishedName(subjectDn),
            orgEcdsa,
            HashAlgorithmName.SHA256);

        // Basic constraints: NOT a CA
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        // Key usage: Digital Signature only
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));

        // Subject Alternative Name with URI
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddUri(new Uri(sanUri));
        request.CertificateExtensions.Add(sanBuilder.Build());

        // Subject Key Identifier
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        // CRL Distribution Points (RFC 5280 §4.2.1.13). Strict X.509 validators
        // require this extension to perform revocation checks — without it the
        // chain walker has no way to locate the CRL, which silently downgrades
        // the revocation guarantee to "no check".
        if (!string.IsNullOrWhiteSpace(crlDistributionPoint))
        {
            request.CertificateExtensions.Add(
                CertificateRevocationListBuilder.BuildCrlDistributionPointExtension(
                    new[] { crlDistributionPoint }));
        }

        // Serial number
        var serialBytes = RandomNumberGenerator.GetBytes(16);
        serialBytes[0] &= 0x7F; // Ensure positive

        var now = DateTimeOffset.UtcNow;
        using var signedCert = request.Create(
            rootCert.SubjectName,
            X509SignatureGenerator.CreateForECDsa(rootEcdsa),
            now,
            now.AddYears(validityYears),
            serialBytes);

        var serialHex = Convert.ToHexString(serialBytes);
        return (signedCert.RawData, serialHex);
    }

    private static (byte[] CertificateDer, byte[] PrivateKey, string SerialNumber) BuildSelfSignedRootP256(
        string subjectDn,
        int validityYears)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var request = new CertificateRequest(
            new X500DistinguishedName(subjectDn),
            ecdsa,
            HashAlgorithmName.SHA256);

        // Basic constraints: IS a CA
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, true, 1, true));

        // Key usage: Certificate Signing, CRL Signing
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                true));

        // Subject Key Identifier
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var now = DateTimeOffset.UtcNow;
        using var cert = request.CreateSelfSigned(now, now.AddYears(validityYears));

        var privateKey = ecdsa.ExportECPrivateKey();
        var serialHex = cert.SerialNumber;

        return (cert.RawData, privateKey, serialHex);
    }
}
