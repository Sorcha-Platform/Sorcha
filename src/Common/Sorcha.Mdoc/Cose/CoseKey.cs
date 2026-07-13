// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Sorcha.Mdoc.Cbor;

namespace Sorcha.Mdoc.Cose;

/// <summary>
/// EC2 (P-256) <c>COSE_Key</c> encode/decode (RFC 9052 §7).
/// </summary>
/// <remarks>
/// <para>
/// Three distinct keys in the ISO 18013-5 proximity exchange are carried as EC2 COSE_Keys, and this
/// type is the single place that shape is written or read:
/// </para>
/// <list type="bullet">
///   <item><description>the mdoc's <b>static</b> device key, published in the MSO (<c>MobileSecurityObject.DeviceKeyCose</c>);</description></item>
///   <item><description><c>EDeviceKey</c> — the holder's <b>ephemeral</b> session key, carried in <c>DeviceEngagement</c>;</description></item>
///   <item><description><c>EReaderKey</c> — the reader's <b>ephemeral</b> session key, carried in <c>SessionEstablishment</c>.</description></item>
/// </list>
/// <para>
/// Parsing returns BouncyCastle parameters rather than <see cref="System.Security.Cryptography.ECDsa"/>
/// because this assembly runs in Blazor WASM, where the BCL's EC implementation is not dependable. Use
/// BouncyCastle for anything this type hands you.
/// </para>
/// <para>mdoc is <b>P-256 only</b> at the format layer (feature 135); other curves are rejected, not coerced.</para>
/// </remarks>
public static class CoseKey
{
    // COSE_Key label constants (RFC 9052 §7.1, RFC 9053 §7.1).
    private const int LabelKty = 1;
    private const int LabelCrv = -1;
    private const int LabelX = -2;
    private const int LabelY = -3;

    private const int KtyEc2 = 2;
    private const int CrvP256 = 1;

    /// <summary>Length in bytes of a P-256 field element.</summary>
    public const int P256CoordinateLength = 32;

    private static readonly X9ECParameters Curve = ECNamedCurveTable.GetByName("P-256");
    private static readonly ECDomainParameters Domain =
        new(Curve.Curve, Curve.G, Curve.N, Curve.H, Curve.GetSeed());

    /// <summary>The P-256 domain parameters, for callers doing ECDH or key generation.</summary>
    public static ECDomainParameters DomainParameters => Domain;

    /// <summary>Encodes a P-256 public key as an EC2 <c>COSE_Key</c> map: <c>{1: 2, -1: 1, -2: x, -3: y}</c>.</summary>
    public static byte[] EncodeEc2PublicKey(ECPublicKeyParameters publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        var q = publicKey.Q.Normalize();
        var x = ToFixedLength(q.AffineXCoord.ToBigInteger());
        var y = ToFixedLength(q.AffineYCoord.ToBigInteger());

        return MdocCbor.Encode(w =>
        {
            w.WriteStartMap(4);
            w.WriteInt32(LabelKty); w.WriteInt32(KtyEc2);
            w.WriteInt32(LabelCrv); w.WriteInt32(CrvP256);
            w.WriteInt32(LabelX); w.WriteByteString(x);
            w.WriteInt32(LabelY); w.WriteByteString(y);
            w.WriteEndMap();
        });
    }

    /// <summary>
    /// Parses an EC2 P-256 <c>COSE_Key</c> map. Returns <see langword="null"/> — never throws — when the
    /// bytes are not a well-formed P-256 EC2 key, so callers can fail closed on a verification path
    /// without exception handling.
    /// </summary>
    public static ECPublicKeyParameters? TryParseEc2PublicKey(ReadOnlyMemory<byte> coseKey)
    {
        if (coseKey.IsEmpty)
            return null;

        try
        {
            int? kty = null, crv = null;
            byte[]? x = null, y = null;

            var reader = new CborReader(coseKey, CborConformanceMode.Lax);
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var label = reader.ReadInt32();
                switch (label)
                {
                    case LabelKty: kty = reader.ReadInt32(); break;
                    case LabelCrv: crv = reader.ReadInt32(); break;
                    case LabelX: x = reader.ReadByteString(); break;
                    case LabelY: y = reader.ReadByteString(); break;
                    default: reader.SkipValue(); break;
                }
            }
            reader.ReadEndMap();

            if (kty != KtyEc2 || crv != CrvP256 || x is null || y is null)
                return null;
            if (x.Length != P256CoordinateLength || y.Length != P256CoordinateLength)
                return null;

            var point = Curve.Curve.CreatePoint(
                new BigInteger(1, x),
                new BigInteger(1, y));

            // Reject a point that is not actually on the curve — an attacker-supplied off-curve point is
            // the classic invalid-curve attack against the ECDH that follows.
            if (!point.IsValid())
                return null;

            return new ECPublicKeyParameters(point, Domain);
        }
        catch (CborContentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ArithmeticException)
        {
            return null;
        }
    }

    /// <summary>Left-pads a coordinate to the P-256 field length. BouncyCastle strips leading zero bytes.</summary>
    private static byte[] ToFixedLength(BigInteger value)
    {
        var bytes = value.ToByteArrayUnsigned();
        if (bytes.Length == P256CoordinateLength)
            return bytes;
        if (bytes.Length > P256CoordinateLength)
            throw new InvalidOperationException($"Coordinate is {bytes.Length} bytes, which is too large for P-256.");

        var padded = new byte[P256CoordinateLength];
        Buffer.BlockCopy(bytes, 0, padded, P256CoordinateLength - bytes.Length, bytes.Length);
        return padded;
    }
}
