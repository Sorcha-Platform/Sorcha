// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Sorcha.Mdoc.Cbor;

namespace Sorcha.Mdoc.Cose;

/// <summary>
/// Assembles and verifies <c>COSE_Sign1</c> (RFC 9052 §4.2) from a <b>raw</b> signature.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists rather than <c>CoseSign1Message.SignDetached</c>.</b> The citizen's device key is a
/// <b>non-extractable</b> WebCrypto key: the only thing the wallet can do with it is
/// <c>SignAsync(byte[]) → byte[]</c>. <c>CoseSign1Message.SignDetached</c> requires an
/// <see cref="System.Security.Cryptography.AsymmetricAlgorithm"/> instance and therefore <b>cannot consume
/// it</b>. There is no BCL API that accepts a pre-computed signature. So we compute the
/// <c>Sig_structure</c> ourselves, hand those bytes to the device to sign, and assemble the COSE object
/// around the signature that comes back.
/// </para>
/// <para>
/// This follows the established practice in this repo of hand-rolling a <em>standard's byte structure</em>
/// (never a crypto primitive) rather than taking a bad dependency — cf. the RLP encoder in feature 182.
/// </para>
/// <para>
/// Signature encoding is JOSE-style fixed-width <c>r ‖ s</c> (64 bytes for ES256), which is what COSE
/// requires — <b>not</b> the ASN.1/DER form that .NET and BouncyCastle produce by default. Getting this
/// wrong yields a signature that verifies nowhere.
/// </para>
/// </remarks>
public static class CoseSign1Builder
{
    /// <summary>COSE tag for a tagged <c>COSE_Sign1</c> (RFC 9052 §2).</summary>
    public const int CoseSign1Tag = 18;

    /// <summary>COSE algorithm identifier for ES256 (RFC 9053 §2.1).</summary>
    public const int AlgEs256 = -7;

    private const int HeaderLabelAlg = 1;

    /// <summary>
    /// Builds the <c>Sig_structure</c> whose bytes the device key must sign:
    /// <c>[ "Signature1", protected, external_aad, payload ]</c>.
    /// </summary>
    /// <param name="protectedHeader">Encoded protected header bstr contents (see <see cref="EncodeProtectedHeaderEs256"/>).</param>
    /// <param name="payload">
    /// The content being signed. For mdoc device auth this is the tag-24-wrapped
    /// <c>DeviceAuthentication</c> bytes, and the payload is <b>detached</b> from the emitted COSE object —
    /// but it is still what gets signed.
    /// </param>
    /// <param name="externalAad">External AAD. Empty for mdoc.</param>
    public static byte[] BuildSigStructure(byte[] protectedHeader, byte[] payload, byte[]? externalAad = null)
    {
        ArgumentNullException.ThrowIfNull(protectedHeader);
        ArgumentNullException.ThrowIfNull(payload);

        return MdocCbor.Encode(w =>
        {
            w.WriteStartArray(4);
            w.WriteTextString("Signature1");
            w.WriteByteString(protectedHeader);
            w.WriteByteString(externalAad ?? []);
            w.WriteByteString(payload);
            w.WriteEndArray();
        });
    }

    /// <summary>Encodes the protected header <c>{1: -7}</c> (alg = ES256) as its bstr contents.</summary>
    public static byte[] EncodeProtectedHeaderEs256() => MdocCbor.Encode(w =>
    {
        w.WriteStartMap(1);
        w.WriteInt32(HeaderLabelAlg);
        w.WriteInt32(AlgEs256);
        w.WriteEndMap();
    });

    /// <summary>
    /// Assembles a <b>detached-payload</b>, <b>tagged</b> <c>COSE_Sign1</c> from a raw ES256 signature.
    /// </summary>
    /// <param name="protectedHeader">The same protected header passed to <see cref="BuildSigStructure"/>.</param>
    /// <param name="rawSignature">The 64-byte JOSE-style <c>r ‖ s</c> signature over the Sig_structure.</param>
    /// <remarks>
    /// Emitted <b>tagged</b> (tag 18) to match how <c>CoseSign1Message.Encode()</c> writes the issuer's
    /// <c>issuerAuth</c> elsewhere in this codebase — so a reader decoding either with
    /// <c>CoseMessage.DecodeSign1</c> sees a consistent shape.
    /// </remarks>
    public static byte[] BuildDetached(byte[] protectedHeader, byte[] rawSignature)
    {
        ArgumentNullException.ThrowIfNull(protectedHeader);
        ArgumentNullException.ThrowIfNull(rawSignature);
        if (rawSignature.Length != 64)
            throw new ArgumentException(
                $"An ES256 COSE signature must be 64 bytes of raw r‖s, but this is {rawSignature.Length}. " +
                "A DER/ASN.1-encoded signature is the usual cause — convert it first.",
                nameof(rawSignature));

        return MdocCbor.Encode(w =>
        {
            w.WriteTag((CborTag)CoseSign1Tag);
            w.WriteStartArray(4);
            w.WriteByteString(protectedHeader);
            w.WriteStartMap(0);      // unprotected: empty
            w.WriteEndMap();
            w.WriteNull();           // payload: detached
            w.WriteByteString(rawSignature);
            w.WriteEndArray();
        });
    }

    /// <summary>
    /// Verifies a detached-payload <c>COSE_Sign1</c> against <paramref name="payload"/> using BouncyCastle.
    /// Returns <see langword="false"/> rather than throwing on any malformed input — this sits on a
    /// verification path and must fail closed.
    /// </summary>
    public static bool VerifyDetached(byte[] coseSign1, byte[] payload, ECPublicKeyParameters publicKey)
    {
        ArgumentNullException.ThrowIfNull(coseSign1);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(publicKey);

        try
        {
            var reader = new CborReader(coseSign1, CborConformanceMode.Lax);
            if (reader.PeekState() == CborReaderState.Tag)
            {
                var tag = reader.ReadTag();
                if ((int)tag != CoseSign1Tag)
                    return false;
            }

            reader.ReadStartArray();
            var protectedHeader = reader.ReadByteString();
            reader.SkipValue();                       // unprotected
            if (reader.PeekState() == CborReaderState.Null)
                reader.ReadNull();                    // detached, as expected
            else
                reader.SkipValue();                   // an embedded payload; we verify against the supplied one
            var signature = reader.ReadByteString();
            reader.ReadEndArray();

            if (signature.Length != 64)
                return false;

            var sigStructure = BuildSigStructure(protectedHeader, payload);
            var digest = System.Security.Cryptography.SHA256.HashData(sigStructure);

            var r = new Org.BouncyCastle.Math.BigInteger(1, signature.AsSpan(0, 32).ToArray());
            var s = new Org.BouncyCastle.Math.BigInteger(1, signature.AsSpan(32, 32).ToArray());

            var verifier = new ECDsaSigner();
            verifier.Init(forSigning: false, publicKey);
            return verifier.VerifySignature(digest, r, s);
        }
        catch (CborContentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Converts a DER/ASN.1 ECDSA signature to the fixed-width JOSE <c>r ‖ s</c> form COSE requires.
    /// </summary>
    /// <remarks>
    /// Provided because BouncyCastle and .NET both hand you DER by default, and passing DER straight into
    /// <see cref="BuildDetached"/> produces an object that verifies nowhere. Only needed on paths that
    /// sign with a library key; a WebCrypto device signature already arrives as raw <c>r ‖ s</c>.
    /// </remarks>
    public static byte[] DerToRawSignature(byte[] derSignature)
    {
        ArgumentNullException.ThrowIfNull(derSignature);

        var sequence = (Org.BouncyCastle.Asn1.Asn1Sequence)Org.BouncyCastle.Asn1.Asn1Object.FromByteArray(derSignature);
        var r = ((Org.BouncyCastle.Asn1.DerInteger)sequence[0]).PositiveValue;
        var s = ((Org.BouncyCastle.Asn1.DerInteger)sequence[1]).PositiveValue;

        var raw = new byte[64];
        WriteFixed(r, raw.AsSpan(0, 32));
        WriteFixed(s, raw.AsSpan(32, 32));
        return raw;

        static void WriteFixed(Org.BouncyCastle.Math.BigInteger value, Span<byte> destination)
        {
            var bytes = value.ToByteArrayUnsigned();
            if (bytes.Length > destination.Length)
                throw new InvalidOperationException("ECDSA signature component is too large for P-256.");
            bytes.CopyTo(destination[(destination.Length - bytes.Length)..]);
        }
    }
}
