// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sorcha.WorkloadIdentity;

/// <summary>
/// Issues the per-installation Workload CA and its leaf certificates (F191 / #1420).
/// EC P-256 / ES256 throughout — the platform's established X.509 rail. This is a deliberately
/// separate trust rail from the Feature 135 tenant/org CA: filesystem-resident, per-installation,
/// issued centrally by the CLI from the known service-principal list (research D4).
/// </summary>
public static class WorkloadCertificateAuthority
{
    /// <summary>Default CA lifetime (years).</summary>
    public const int DefaultCaValidityYears = 5;

    /// <summary>Default leaf lifetime (years).</summary>
    public const int DefaultLeafValidityYears = 2;

    private static readonly Oid ClientAuthOid = new("1.3.6.1.5.5.7.3.2");
    private static readonly Oid ServerAuthOid = new("1.3.6.1.5.5.7.3.1");

    // Certificates must already be valid at issue time despite modest clock skew between hosts.
    private static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(5);

    /// <summary>Creates the self-signed Workload CA for an installation.</summary>
    public static X509Certificate2 CreateCertificateAuthority(
        string installationName,
        int validityYears = DefaultCaValidityYears)
    {
        if (string.IsNullOrWhiteSpace(installationName))
            throw new ArgumentException("Installation name is required.", nameof(installationName));

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedName($"CN=Sorcha Workload CA ({installationName.Trim().ToLowerInvariant()}), O=Sorcha");
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);

        // pathLength 0: this CA signs leaves only, never intermediates.
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: true, hasPathLengthConstraint: true, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow - ClockSkewAllowance;
        return request.CreateSelfSigned(notBefore, notBefore.AddYears(validityYears));
    }

    /// <summary>
    /// Issues a service (client-auth) leaf carrying the SPIFFE URI SAN and the service's
    /// internal DNS name. A fresh keypair is generated per issuance (renew semantics).
    /// </summary>
    public static X509Certificate2 IssueServiceCertificate(
        X509Certificate2 issuingCa,
        SpiffeId spiffeId,
        string dnsName,
        int validityYears = DefaultLeafValidityYears,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        ArgumentNullException.ThrowIfNull(spiffeId);
        if (string.IsNullOrWhiteSpace(dnsName))
            throw new ArgumentException("DNS name is required.", nameof(dnsName));

        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(spiffeId.ToUri());
        san.AddDnsName(dnsName);

        return IssueLeaf(
            issuingCa,
            subjectCn: spiffeId.ClientId,
            san,
            ClientAuthOid,
            validityYears,
            notBefore,
            notAfter);
    }

    /// <summary>Issues the server certificate for the Tenant mTLS listener.</summary>
    public static X509Certificate2 IssueServerCertificate(
        X509Certificate2 issuingCa,
        string dnsName,
        int validityYears = DefaultLeafValidityYears,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        if (string.IsNullOrWhiteSpace(dnsName))
            throw new ArgumentException("DNS name is required.", nameof(dnsName));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);

        return IssueLeaf(
            issuingCa,
            subjectCn: dnsName,
            san,
            ServerAuthOid,
            validityYears,
            notBefore,
            notAfter);
    }

    /// <summary>
    /// Extracts the SPIFFE workload id from a certificate's URI SAN. Returns false when the
    /// certificate carries no URI SAN in the workload shape (e.g. a server certificate).
    /// </summary>
    public static bool TryGetSpiffeId(X509Certificate2 certificate, out SpiffeId? spiffeId)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        spiffeId = null;

        foreach (var extension in certificate.Extensions)
        {
            if (extension is not X509SubjectAlternativeNameExtension sanExtension)
                continue;

            foreach (var uri in EnumerateUriSans(sanExtension))
            {
                if (SpiffeId.TryParse(uri, out var parsed))
                {
                    spiffeId = parsed;
                    return true;
                }
            }
        }

        return false;
    }

    private static X509Certificate2 IssueLeaf(
        X509Certificate2 issuingCa,
        string subjectCn,
        SubjectAlternativeNameBuilder san,
        Oid eku,
        int validityYears,
        DateTimeOffset? notBefore,
        DateTimeOffset? notAfter)
    {
        ArgumentNullException.ThrowIfNull(issuingCa);
        if (!issuingCa.HasPrivateKey)
            throw new ArgumentException("Issuing CA must carry its private key.", nameof(issuingCa));

        using var caKey = issuingCa.GetECDsaPrivateKey()
            ?? throw new ArgumentException("Issuing CA must be an EC P-256 certificate.", nameof(issuingCa));

        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={subjectCn}, O=Sorcha Workload"),
            leafKey,
            HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(
            certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { eku }, critical: false));
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        // Authority Key Identifier is LOAD-BEARING for CA rotation, not just hygiene: during
        // rotate-ca overlap the old and new roots share a subject DN, and OpenSSL-based chain
        // building (Linux) selects its issuer candidate by subject — without AKI→SKI matching it
        // can bind a leaf to the WRONG same-subject root and fail with NotSignatureValid.
        // Windows CryptoAPI happens to tolerate this, so only Linux ever sees the failure.
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(
                issuingCa, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        var effectiveNotBefore = notBefore ?? (DateTimeOffset.UtcNow - ClockSkewAllowance);
        var effectiveNotAfter = notAfter ?? effectiveNotBefore.AddYears(validityYears);

        // Random 16-byte serial, high bit cleared (positive INTEGER per RFC 5280).
        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        serial[0] &= 0x7F;

        using var withoutKey = request.Create(
            issuingCa.SubjectName,
            X509SignatureGenerator.CreateForECDsa(caKey),
            effectiveNotBefore,
            effectiveNotAfter,
            serial);

        // Round-trip through PKCS#12 with default (non-ephemeral) key storage: Windows SChannel
        // refuses TLS with ephemeral private keys, and these leaves exist to do TLS.
        using var ephemeral = withoutKey.CopyWithPrivateKey(leafKey);
        return X509CertificateLoader.LoadPkcs12(
            ephemeral.Export(X509ContentType.Pfx),
            password: null,
            X509KeyStorageFlags.Exportable);
    }

    private static IEnumerable<string> EnumerateUriSans(X509SubjectAlternativeNameExtension sanExtension)
    {
        // GeneralName ::= CHOICE { ... uniformResourceIdentifier [6] IA5String ... }
        var uris = new List<string>();
        var reader = new AsnReader(sanExtension.RawData, AsnEncodingRules.DER);
        var sequence = reader.ReadSequence();
        while (sequence.HasData)
        {
            var tag = sequence.PeekTag();
            if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 6)
            {
                uris.Add(sequence.ReadCharacterString(
                    UniversalTagNumber.IA5String,
                    new Asn1Tag(TagClass.ContextSpecific, 6)));
            }
            else
            {
                sequence.ReadEncodedValue();
            }
        }

        return uris;
    }
}
