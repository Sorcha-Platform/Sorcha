// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>Verifies JOSE ES256K signatures against an secp256k1 public key.</summary>
public interface ISecp256k1Verifier
{
    /// <summary>
    /// Verify a JOSE ES256K signature (ECDSA over SHA-256 with a 64-byte fixed-width <c>r || s</c>)
    /// against <paramref name="key"/>. Returns <c>false</c> — never throws — for any malformed input.
    /// </summary>
    bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, Secp256k1PublicKey key);

    /// <summary>
    /// Verify a JOSE ES256K signature by <b>public-key recovery</b> against an Ethereum address
    /// (address-form DIDs — <c>did:pkh</c> / address-form <c>did:ethr</c> — where no public key is
    /// published). Recovers the candidate signing keys, derives each one's EIP-55 address, and returns
    /// <c>true</c> iff any matches <paramref name="expectedAddress"/> case-insensitively.
    /// <paramref name="expectedAddress"/> may be a bare <c>0x…</c> address or a CAIP-10
    /// <c>eip155:{chainId}:0x…</c> account id. Returns <c>false</c> — never throws — on any malformed input.
    /// </summary>
    bool VerifyByAddress(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, string expectedAddress);
}

/// <summary>
/// Pure-managed (BouncyCastle) JOSE ES256K verifier. Accepts both high-s and low-s signatures on
/// verification (low-s is a produce-side concern deferred to a later phase). WASM-safe.
/// </summary>
public sealed class Secp256k1Verifier : ISecp256k1Verifier
{
    /// <summary>A shared stateless instance for call sites that do not use dependency injection.</summary>
    public static readonly Secp256k1Verifier Default = new();

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, Secp256k1PublicKey key)
        => VerifyEs256k(message, joseSignature, key);

    /// <inheritdoc />
    public bool VerifyByAddress(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, string expectedAddress)
        => VerifyByAddressStatic(message, joseSignature, expectedAddress);

    /// <summary>
    /// Static public-key-recovery verification entry point for the static call sites
    /// (e.g. <c>SdJwtService.Verify</c>). See <see cref="ISecp256k1Verifier.VerifyByAddress"/>.
    /// </summary>
    public static bool VerifyByAddressStatic(
        ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, string expectedAddress)
    {
        var target = NormalizeAddress(expectedAddress);
        if (target is null)
        {
            return false;
        }

        foreach (var key in Secp256k1Recovery.TryRecover(message, joseSignature))
        {
            var recovered = NormalizeAddress(EthereumAddress.FromPublicKey(key));
            if (recovered is not null && string.Equals(recovered, target, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalise an Ethereum address to 40 lowercase hex chars (no <c>0x</c>), tolerating a CAIP-10
    /// <c>eip155:{chainId}:0x…</c> prefix and EIP-55 checksummed casing. Returns null if not a valid address.
    /// </summary>
    private static string? NormalizeAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var addr = value.Trim();

        // Strip any CAIP-10 namespace/chain prefix (eip155:1:0x…) — keep the trailing account segment.
        var colon = addr.LastIndexOf(':');
        if (colon >= 0)
        {
            addr = addr[(colon + 1)..];
        }

        if (addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            addr = addr[2..];
        }

        if (addr.Length != 40)
        {
            return null;
        }

        foreach (var c in addr)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return null;
            }
        }

        return addr.ToLowerInvariant();
    }

    /// <summary>
    /// Static verification entry point for the static call sites (e.g. <c>SdJwtService.Verify</c>).
    /// </summary>
    public static bool VerifyEs256k(ReadOnlySpan<byte> message, ReadOnlySpan<byte> joseSignature, Secp256k1PublicKey key)
    {
        if (key is null || joseSignature.Length != 64)
        {
            return false;
        }

        try
        {
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(message, hash);

            var r = new BigInteger(1, joseSignature[..32].ToArray());
            var s = new BigInteger(1, joseSignature[32..].ToArray());
            if (r.SignValue <= 0 || s.SignValue <= 0)
            {
                return false;
            }

            var signer = new ECDsaSigner();
            signer.Init(false, new ECPublicKeyParameters(key.Point, Secp256k1PublicKey.Domain));
            return signer.VerifySignature(hash.ToArray(), r, s);
        }
        catch
        {
            return false;
        }
    }
}
