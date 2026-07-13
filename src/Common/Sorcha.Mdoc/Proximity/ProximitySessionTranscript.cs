// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Mdoc.Cbor;

namespace Sorcha.Mdoc.Proximity;

/// <summary>
/// The ISO 18013-5 proximity <c>SessionTranscript</c> — the value that binds a presentation to one
/// engagement, and the single most unforgiving structure in the exchange.
/// </summary>
/// <remarks>
/// <para>
/// <c>SessionTranscript = [ DeviceEngagementBytes, EReaderKeyBytes, Handover ]</c>, where the first two
/// elements are each <b>tag-24-wrapped</b>. For QR engagement <c>Handover</c> is <c>null</c>; for NFC it
/// carries the handover messages.
/// </para>
/// <para>
/// <b>The standard uses two different forms of this, and confusing them is the classic way to lose a week:</b>
/// </para>
/// <list type="table">
///   <listheader><term>Form</term><description>Where it is used</description></listheader>
///   <item>
///     <term><see cref="Encoded"/> — the bare 3-element array</term>
///     <description>Spliced verbatim into <c>DeviceAuthentication</c>.</description>
///   </item>
///   <item>
///     <term><see cref="TaggedBytes"/> — <c>#6.24(bstr .cbor SessionTranscript)</c></term>
///     <description>Hashed (SHA-256) to produce the HKDF <b>salt</b> for the session keys.</description>
///   </item>
/// </list>
/// <para>
/// Swap them and you derive plausible-looking keys that decrypt nothing, or MAC over the wrong bytes — and
/// the failure is silent. This type exists so that no caller ever has to remember which is which: it holds
/// both, named.
/// </para>
/// <para>
/// ⚠ The <c>Handover</c> element is a <b>2021 addition</b> — the freely-downloadable DIS draft has a
/// 2-element transcript. If you are reconciling against a draft, you are reconciling against the wrong thing.
/// </para>
/// </remarks>
public sealed class ProximitySessionTranscript
{
    private ProximitySessionTranscript(byte[] encoded, byte[] taggedBytes)
    {
        Encoded = encoded;
        TaggedBytes = taggedBytes;
    }

    /// <summary>The bare <c>SessionTranscript</c> array, encoded. Splice this into <c>DeviceAuthentication</c>.</summary>
    public byte[] Encoded { get; }

    /// <summary>The tag-24-wrapped <c>SessionTranscriptBytes</c>. Hash this for the HKDF salt.</summary>
    public byte[] TaggedBytes { get; }

    /// <summary>
    /// Builds the transcript for <b>QR engagement</b> (<c>Handover = null</c>) — the path this feature ships.
    /// </summary>
    /// <param name="deviceEngagementTagged">The tag-24-wrapped <c>DeviceEngagementBytes</c>.</param>
    /// <param name="eReaderKeyTagged">The tag-24-wrapped <c>EReaderKeyBytes</c> (the reader's ephemeral COSE_Key).</param>
    public static ProximitySessionTranscript ForQrEngagement(byte[] deviceEngagementTagged, byte[] eReaderKeyTagged)
    {
        ArgumentNullException.ThrowIfNull(deviceEngagementTagged);
        ArgumentNullException.ThrowIfNull(eReaderKeyTagged);

        var encoded = MdocCbor.Encode(w =>
        {
            w.WriteStartArray(3);
            w.WriteEncodedValue(deviceEngagementTagged);
            w.WriteEncodedValue(eReaderKeyTagged);
            w.WriteNull();                          // Handover: null for QR engagement.
            w.WriteEndArray();
        });

        return new ProximitySessionTranscript(encoded, MdocCbor.WrapTag24(encoded));
    }

    /// <summary>
    /// Builds the transcript for <b>NFC engagement</b>, whose <c>Handover</c> carries the handover-select and
    /// handover-request messages.
    /// </summary>
    /// <param name="handoverSelect">The NFC handover-select message. Never null.</param>
    /// <param name="handoverRequest">The NFC handover-request message, or null for static handover.</param>
    /// <remarks>
    /// Included because ISO 18013-5 Annex D's worked example is the <b>NFC</b> case — so this is the only
    /// form for which published reference data exists, and it is therefore what the Annex D tests exercise.
    /// The QR form we actually ship has no published vector and is validated structurally instead.
    /// </remarks>
    public static ProximitySessionTranscript ForNfcEngagement(
        byte[] deviceEngagementTagged, byte[] eReaderKeyTagged, byte[] handoverSelect, byte[]? handoverRequest)
    {
        ArgumentNullException.ThrowIfNull(deviceEngagementTagged);
        ArgumentNullException.ThrowIfNull(eReaderKeyTagged);
        ArgumentNullException.ThrowIfNull(handoverSelect);

        var encoded = MdocCbor.Encode(w =>
        {
            w.WriteStartArray(3);
            w.WriteEncodedValue(deviceEngagementTagged);
            w.WriteEncodedValue(eReaderKeyTagged);
            w.WriteStartArray(2);                   // NFCHandover = [ handoverSelect, handoverRequest ]
            w.WriteByteString(handoverSelect);
            if (handoverRequest is null) w.WriteNull(); else w.WriteByteString(handoverRequest);
            w.WriteEndArray();
            w.WriteEndArray();
        });

        return new ProximitySessionTranscript(encoded, MdocCbor.WrapTag24(encoded));
    }

    /// <summary>
    /// Adopts an already-encoded <c>SessionTranscript</c> array (for example, one recovered from reference
    /// data or received from a peer), deriving the tag-24 form from it.
    /// </summary>
    public static ProximitySessionTranscript FromEncoded(byte[] encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        return new ProximitySessionTranscript(encoded, MdocCbor.WrapTag24(encoded));
    }

    /// <summary>Adopts an already-tag-24-wrapped <c>SessionTranscriptBytes</c>, unwrapping to get the bare array.</summary>
    public static ProximitySessionTranscript FromTaggedBytes(byte[] taggedBytes)
    {
        ArgumentNullException.ThrowIfNull(taggedBytes);
        return new ProximitySessionTranscript(MdocCbor.UnwrapTag24(taggedBytes), taggedBytes);
    }
}
