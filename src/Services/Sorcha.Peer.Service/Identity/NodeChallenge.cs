// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Peer.Service.Identity;

/// <summary>
/// Verification helpers for the Feature 175 node-identity registration challenge. A registering peer
/// signs a server-issued nonce with its node key; the server verifies the signature against the
/// peer-supplied public key, proving node-key possession independently of the transport (works over
/// cleartext as well as mTLS). Trust in replicated <em>data</em> still comes from register
/// cryptography — this only authenticates the peer's identity claim at the discovery layer.
/// </summary>
public static class NodeChallenge
{
    /// <summary>Encodes a nonce string to the exact bytes that are signed/verified.</summary>
    public static byte[] NonceBytes(string nonce) => Encoding.UTF8.GetBytes(nonce);

    /// <summary>
    /// Verifies that <paramref name="signature"/> is a valid ECDSA/SHA-256 signature over
    /// <paramref name="nonce"/> for the P-256 public key encoded in <paramref name="publicKeySpki"/>
    /// (SubjectPublicKeyInfo DER). Returns <c>false</c> for any malformed input rather than throwing.
    /// </summary>
    public static bool Verify(byte[] publicKeySpki, string nonce, byte[] signature)
    {
        if (publicKeySpki is null || publicKeySpki.Length == 0 ||
            signature is null || signature.Length == 0 || string.IsNullOrEmpty(nonce))
        {
            return false;
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
            return ecdsa.VerifyData(NonceBytes(nonce), signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Derives the stable node identity string from a public key (SHA-256 of the SPKI, hex). This is
    /// the installation-neutral identity a verified peer is recorded under, independent of any
    /// self-assigned peer id.
    /// </summary>
    public static string IdentityFromPublicKey(byte[] publicKeySpki) =>
        Convert.ToHexString(SHA256.HashData(publicKeySpki));
}
