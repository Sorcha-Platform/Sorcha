// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.X509;

namespace Sorcha.Mdoc.Cose;

/// <summary>
/// Reads the issuer's public key out of an x5chain leaf certificate — <b>without</b> the BCL's
/// <c>X509Certificate2</c>.
/// </summary>
/// <remarks>
/// <para>
/// mdoc resolves the issuer key from the certificate chain in the COSE header, so verification cannot avoid
/// parsing an X.509 certificate. But <c>X509CertificateLoader</c> / <c>GetECDsaPublicKey()</c> are BCL
/// platform crypto, and this assembly runs in a Blazor <b>WASM</b> host where that is not dependable — the
/// code compiles and then fails on a phone.
/// </para>
/// <para>
/// BouncyCastle's parser is pure-managed and behaves identically on the server, in tests, and in the browser,
/// so the reader app can verify a credential offline on a device. This is the same reasoning that produced
/// <c>Sorcha.Cryptography.Secp256k1</c> and that forced <c>Sorcha.Mdoc</c> out of <c>Sorcha.Cryptography</c>
/// in the first place.
/// </para>
/// <para>
/// <b>Scope:</b> this extracts a key. It does <b>not</b> validate the chain, check revocation, or decide
/// trust — those are the trust evaluator's job (feature 135), and conflating them here would quietly turn a
/// parsing helper into a trust decision.
/// </para>
/// </remarks>
public static class X509Leaf
{
    /// <summary>
    /// Extracts the P-256 public key from a DER-encoded certificate.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> — never an exception — when the bytes are not a certificate, or its key is not
    /// EC. Callers sit on a verification path and must fail closed.
    /// </returns>
    public static ECPublicKeyParameters? TryReadEcPublicKey(byte[] derCertificate)
    {
        ArgumentNullException.ThrowIfNull(derCertificate);

        try
        {
            var certificate = new X509CertificateParser().ReadCertificate(derCertificate);
            return certificate?.GetPublicKey() as ECPublicKeyParameters;
        }
        catch (Exception)
        {
            // A malformed or non-EC certificate yields no key, and verification then fails closed.
            return null;
        }
    }

    /// <summary>Reads the leaf's subject DN, for surfacing the issuer's identity to an operator.</summary>
    /// <returns><see langword="null"/> when the certificate cannot be parsed.</returns>
    public static string? TryReadSubject(byte[] derCertificate)
    {
        ArgumentNullException.ThrowIfNull(derCertificate);

        try
        {
            return new X509CertificateParser().ReadCertificate(derCertificate)?.SubjectDN.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
