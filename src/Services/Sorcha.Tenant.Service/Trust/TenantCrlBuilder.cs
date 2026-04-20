// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Builds signed X.509 Certificate Revocation Lists (RFC 5280 §5) for a tenant
/// using the BCL <see cref="CertificateRevocationListBuilder"/>. Callers supply
/// the root CA certificate bytes + private key and the set of revoked org-cert
/// serial numbers; this class handles the DER encoding and signing.
/// </summary>
public static class TenantCrlBuilder
{
    /// <summary>
    /// Builds a signed CRL containing the supplied revoked entries. The CRL is
    /// signed by the tenant root CA and must be refreshed periodically —
    /// <paramref name="validityHours"/> sets the <c>nextUpdate</c> field so
    /// strict verifiers know when the CRL is stale. A CRL with all-zero entries
    /// is a legitimate "no revocations" publication and must still be served.
    /// </summary>
    /// <param name="rootCertDer">DER-encoded tenant root CA certificate.</param>
    /// <param name="rootPrivateKey">Root CA private key (currently ECDSA P-256).</param>
    /// <param name="revokedEntries">Serial-hex + revocation time pairs.</param>
    /// <param name="crlNumber">Monotonic CRL version — must strictly increase across publications.</param>
    /// <param name="validityHours">Hours until <c>nextUpdate</c>. Default 24.</param>
    /// <param name="algorithm">Signing algorithm identifier from the tenant trust config (e.g. "ES256"). Only ES256 is supported today.</param>
    /// <returns>DER-encoded CRL bytes plus the effective <c>nextUpdate</c>.</returns>
    /// <exception cref="NotSupportedException">Thrown when <paramref name="algorithm"/> is not ES256 — RSA and EdDSA are deferred until keys are managed under PKCS#8 with an explicit discriminator.</exception>
    public static (byte[] CrlDer, DateTimeOffset NextUpdate) Build(
        byte[] rootCertDer,
        byte[] rootPrivateKey,
        IEnumerable<(string SerialHex, DateTimeOffset RevokedAt)> revokedEntries,
        long crlNumber,
        int validityHours = 24,
        string algorithm = "ES256")
    {
        ArgumentNullException.ThrowIfNull(rootCertDer);
        ArgumentNullException.ThrowIfNull(rootPrivateKey);
        ArgumentNullException.ThrowIfNull(revokedEntries);
        if (validityHours < 1)
            throw new ArgumentOutOfRangeException(nameof(validityHours));

        // Guard the latent runtime crash flagged on PR #316: raw ImportECPrivateKey
        // silently throws CryptographicException when a tenant CA is provisioned with
        // RSA (or a future EdDSA). Fail loudly at the callsite instead so operators
        // see a clear "not supported" error at config time, not a cryptic stack later.
        if (!string.Equals(algorithm, "ES256", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"CRL signing for algorithm '{algorithm}' is not yet implemented. " +
                "Only ES256 (NIST P-256) is supported today; see spec 096 persistent-storage follow-up for RSA / EdDSA.");
        }

        using var rootCert = X509CertificateLoader.LoadCertificate(rootCertDer);
        using var rootEcdsa = ECDsa.Create();
        rootEcdsa.ImportECPrivateKey(rootPrivateKey, out _);

        var builder = new CertificateRevocationListBuilder();
        foreach (var (serialHex, revokedAt) in revokedEntries)
        {
            if (string.IsNullOrWhiteSpace(serialHex))
                continue;
            var serialBytes = Convert.FromHexString(serialHex);
            // The builder takes serial bytes in big-endian form — the same order
            // X509CertificateBuilder emits them, so no reversal is needed.
            builder.AddEntry(serialBytes, revokedAt);
        }

        var now = DateTimeOffset.UtcNow;
        var nextUpdate = now.AddHours(validityHours);

        // The CRL must carry an AuthorityKeyIdentifier derived from the root's
        // SubjectKeyIdentifier so verifiers can bind the CRL back to the issuer
        // CA. Fallback to an AKI derived from the public key SPKI when the SKI
        // extension is missing.
        var skiExt = rootCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault();
        var aki = skiExt is not null
            ? X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(skiExt)
            : X509AuthorityKeyIdentifierExtension.CreateFromCertificate(rootCert, includeKeyIdentifier: true, includeIssuerAndSerial: false);

        // HashAlgorithmName.SHA256 matches the P-256 root cert's signing choice.
        var crlDer = builder.Build(
            issuerName: rootCert.SubjectName,
            generator: X509SignatureGenerator.CreateForECDsa(rootEcdsa),
            crlNumber: crlNumber,
            nextUpdate: nextUpdate,
            hashAlgorithm: HashAlgorithmName.SHA256,
            authorityKeyIdentifier: aki);

        return (crlDer, nextUpdate);
    }
}
