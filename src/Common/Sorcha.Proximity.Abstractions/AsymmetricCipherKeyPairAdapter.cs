// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Org.BouncyCastle.Crypto.Parameters;
using Sorcha.Mdoc.Proximity;

namespace Sorcha.Proximity;

/// <summary>
/// An ephemeral P-256 key pair for one session, with the two halves typed so they cannot be transposed.
/// </summary>
/// <remarks>
/// BouncyCastle's <c>AsymmetricCipherKeyPair</c> exposes both halves as the same base type, so passing the
/// private key where the public one belongs — or a peer's key where your own belongs — compiles cleanly and
/// then derives silently-wrong session keys. Given that the ECDH pairings in this protocol are already
/// asymmetric and easy to get backwards, the types are worth the small wrapper.
/// </remarks>
internal sealed class AsymmetricCipherKeyPairAdapter
{
    private AsymmetricCipherKeyPairAdapter(ECPrivateKeyParameters priv, ECPublicKeyParameters pub)
    {
        Private = priv;
        Public = pub;
    }

    /// <summary>The ephemeral private key. Never leaves the session.</summary>
    public ECPrivateKeyParameters Private { get; }

    /// <summary>The ephemeral public key. Travels in the clear — it is half of the ECDH the peer needs.</summary>
    public ECPublicKeyParameters Public { get; }

    /// <summary>Generates a fresh pair. Called once per exchange, never reused.</summary>
    public static AsymmetricCipherKeyPairAdapter Generate()
    {
        var pair = MdocSessionCrypto.GenerateEphemeralKeyPair();
        return new AsymmetricCipherKeyPairAdapter(
            (ECPrivateKeyParameters)pair.Private,
            (ECPublicKeyParameters)pair.Public);
    }
}
