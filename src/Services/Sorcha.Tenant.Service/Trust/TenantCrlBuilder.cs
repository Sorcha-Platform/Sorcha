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
            // X509CertificateBuilder emits them, so no reversal is needed. They
            // MUST, however, be a minimal DER INTEGER: org-cert serials are stored
            // as the raw 16-byte RNG buffer, so a leading 0x00 (redundant positive
            // padding) makes AddEntry throw "invalid serial number ... redundant
            // leading bytes", and a leading high-bit byte would be silently encoded
            // as a negative integer that never matches the certificate (#817).
            builder.AddEntry(NormalizeSerial(serialBytes), revokedAt);
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

    /// <summary>
    /// Normalises a big-endian serial number to the minimal, positive DER INTEGER
    /// encoding required by <see cref="CertificateRevocationListBuilder.AddEntry(System.ReadOnlySpan{byte}, System.DateTimeOffset)"/>.
    /// Strips redundant leading <c>0x00</c> / <c>0xFF</c> padding and prepends a
    /// single <c>0x00</c> sign byte when the high bit is set, so the encoded serial
    /// matches the certificate's own (always-positive) serial. See issue #817.
    /// </summary>
    internal static byte[] NormalizeSerial(byte[] serial)
    {
        var start = 0;
        // Drop redundant leading 0x00 (positive padding) — a leading zero is only
        // permitted when the next byte's high bit is set (to keep the value positive).
        while (start < serial.Length - 1 && serial[start] == 0x00 && (serial[start + 1] & 0x80) == 0)
            start++;
        // Drop redundant leading 0xFF (negative padding) — defensive; serials are
        // generated positive, but normalise faithfully if one ever arrives signed.
        while (start < serial.Length - 1 && serial[start] == 0xFF && (serial[start + 1] & 0x80) != 0)
            start++;

        var trimmed = serial.AsSpan(start);

        // A high bit on the leading byte would be read as a negative integer.
        // Certificate serials are positive, so prepend a 0x00 sign byte.
        if ((trimmed[0] & 0x80) != 0)
        {
            var prefixed = new byte[trimmed.Length + 1];
            trimmed.CopyTo(prefixed.AsSpan(1));
            return prefixed;
        }

        return trimmed.ToArray();
    }
}
