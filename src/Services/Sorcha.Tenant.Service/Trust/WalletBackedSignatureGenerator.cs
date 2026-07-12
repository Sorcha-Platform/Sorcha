// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Feature 181 US4 — an <see cref="X509SignatureGenerator"/> that signs a CSR's
/// <c>CertificationRequestInfo</c> (or a certificate's TBS) with an org's P-256 key held in the Wallet
/// Service. The private key never leaves Wallet custody (FR-018, research R10): this generator hashes the
/// to-be-signed bytes with SHA-256, asks the Wallet Service to sign the digest (returning a raw IEEE P1363
/// <c>r‖s</c> signature), and converts that to the DER <c>ECDSA-Sig-Value</c> encoding X.509 requires.
/// P-256 / SHA-256 only.
/// </summary>
public sealed class WalletBackedSignatureGenerator : X509SignatureGenerator
{
    private readonly IWalletServiceClient _walletClient;
    private readonly string _walletAddress;

    /// <param name="walletClient">Wallet Service client used to sign the pre-computed digest.</param>
    /// <param name="walletAddress">Org wallet whose P-256 cert-issuing key signs.</param>
    /// <param name="publicKeySpki">DER SubjectPublicKeyInfo of that P-256 key (from the resolve call).</param>
    public WalletBackedSignatureGenerator(IWalletServiceClient walletClient, string walletAddress, byte[] publicKeySpki)
    {
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _walletAddress = walletAddress ?? throw new ArgumentNullException(nameof(walletAddress));
        ArgumentNullException.ThrowIfNull(publicKeySpki);

        var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
        // Delegate algorithm-identifier + public-key encoding to the BCL's own ECDSA generator (public-only
        // key is sufficient for both); only SignData is overridden to reach the remote key.
        _inner = CreateForECDsa(ecdsa);
    }

    private readonly X509SignatureGenerator _inner;

    /// <inheritdoc />
    protected override PublicKey BuildPublicKey() => _inner.PublicKey;

    /// <inheritdoc />
    public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm)
        => _inner.GetSignatureAlgorithmIdentifier(hashAlgorithm);

    /// <inheritdoc />
    public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
    {
        if (hashAlgorithm != HashAlgorithmName.SHA256)
        {
            throw new NotSupportedException(
                $"WalletBackedSignatureGenerator supports SHA-256 only (P-256/ES256); got {hashAlgorithm.Name}.");
        }

        var digest = SHA256.HashData(data);

        // X509SignatureGenerator.SignData is synchronous; CSR generation is a cold admin path, so blocking
        // on the remote sign is acceptable here.
        var rawSignature = _walletClient
            .SignIssuerCertPreHashedAsync(_walletAddress, digest)
            .GetAwaiter().GetResult();

        return ConvertP1363ToDer(rawSignature);
    }

    /// <summary>
    /// Convert a raw IEEE P1363 <c>r‖s</c> ECDSA signature into a DER <c>ECDSA-Sig-Value</c> SEQUENCE of two
    /// INTEGERs (RFC 3279 §2.2.3), as X.509/PKCS#10 require.
    /// </summary>
    internal static byte[] ConvertP1363ToDer(byte[] p1363)
    {
        ArgumentNullException.ThrowIfNull(p1363);
        if (p1363.Length == 0 || p1363.Length % 2 != 0)
        {
            throw new CryptographicException(
                $"Raw ECDSA signature length {p1363.Length} is not an even, non-zero r‖s concatenation.");
        }

        var half = p1363.Length / 2;
        var r = new BigInteger(p1363.AsSpan(0, half), isUnsigned: true, isBigEndian: true);
        var s = new BigInteger(p1363.AsSpan(half), isUnsigned: true, isBigEndian: true);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(r);
            writer.WriteInteger(s);
        }
        return writer.Encode();
    }
}
