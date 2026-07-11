// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.Xml;

namespace Sorcha.Tenant.Service.Trust;

/// <summary>
/// Feature 181 US3 — maps the RFC 4051 <c>ecdsa-sha256</c> XMLDSig URI onto <see cref="ECDsa"/>
/// sign/verify, since .NET ships no built-in <see cref="SignatureDescription"/> for ECDSA. Registered
/// once via <see cref="CryptoConfig.AddAlgorithm(Type, string[])"/> so <see cref="SignedXml"/> can
/// verify ECDSA-signed trusted lists (EU LOTL is typically RSA; some national lists use ECDSA).
/// Public because <c>CryptoConfig.AddAlgorithm</c> requires a visible type.
/// </summary>
public sealed class EcdsaSha256SignatureDescription : SignatureDescription
{
    /// <summary>The XMLDSig signature-method URI for ECDSA P-256 / SHA-256 (RFC 4051).</summary>
    public const string SignatureMethodUri = "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256";

    /// <summary>Initialises the description with the ECDsa key algorithm.</summary>
    public EcdsaSha256SignatureDescription()
    {
        KeyAlgorithm = typeof(ECDsa).AssemblyQualifiedName;
    }

    /// <inheritdoc />
    public override HashAlgorithm CreateDigest() => SHA256.Create();

    /// <inheritdoc />
    public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key) =>
        new EcdsaSignatureFormatter((ECDsa)key);

    /// <inheritdoc />
    public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key) =>
        new EcdsaSignatureDeformatter((ECDsa)key);

    private sealed class EcdsaSignatureFormatter(ECDsa key) : AsymmetricSignatureFormatter
    {
        private ECDsa _key = key;

        public override byte[] CreateSignature(byte[] rgbHash) => _key.SignHash(rgbHash);

        public override void SetHashAlgorithm(string strName) { }

        public override void SetKey(AsymmetricAlgorithm key) => _key = (ECDsa)key;
    }

    private sealed class EcdsaSignatureDeformatter(ECDsa key) : AsymmetricSignatureDeformatter
    {
        private ECDsa _key = key;

        public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature) =>
            _key.VerifyHash(rgbHash, rgbSignature);

        public override void SetHashAlgorithm(string strName) { }

        public override void SetKey(AsymmetricAlgorithm key) => _key = (ECDsa)key;
    }
}
