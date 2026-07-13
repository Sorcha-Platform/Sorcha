// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using System.Security.Cryptography;
using Sorcha.Mdoc.Cbor;

namespace Sorcha.Mdoc.Cose;

/// <summary>
/// Builds and verifies <c>COSE_Mac0</c> (RFC 9052 §6.2) with HMAC-SHA256 — the <c>deviceMac</c> form of ISO
/// 18013-5 device authentication.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The BCL has no <c>COSE_Mac0</c> type at all. That is the literal, documented
/// reason mdoc MAC device auth was refused in feature 135 ("MAC-based device authentication is not
/// supported in v1"), and why <c>DeviceAuth.DeviceMacRaw</c> has been round-tripped as opaque bytes ever
/// since. This type closes that gap.
/// </para>
/// <para>
/// <b>Why a MAC rather than a signature.</b> A <c>deviceSignature</c> is non-repudiable and
/// <em>transferable</em>: a verifier can keep it and prove to a third party, forever, that the citizen
/// presented. A MAC can only be checked by the party that took part in the key agreement, so it proves
/// possession to the reader in front of you and to nobody else. That privacy property is why most real EUDI
/// wallets choose it — and why our reader must be able to verify it if it is ever to read one.
/// </para>
/// <para>
/// The MAC key (<c>EMacKey</c>) is derived by ECDH between the mdoc's <b>static</b> device key (the one
/// published in the MSO) and the reader's <b>ephemeral</b> key. See <c>MdocSessionCrypto</c>.
/// </para>
/// </remarks>
public static class CoseMac0
{
    /// <summary>COSE tag for a tagged <c>COSE_Mac0</c> (RFC 9052 §2).</summary>
    public const int CoseMac0Tag = 17;

    /// <summary>COSE algorithm identifier for HMAC 256/256 (RFC 9053 §3.1).</summary>
    public const int AlgHmac256 = 5;

    private const int HeaderLabelAlg = 1;

    /// <summary>Encodes the protected header <c>{1: 5}</c> (alg = HMAC 256/256) as its bstr contents.</summary>
    public static byte[] EncodeProtectedHeaderHmac256() => MdocCbor.Encode(w =>
    {
        w.WriteStartMap(1);
        w.WriteInt32(HeaderLabelAlg);
        w.WriteInt32(AlgHmac256);
        w.WriteEndMap();
    });

    /// <summary>
    /// Builds the <c>MAC_structure</c> that is HMAC'd:
    /// <c>[ "MAC0", protected, external_aad, payload ]</c>.
    /// </summary>
    /// <remarks>
    /// Structurally identical to a <c>Sig_structure</c> apart from the context string — and that string is
    /// exactly what stops a MAC being replayed as a signature (or vice versa) across COSE contexts.
    /// </remarks>
    public static byte[] BuildMacStructure(byte[] protectedHeader, byte[] payload, byte[]? externalAad = null)
    {
        ArgumentNullException.ThrowIfNull(protectedHeader);
        ArgumentNullException.ThrowIfNull(payload);

        return MdocCbor.Encode(w =>
        {
            w.WriteStartArray(4);
            w.WriteTextString("MAC0");
            w.WriteByteString(protectedHeader);
            w.WriteByteString(externalAad ?? []);
            w.WriteByteString(payload);
            w.WriteEndArray();
        });
    }

    /// <summary>
    /// Builds a <b>detached-payload</b>, <b>tagged</b> <c>COSE_Mac0</c> over <paramref name="payload"/> with
    /// <paramref name="macKey"/>.
    /// </summary>
    /// <param name="payload">
    /// For mdoc device auth: the tag-24-wrapped <c>DeviceAuthentication</c> bytes. Detached in the emitted
    /// object (the reader reconstructs it from the session transcript), but it is what gets MAC'd.
    /// </param>
    /// <param name="macKey">The 32-byte <c>EMacKey</c>.</param>
    public static byte[] BuildDetached(byte[] payload, byte[] macKey)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(macKey);

        var protectedHeader = EncodeProtectedHeaderHmac256();
        var macStructure = BuildMacStructure(protectedHeader, payload);
        var tag = HMACSHA256.HashData(macKey, macStructure);

        return MdocCbor.Encode(w =>
        {
            w.WriteTag((CborTag)CoseMac0Tag);
            w.WriteStartArray(4);
            w.WriteByteString(protectedHeader);
            w.WriteStartMap(0);      // unprotected: empty
            w.WriteEndMap();
            w.WriteNull();           // payload: detached
            w.WriteByteString(tag);
            w.WriteEndArray();
        });
    }

    /// <summary>
    /// Verifies a detached-payload <c>COSE_Mac0</c> against <paramref name="payload"/> and
    /// <paramref name="macKey"/>. Returns <see langword="false"/> — never throws — on any malformed input:
    /// this is a verification path and must fail closed.
    /// </summary>
    /// <remarks>The tag comparison is fixed-time. A short-circuiting compare here would leak the tag.</remarks>
    public static bool VerifyDetached(byte[] coseMac0, byte[] payload, byte[] macKey)
    {
        ArgumentNullException.ThrowIfNull(coseMac0);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(macKey);

        try
        {
            var reader = new CborReader(coseMac0, CborConformanceMode.Lax);
            if (reader.PeekState() == CborReaderState.Tag)
            {
                var tag = reader.ReadTag();
                if ((int)tag != CoseMac0Tag)
                    return false;
            }

            reader.ReadStartArray();
            var protectedHeader = reader.ReadByteString();
            reader.SkipValue();                       // unprotected
            if (reader.PeekState() == CborReaderState.Null)
                reader.ReadNull();                    // detached, as expected
            else
                reader.SkipValue();
            var presentedTag = reader.ReadByteString();
            reader.ReadEndArray();

            // Reject any protected header that does not say HMAC 256/256 — an attacker must not be able to
            // talk us into a weaker (or different) MAC algorithm by relabelling the header.
            if (!ProtectedHeaderIsHmac256(protectedHeader))
                return false;

            var macStructure = BuildMacStructure(protectedHeader, payload);
            var expectedTag = HMACSHA256.HashData(macKey, macStructure);

            return CryptographicOperations.FixedTimeEquals(presentedTag, expectedTag);
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

    private static bool ProtectedHeaderIsHmac256(byte[] protectedHeader)
    {
        try
        {
            var reader = new CborReader(protectedHeader, CborConformanceMode.Lax);
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var label = reader.ReadInt32();
                if (label == HeaderLabelAlg)
                    return reader.ReadInt32() == AlgHmac256;
                reader.SkipValue();
            }
            return false;
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
}
