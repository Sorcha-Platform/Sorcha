// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using Sorcha.Mdoc.Cbor;

namespace Sorcha.Mdoc.Proximity;

/// <summary>
/// The reader's opening message: its ephemeral public key in the clear, plus the encrypted
/// <c>DeviceRequest</c>.
/// </summary>
/// <remarks>
/// <c>SessionEstablishment = { "eReaderKey": EReaderKeyBytes, "data": bstr }</c>. The key must travel in the
/// clear — it is half of the ECDH the holder needs in order to derive the keys that decrypt <c>data</c>.
/// </remarks>
public sealed class SessionEstablishment
{
    /// <summary>The tag-24-wrapped <c>EReaderKeyBytes</c> (the reader's ephemeral COSE_Key).</summary>
    public required byte[] EReaderKeyBytes { get; init; }

    /// <summary>The encrypted <c>DeviceRequest</c> (ciphertext ‖ GCM tag).</summary>
    public required byte[] Data { get; init; }

    /// <summary>Encodes to CBOR.</summary>
    public byte[] Encode() => MdocCbor.Encode(w =>
    {
        w.WriteStartMap(2);
        w.WriteTextString("eReaderKey");
        w.WriteEncodedValue(EReaderKeyBytes);   // already tag-24-wrapped; splice verbatim
        w.WriteTextString("data");
        w.WriteByteString(Data);
        w.WriteEndMap();
    });

    /// <summary>Decodes from CBOR. Throws <see cref="CborContentException"/> on a malformed message.</summary>
    public static SessionEstablishment Decode(ReadOnlyMemory<byte> cbor)
    {
        byte[]? eReaderKey = null;
        byte[]? data = null;

        var r = new CborReader(cbor, CborConformanceMode.Lax);
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "eReaderKey": eReaderKey = MdocCbor.ReadRawItem(r); break;   // keep verbatim
                case "data": data = r.ReadByteString(); break;
                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();

        if (eReaderKey is null)
            throw new CborContentException("SessionEstablishment is missing eReaderKey.");
        if (data is null)
            throw new CborContentException("SessionEstablishment is missing data.");

        return new SessionEstablishment { EReaderKeyBytes = eReaderKey, Data = data };
    }
}

/// <summary>
/// Every message after <see cref="SessionEstablishment"/>, in either direction: encrypted payload, a status,
/// or both.
/// </summary>
/// <remarks>
/// <c>SessionData = { ?"data": bstr, ?"status": uint }</c>. A message carrying only a status terminates the
/// session.
/// </remarks>
public sealed class SessionData
{
    /// <summary>Encrypted payload (ciphertext ‖ GCM tag), or null on a status-only message.</summary>
    public byte[]? Data { get; init; }

    /// <summary>Status code, or null when the message carries only data.</summary>
    public uint? Status { get; init; }

    /// <summary>Normal session termination.</summary>
    public const uint StatusSessionTermination = 20;

    /// <summary>The session ended because of an error.</summary>
    public const uint StatusSessionEncryptionError = 10;

    /// <summary>The <c>DeviceRequest</c> could not be understood.</summary>
    public const uint StatusCborDecodingError = 11;

    /// <summary>A status-only message ending the session normally.</summary>
    public static SessionData Termination() => new() { Status = StatusSessionTermination };

    /// <summary>Encodes to CBOR.</summary>
    public byte[] Encode()
    {
        var fieldCount = (Data is not null ? 1 : 0) + (Status is not null ? 1 : 0);
        if (fieldCount == 0)
            throw new InvalidOperationException("A SessionData message must carry data, a status, or both.");

        return MdocCbor.Encode(w =>
        {
            w.WriteStartMap(fieldCount);
            if (Data is not null)
            {
                w.WriteTextString("data");
                w.WriteByteString(Data);
            }
            if (Status is not null)
            {
                w.WriteTextString("status");
                w.WriteUInt32(Status.Value);
            }
            w.WriteEndMap();
        });
    }

    /// <summary>Decodes from CBOR. Throws <see cref="CborContentException"/> on a malformed message.</summary>
    public static SessionData Decode(ReadOnlyMemory<byte> cbor)
    {
        byte[]? data = null;
        uint? status = null;

        var r = new CborReader(cbor, CborConformanceMode.Lax);
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "data": data = r.ReadByteString(); break;
                case "status": status = r.ReadUInt32(); break;
                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();

        if (data is null && status is null)
            throw new CborContentException("SessionData carries neither data nor status.");

        return new SessionData { Data = data, Status = status };
    }
}
