// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Feature 181 US6 (T052 / R12) — the verifier's request-signing X.509 certificate. Its SAN dNSName is the
/// installation's public host, and it backs the <c>x509_san_dns:{host}</c> client_id + the <c>x5c</c> header
/// on signed request objects so a wallet can authenticate the verifier. P-256 / ES256 only (EUDI).
/// </summary>
/// <remarks>
/// Loaded from <c>Haip:VerifierCertificate</c> (PFX file path or base64 PKCS#12) + <c>Haip:VerifierCertificatePassword?</c>.
/// In dev, when no certificate is configured, a self-signed ES256 certificate with SAN dNSName =
/// <c>Haip:PublicHost</c> is generated in-memory — self-contained demos keep working and wallets render the
/// verifier as authentic-but-untrusted. Production/Staging fail-fast (mirrors <c>SorchaIssuer</c>) when no
/// certificate is configured or its SAN does not match the public host.
/// </remarks>
public sealed class VerifierCertificate : IDisposable
{
    private readonly ECDsa _signingKey;

    private VerifierCertificate(string publicHost, ECDsa signingKey, IReadOnlyList<byte[]> x5cChain, bool isDevFallback)
    {
        PublicHost = publicHost;
        _signingKey = signingKey;
        X5cChain = x5cChain;
        IsDevFallback = isDevFallback;
    }

    /// <summary>The installation's public host (the SAN dNSName the certificate binds).</summary>
    public string PublicHost { get; }

    /// <summary>The prefixed final-spec client identifier: <c>x509_san_dns:{PublicHost}</c>.</summary>
    public string ClientId => $"x509_san_dns:{PublicHost}";

    /// <summary>The certificate chain (DER per element, leaf-first) for the JWS <c>x5c</c> header.</summary>
    public IReadOnlyList<byte[]> X5cChain { get; }

    /// <summary>True when this is the in-memory self-signed dev fallback (not an operator-provisioned cert).</summary>
    public bool IsDevFallback { get; }

    /// <summary>Sign the request-object signing input with the verifier key (ES256, raw r‖s / P1363).</summary>
    public byte[] SignData(byte[] signingInput)
        => _signingKey.SignData(signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    /// <summary>
    /// Resolve the verifier certificate from configuration, generating the dev fallback when none is
    /// configured. Throws in Production/Staging when no certificate is available or its SAN mismatches
    /// the public host (fail-closed startup validation, FR-025).
    /// </summary>
    public static VerifierCertificate Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var publicHost = configuration.GetValue<string>("Haip:PublicHost")
            ?? DeriveHostFromIssuerUrl(configuration.GetValue<string>("Haip:IssuerUrl"))
            ?? "localhost";

        var certConfig = configuration.GetValue<string>("Haip:VerifierCertificate");
        var isProdLike = environment.IsProduction() || environment.IsStaging();

        if (!string.IsNullOrWhiteSpace(certConfig))
        {
            var password = configuration.GetValue<string>("Haip:VerifierCertificatePassword");
            var cert = LoadPkcs12(certConfig, password);
            var san = LeafSanDnsName(cert);
            if (!string.Equals(san, publicHost, StringComparison.OrdinalIgnoreCase))
            {
                cert.Dispose();
                throw new InvalidOperationException(
                    $"Haip:VerifierCertificate SAN dNSName '{san ?? "(none)"}' does not match Haip:PublicHost '{publicHost}'.");
            }
            var ecdsa = cert.GetECDsaPrivateKey()
                ?? throw new InvalidOperationException("Haip:VerifierCertificate must carry a P-256 (ES256) private key.");
            var chain = new List<byte[]> { cert.RawData };
            cert.Dispose();
            return new VerifierCertificate(publicHost, ecdsa, chain, isDevFallback: false);
        }

        if (isProdLike)
        {
            throw new InvalidOperationException(
                "Haip:VerifierCertificate is required in Production/Staging — the verifier must present an " +
                "x5c-bound request object. Configure a PFX whose SAN dNSName matches Haip:PublicHost.");
        }

        return GenerateDevFallback(publicHost);
    }

    /// <summary>
    /// Feature 181 US6 — a self-signed ES256 verifier certificate with SAN dNSName = <paramref name="publicHost"/>.
    /// Used for the dev fallback and as a test seam; wallets render it as authentic-but-untrusted.
    /// </summary>
    public static VerifierCertificate CreateSelfSigned(string publicHost) => GenerateDevFallback(publicHost);

    private static VerifierCertificate GenerateDevFallback(string publicHost)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            new X500DistinguishedName($"CN={publicHost}, O=Sorcha Verifier (dev)"), ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(publicHost);
        request.CertificateExtensions.Add(san.Build());

        var now = DateTimeOffset.UtcNow;
        using var cert = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(1));
        // Keep the private-key handle (the request's ecdsa) for signing; export just the DER for x5c.
        return new VerifierCertificate(publicHost, ecdsa, new[] { cert.RawData }, isDevFallback: true);
    }

    private static X509Certificate2 LoadPkcs12(string config, string? password)
    {
        // Config is either a file path or a base64 PKCS#12 blob.
        byte[] pfx;
        if (File.Exists(config))
        {
            pfx = File.ReadAllBytes(config);
        }
        else
        {
            try { pfx = Convert.FromBase64String(config); }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    "Haip:VerifierCertificate must be a PFX file path or a base64 PKCS#12 blob.", ex);
            }
        }
        return X509CertificateLoader.LoadPkcs12(pfx, password);
    }

    private static string? LeafSanDnsName(X509Certificate2 cert)
    {
        var san = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (san is null)
        {
            return null;
        }
        try
        {
            return san.EnumerateDnsNames().FirstOrDefault();
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static string? DeriveHostFromIssuerUrl(string? issuerUrl)
        => Uri.TryCreate(issuerUrl, UriKind.Absolute, out var uri) ? uri.Host : null;

    /// <inheritdoc />
    public void Dispose() => _signingKey.Dispose();
}
