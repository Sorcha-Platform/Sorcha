// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;


namespace Sorcha.Cryptography.SdJwt;

/// <summary>
/// Decides whether an issued credential verifies under the <c>x5c</c> chain it would carry.
/// </summary>
/// <remarks>
/// RFC 7515 §4.1.6 requires the first certificate in <c>x5c</c> to contain the key that signed the
/// JWS, and verifiers act on that: Blueprint's issuer-key resolver prefers <c>x5c</c> over DID
/// resolution. #1699: issuance attached the org's P-256 certificate chain while signing with the
/// org's separate Ed25519 VC-issuance key (the Feature 120 kid-swap changed the signing key and
/// left the chain alone). Every such credential had a valid signature and a header pointing at the
/// wrong key, so it failed verification everywhere x5c is honoured — reported as "key must be 32
/// bytes", which named nothing.
///
/// The check is behavioural rather than a key comparison: verify the minted token with the leaf's
/// key, exactly as a verifier would. That covers every way the two can disagree (different key,
/// different curve, different algorithm) without needing the signing public key, which the
/// issuance material does not carry.
/// </remarks>
public static class X5cSigningKeyMatch
{
    /// <summary>OID of an Ed25519 subject public key (RFC 8410).</summary>
    private const string Ed25519Oid = "1.3.101.112";

    /// <summary>
    /// True when <paramref name="rawSdJwt"/>'s issuer signature verifies with the public key of the
    /// first certificate in <paramref name="chain"/>. Any failure to read the leaf is false: a chain
    /// that cannot be checked must not be attached.
    /// </summary>
    public static async Task<bool> TokenVerifiesUnderLeafAsync(
        ISdJwtService sdJwtService,
        string rawSdJwt,
        IReadOnlyList<byte[]> chain,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sdJwtService);
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0 || string.IsNullOrWhiteSpace(rawSdJwt))
            return false;

        byte[] leafKey;
        string leafAlgorithm;
        try
        {
            using var leaf = X509CertificateLoader.LoadCertificate(chain[0]);
            if (leaf.PublicKey.Oid.Value == Ed25519Oid)
            {
                leafKey = leaf.PublicKey.EncodedKeyValue.RawData;
                leafAlgorithm = "EdDSA";
            }
            else if (leaf.GetECDsaPublicKey() is { } ecdsa)
            {
                using (ecdsa)
                    leafKey = ecdsa.ExportSubjectPublicKeyInfo();
                leafAlgorithm = "ES256";
            }
            else
            {
                return false;
            }
        }
        catch (CryptographicException)
        {
            return false;
        }

        var result = await sdJwtService
            .VerifyPresentationAsync(rawSdJwt, leafKey, leafAlgorithm, cancellationToken)
            .ConfigureAwait(false);
        return result.IsValid;
    }
}
