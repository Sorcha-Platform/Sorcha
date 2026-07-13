// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;

namespace Sorcha.Mdoc.Cbor;

/// <summary>
/// CBOR primitives for the ISO mdoc wire format (feature 135). Centralises tag-24
/// (<c>#6.24(bstr .cbor X)</c>) wrapping — which is load-bearing because digests and signatures
/// are computed over the <em>tagged outer bytes</em> — and deterministic encode/decode helpers.
/// The inner bytes of a tag-24 value are always preserved verbatim; never re-encode the inner map.
/// </summary>
public static class MdocCbor
{
    /// <summary>
    /// Wraps <paramref name="innerCbor"/> as a tag-24 encoded-CBOR-data-item:
    /// <c>#6.24(bstr)</c> whose byte-string payload is <paramref name="innerCbor"/> verbatim.
    /// </summary>
    public static byte[] WrapTag24(byte[] innerCbor)
    {
        ArgumentNullException.ThrowIfNull(innerCbor);
        var writer = new CborWriter(CborConformanceMode.Canonical);
        writer.WriteTag(CborTag.EncodedCborDataItem); // tag 24
        writer.WriteByteString(innerCbor);
        return writer.Encode();
    }

    /// <summary>
    /// Reads a tag-24 encoded-CBOR-data-item and returns its inner byte-string payload verbatim.
    /// Throws when the value is not tag-24 wrapped.
    /// </summary>
    public static byte[] UnwrapTag24(ReadOnlyMemory<byte> tagged)
    {
        var reader = new CborReader(tagged, CborConformanceMode.Lax);
        var tag = reader.ReadTag();
        if (tag != CborTag.EncodedCborDataItem)
            throw new CborContentException($"Expected tag 24 (encoded CBOR data item) but read tag {(int)tag}.");
        return reader.ReadByteString();
    }

    /// <summary>Encodes a CBOR data item written by <paramref name="write"/> in deterministic (canonical) form.</summary>
    public static byte[] Encode(Action<CborWriter> write, CborConformanceMode mode = CborConformanceMode.Canonical)
    {
        ArgumentNullException.ThrowIfNull(write);
        var writer = new CborWriter(mode);
        write(writer);
        return writer.Encode();
    }

    /// <summary>Decodes a single CBOR data item from <paramref name="data"/> via <paramref name="read"/>.</summary>
    public static T Decode<T>(ReadOnlyMemory<byte> data, Func<CborReader, T> read, CborConformanceMode mode = CborConformanceMode.Lax)
    {
        ArgumentNullException.ThrowIfNull(read);
        var reader = new CborReader(data, mode);
        return read(reader);
    }

    /// <summary>
    /// Reads the next data item's raw encoded bytes without interpreting them — used to capture the
    /// tag-24 <c>IssuerSignedItemBytes</c> and other signed/hashed payloads exactly as received.
    /// </summary>
    public static byte[] ReadRawItem(CborReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return reader.ReadEncodedValue().ToArray();
    }
}
