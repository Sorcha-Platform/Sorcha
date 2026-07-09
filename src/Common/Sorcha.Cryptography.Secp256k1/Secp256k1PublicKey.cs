// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// An secp256k1 public key (a validated curve point) used for JOSE ES256K signature verification
/// and, as Phase-1 foundation, Ethereum address derivation. Immutable; every factory validates the
/// point lies on the secp256k1 curve and is not the point at infinity.
/// </summary>
public sealed class Secp256k1PublicKey
{
    internal static readonly X9ECParameters CurveParams = SecNamedCurves.GetByName("secp256k1");

    internal static readonly ECDomainParameters Domain =
        new(CurveParams.Curve, CurveParams.G, CurveParams.N, CurveParams.H);

    internal ECPoint Point { get; }

    private Secp256k1PublicKey(ECPoint point) => Point = point.Normalize();

    /// <summary>The 32-byte big-endian affine X coordinate.</summary>
    public byte[] X => Point.AffineXCoord.GetEncoded();

    /// <summary>The 32-byte big-endian affine Y coordinate.</summary>
    public byte[] Y => Point.AffineYCoord.GetEncoded();

    /// <summary>
    /// Create a key from affine coordinates, each a 32-byte big-endian integer.
    /// </summary>
    /// <exception cref="ArgumentException">The coordinates do not describe a valid secp256k1 point.</exception>
    public static Secp256k1PublicKey FromCoordinates(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y)
    {
        var px = new BigInteger(1, x.ToArray());
        var py = new BigInteger(1, y.ToArray());
        ECPoint point;
        try
        {
            point = Domain.Curve.CreatePoint(px, py);
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Coordinates are not a valid secp256k1 point.", ex);
        }

        if (!point.IsValid())
        {
            throw new ArgumentException("Coordinates are not a valid secp256k1 point.");
        }

        return new Secp256k1PublicKey(point);
    }

    /// <summary>
    /// Create a key from a SEC1 encoded point: 65-byte uncompressed (<c>0x04 || X || Y</c>) or
    /// 33-byte compressed (<c>0x02</c>/<c>0x03 || X</c>). Compressed input is decompressed here — this
    /// is how <c>did:key</c> (multicodec <c>0xe701</c>) and <c>did:jwk</c> compressed keys are read.
    /// </summary>
    /// <exception cref="ArgumentException">The encoding is malformed or the point is off-curve.</exception>
    public static Secp256k1PublicKey FromSec1(ReadOnlySpan<byte> encoded)
    {
        ECPoint point;
        try
        {
            point = Domain.Curve.DecodePoint(encoded.ToArray());
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Invalid SEC1 secp256k1 point encoding.", ex);
        }

        if (!point.IsValid())
        {
            throw new ArgumentException("Encoded point is not a valid secp256k1 public key.");
        }

        return new Secp256k1PublicKey(point);
    }

    /// <summary>Encode as a 65-byte uncompressed SEC1 point (<c>0x04 || X || Y</c>).</summary>
    public byte[] ToSec1Uncompressed() => Point.GetEncoded(false);
}
