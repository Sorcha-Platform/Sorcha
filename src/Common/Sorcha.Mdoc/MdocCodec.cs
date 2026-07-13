// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using System.Security.Cryptography.Cose;

using Sorcha.Mdoc.Cbor;

namespace Sorcha.Mdoc;

/// <summary>
/// Encodes and decodes the ISO 18013-5 mdoc wire structures (feature 135) using
/// <see cref="System.Formats.Cbor"/> + <see cref="System.Security.Cryptography.Cose"/>. Decoding
/// preserves the tag-24 <c>IssuerSignedItemBytes</c> and device-namespaces bytes verbatim so the
/// MSO digests and the <c>DeviceAuthentication</c> payload reconstruct exactly. Also builds the
/// OpenID4VP 1.x hash-based <c>SessionTranscript</c> and the detached <c>DeviceAuthentication</c>.
/// </summary>
public static class MdocCodec
{
    // ---- IssuerSignedItem -----------------------------------------------------

    /// <summary>Encodes the inner CBOR of an <see cref="IssuerSignedItem"/> (before tag-24 wrapping).</summary>
    public static byte[] EncodeIssuerSignedItem(IssuerSignedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return MdocCbor.Encode(w =>
        {
            w.WriteStartMap(4);
            w.WriteTextString("digestID"); w.WriteUInt32(item.DigestId);
            w.WriteTextString("random"); w.WriteByteString(item.Random);
            w.WriteTextString("elementIdentifier"); w.WriteTextString(item.ElementIdentifier);
            w.WriteTextString("elementValue"); WriteGeneric(w, item.ElementValue);
            w.WriteEndMap();
        });
    }

    /// <summary>Decodes the inner CBOR of an <see cref="IssuerSignedItem"/>.</summary>
    public static IssuerSignedItem DecodeIssuerSignedItem(ReadOnlyMemory<byte> innerCbor)
    {
        return MdocCbor.Decode(innerCbor, r =>
        {
            var item = new IssuerSignedItem();
            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                switch (r.ReadTextString())
                {
                    case "digestID": item.DigestId = (uint)r.ReadUInt64(); break;
                    case "random": item.Random = r.ReadByteString(); break;
                    case "elementIdentifier": item.ElementIdentifier = r.ReadTextString(); break;
                    case "elementValue": item.ElementValue = ReadGeneric(r); break;
                    default: r.SkipValue(); break;
                }
            }
            r.ReadEndMap();
            return item;
        });
    }

    // ---- MobileSecurityObject -------------------------------------------------

    /// <summary>Encodes the inner CBOR of an MSO (the tag-24 payload of <c>issuerAuth</c>, before wrapping).</summary>
    public static byte[] EncodeMso(MobileSecurityObject mso)
    {
        ArgumentNullException.ThrowIfNull(mso);
        return MdocCbor.Encode(w =>
        {
            var fieldCount = 6 + (mso.Status is not null ? 1 : 0);
            w.WriteStartMap(fieldCount);

            w.WriteTextString("version"); w.WriteTextString(mso.Version);
            w.WriteTextString("digestAlgorithm"); w.WriteTextString(mso.DigestAlgorithm);

            w.WriteTextString("valueDigests");
            w.WriteStartMap(mso.ValueDigests.Count);
            foreach (var (ns, digests) in mso.ValueDigests)
            {
                w.WriteTextString(ns);
                w.WriteStartMap(digests.Count);
                foreach (var (digestId, digest) in digests)
                {
                    w.WriteUInt32(digestId);
                    w.WriteByteString(digest);
                }
                w.WriteEndMap();
            }
            w.WriteEndMap();

            w.WriteTextString("deviceKeyInfo");
            w.WriteStartMap(1);
            w.WriteTextString("deviceKey");
            w.WriteEncodedValue(mso.DeviceKeyCose);
            w.WriteEndMap();

            w.WriteTextString("docType"); w.WriteTextString(mso.DocType);

            w.WriteTextString("validityInfo");
            var vi = mso.ValidityInfo;
            w.WriteStartMap(3 + (vi.ExpectedUpdate.HasValue ? 1 : 0));
            w.WriteTextString("signed"); w.WriteDateTimeOffset(vi.Signed);
            w.WriteTextString("validFrom"); w.WriteDateTimeOffset(vi.ValidFrom);
            w.WriteTextString("validUntil"); w.WriteDateTimeOffset(vi.ValidUntil);
            if (vi.ExpectedUpdate.HasValue)
            {
                w.WriteTextString("expectedUpdate"); w.WriteDateTimeOffset(vi.ExpectedUpdate.Value);
            }
            w.WriteEndMap();

            if (mso.Status is not null)
            {
                w.WriteTextString("status");
                w.WriteStartMap(1);
                w.WriteTextString("status_list");
                w.WriteStartMap(2);
                w.WriteTextString("uri"); w.WriteTextString(mso.Status.Uri);
                w.WriteTextString("idx"); w.WriteUInt32(mso.Status.Idx);
                w.WriteEndMap();
                w.WriteEndMap();
            }

            w.WriteEndMap();
        });
    }

    /// <summary>Decodes an MSO from its inner (tag-24-unwrapped) CBOR.</summary>
    public static MobileSecurityObject DecodeMso(ReadOnlyMemory<byte> innerCbor)
    {
        return MdocCbor.Decode(innerCbor, r =>
        {
            var mso = new MobileSecurityObject();
            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                switch (r.ReadTextString())
                {
                    case "version": mso.Version = r.ReadTextString(); break;
                    case "digestAlgorithm": mso.DigestAlgorithm = r.ReadTextString(); break;
                    case "valueDigests": mso.ValueDigests = ReadValueDigests(r); break;
                    case "deviceKeyInfo": mso.DeviceKeyCose = ReadDeviceKey(r); break;
                    case "docType": mso.DocType = r.ReadTextString(); break;
                    case "validityInfo": mso.ValidityInfo = ReadValidityInfo(r); break;
                    case "status": mso.Status = ReadStatus(r); break;
                    default: r.SkipValue(); break;
                }
            }
            r.ReadEndMap();
            return mso;
        });
    }

    private static Dictionary<string, Dictionary<uint, byte[]>> ReadValueDigests(CborReader r)
    {
        var result = new Dictionary<string, Dictionary<uint, byte[]>>();
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            var ns = r.ReadTextString();
            var digests = new Dictionary<uint, byte[]>();
            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                var id = (uint)r.ReadUInt64();
                digests[id] = r.ReadByteString();
            }
            r.ReadEndMap();
            result[ns] = digests;
        }
        r.ReadEndMap();
        return result;
    }

    private static byte[] ReadDeviceKey(CborReader r)
    {
        byte[] deviceKey = [];
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            if (r.ReadTextString() == "deviceKey")
                deviceKey = MdocCbor.ReadRawItem(r);
            else
                r.SkipValue();
        }
        r.ReadEndMap();
        return deviceKey;
    }

    private static ValidityInfo ReadValidityInfo(CborReader r)
    {
        var vi = new ValidityInfo();
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "signed": vi.Signed = r.ReadDateTimeOffset(); break;
                case "validFrom": vi.ValidFrom = r.ReadDateTimeOffset(); break;
                case "validUntil": vi.ValidUntil = r.ReadDateTimeOffset(); break;
                case "expectedUpdate": vi.ExpectedUpdate = r.ReadDateTimeOffset(); break;
                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();
        return vi;
    }

    private static MsoStatus? ReadStatus(CborReader r)
    {
        MsoStatus? status = null;
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            if (r.ReadTextString() == "status_list")
            {
                status = new MsoStatus();
                r.ReadStartMap();
                while (r.PeekState() != CborReaderState.EndMap)
                {
                    switch (r.ReadTextString())
                    {
                        case "uri": status.Uri = r.ReadTextString(); break;
                        case "idx": status.Idx = (uint)r.ReadUInt64(); break;
                        default: r.SkipValue(); break;
                    }
                }
                r.ReadEndMap();
            }
            else
            {
                r.SkipValue();
            }
        }
        r.ReadEndMap();
        return status;
    }

    // ---- DeviceResponse -------------------------------------------------------

    /// <summary>Encodes a full <see cref="DeviceResponse"/> to CBOR.</summary>
    public static byte[] EncodeDeviceResponse(DeviceResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return MdocCbor.Encode(w =>
        {
            w.WriteStartMap(3);
            w.WriteTextString("version"); w.WriteTextString(response.Version);
            w.WriteTextString("documents");
            w.WriteStartArray(response.Documents.Count);
            foreach (var doc in response.Documents)
                WriteDocument(w, doc);
            w.WriteEndArray();
            w.WriteTextString("status"); w.WriteUInt32(response.Status);
            w.WriteEndMap();
        });
    }

    /// <summary>Encodes a standalone <see cref="IssuerSigned"/> (the issued mdoc credential at rest).</summary>
    public static byte[] EncodeIssuerSigned(IssuerSigned issuerSigned)
    {
        ArgumentNullException.ThrowIfNull(issuerSigned);
        return MdocCbor.Encode(w => WriteIssuerSigned(w, issuerSigned));
    }

    /// <summary>Decodes a standalone <see cref="IssuerSigned"/>.</summary>
    public static IssuerSigned DecodeIssuerSigned(ReadOnlyMemory<byte> cbor)
        => MdocCbor.Decode(cbor, ReadIssuerSigned);

    private static void WriteIssuerSigned(CborWriter w, IssuerSigned issuerSigned)
    {
        w.WriteStartMap(2);
        w.WriteTextString("nameSpaces");
        w.WriteStartMap(issuerSigned.NameSpaces.Count);
        foreach (var (ns, items) in issuerSigned.NameSpaces)
        {
            w.WriteTextString(ns);
            w.WriteStartArray(items.Count);
            foreach (var item in items)
                w.WriteEncodedValue(item.TaggedBytes); // verbatim tag-24 bytes
            w.WriteEndArray();
        }
        w.WriteEndMap();
        w.WriteTextString("issuerAuth");
        w.WriteEncodedValue(issuerSigned.IssuerAuth.Encode());
        w.WriteEndMap();
    }

    private static void WriteDocument(CborWriter w, Document doc)
    {
        w.WriteStartMap(3);
        w.WriteTextString("docType"); w.WriteTextString(doc.DocType);

        w.WriteTextString("issuerSigned");
        WriteIssuerSigned(w, doc.IssuerSigned);

        w.WriteTextString("deviceSigned");
        w.WriteStartMap(2);
        w.WriteTextString("nameSpaces");
        w.WriteEncodedValue(doc.DeviceSigned.NameSpacesBytes); // verbatim tag-24 bytes
        w.WriteTextString("deviceAuth");
        WriteDeviceAuth(w, doc.DeviceSigned.DeviceAuth);
        w.WriteEndMap();

        w.WriteEndMap();
    }

    private static void WriteDeviceAuth(CborWriter w, DeviceAuth auth)
    {
        w.WriteStartMap(1);
        if (auth.DeviceSignature is not null)
        {
            w.WriteTextString("deviceSignature");
            w.WriteEncodedValue(auth.DeviceSignature.Encode());
        }
        else if (auth.DeviceMacRaw is not null)
        {
            w.WriteTextString("deviceMac");
            w.WriteEncodedValue(auth.DeviceMacRaw);
        }
        else
        {
            throw new InvalidOperationException("DeviceAuth must carry either a deviceSignature or a deviceMac.");
        }
        w.WriteEndMap();
    }

    /// <summary>Decodes a full <see cref="DeviceResponse"/> from CBOR.</summary>
    public static DeviceResponse DecodeDeviceResponse(ReadOnlyMemory<byte> cbor)
    {
        return MdocCbor.Decode(cbor, r =>
        {
            var response = new DeviceResponse();
            r.ReadStartMap();
            while (r.PeekState() != CborReaderState.EndMap)
            {
                switch (r.ReadTextString())
                {
                    case "version": response.Version = r.ReadTextString(); break;
                    case "status": response.Status = (uint)r.ReadUInt64(); break;
                    case "documents":
                        r.ReadStartArray();
                        while (r.PeekState() != CborReaderState.EndArray)
                            response.Documents.Add(ReadDocument(r));
                        r.ReadEndArray();
                        break;
                    default: r.SkipValue(); break;
                }
            }
            r.ReadEndMap();
            return response;
        });
    }

    private static Document ReadDocument(CborReader r)
    {
        string docType = string.Empty;
        IssuerSigned? issuerSigned = null;
        DeviceSigned? deviceSigned = null;

        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "docType": docType = r.ReadTextString(); break;
                case "issuerSigned": issuerSigned = ReadIssuerSigned(r); break;
                case "deviceSigned": deviceSigned = ReadDeviceSigned(r); break;
                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();

        if (issuerSigned is null || deviceSigned is null)
            throw new CborContentException("Document is missing issuerSigned or deviceSigned.");

        return new Document { DocType = docType, IssuerSigned = issuerSigned, DeviceSigned = deviceSigned };
    }

    private static IssuerSigned ReadIssuerSigned(CborReader r)
    {
        var nameSpaces = new Dictionary<string, IReadOnlyList<IssuerSignedItemBytes>>();
        CoseSign1Message? issuerAuth = null;

        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "nameSpaces":
                    r.ReadStartMap();
                    while (r.PeekState() != CborReaderState.EndMap)
                    {
                        var ns = r.ReadTextString();
                        var items = new List<IssuerSignedItemBytes>();
                        r.ReadStartArray();
                        while (r.PeekState() != CborReaderState.EndArray)
                        {
                            var tagged = MdocCbor.ReadRawItem(r); // #6.24(bstr) verbatim
                            items.Add(new IssuerSignedItemBytes
                            {
                                TaggedBytes = tagged,
                                Item = DecodeIssuerSignedItem(MdocCbor.UnwrapTag24(tagged))
                            });
                        }
                        r.ReadEndArray();
                        nameSpaces[ns] = items;
                    }
                    r.ReadEndMap();
                    break;

                case "issuerAuth":
                    issuerAuth = CoseMessage.DecodeSign1(MdocCbor.ReadRawItem(r));
                    break;

                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();

        if (issuerAuth is null)
            throw new CborContentException("IssuerSigned is missing issuerAuth.");

        return new IssuerSigned { NameSpaces = nameSpaces, IssuerAuth = issuerAuth };
    }

    private static DeviceSigned ReadDeviceSigned(CborReader r)
    {
        byte[] nameSpacesBytes = [];
        DeviceAuth? deviceAuth = null;

        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "nameSpaces": nameSpacesBytes = MdocCbor.ReadRawItem(r); break;
                case "deviceAuth": deviceAuth = ReadDeviceAuth(r); break;
                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();

        if (deviceAuth is null)
            throw new CborContentException("DeviceSigned is missing deviceAuth.");

        return new DeviceSigned { NameSpacesBytes = nameSpacesBytes, DeviceAuth = deviceAuth };
    }

    private static DeviceAuth ReadDeviceAuth(CborReader r)
    {
        var auth = new DeviceAuth();
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "deviceSignature": auth.DeviceSignature = CoseMessage.DecodeSign1(MdocCbor.ReadRawItem(r)); break;
                case "deviceMac": auth.DeviceMacRaw = MdocCbor.ReadRawItem(r); break;
                default: r.SkipValue(); break;
            }
        }
        r.ReadEndMap();
        return auth;
    }

    // ---- SessionTranscript / DeviceAuthentication (OpenID4VP 1.x, R9) ---------

    /// <summary>
    /// Builds the OpenID4VP 1.x hash-based <c>SessionTranscript</c> CBOR:
    /// <c>[ null, null, [ "OpenID4VPHandover", SHA-256(OpenID4VPHandoverInfoBytes) ] ]</c> where
    /// <c>OpenID4VPHandoverInfo = [ clientId, nonce, jwkThumbprint|null, responseUri ]</c>.
    /// </summary>
    public static byte[] BuildOpenId4VpSessionTranscript(
        string clientId, string nonce, byte[]? jwkThumbprint, string responseUri)
    {
        var handoverInfo = MdocCbor.Encode(w =>
        {
            w.WriteStartArray(4);
            w.WriteTextString(clientId);
            w.WriteTextString(nonce);
            if (jwkThumbprint is null) w.WriteNull(); else w.WriteByteString(jwkThumbprint);
            w.WriteTextString(responseUri);
            w.WriteEndArray();
        });
        var handoverHash = System.Security.Cryptography.SHA256.HashData(handoverInfo);

        return MdocCbor.Encode(w =>
        {
            w.WriteStartArray(3);
            w.WriteNull();
            w.WriteNull();
            w.WriteStartArray(2);
            w.WriteTextString("OpenID4VPHandover");
            w.WriteByteString(handoverHash);
            w.WriteEndArray();
            w.WriteEndArray();
        });
    }

    /// <summary>
    /// Builds the tag-24-wrapped <c>DeviceAuthentication</c> payload that <c>DeviceAuth</c> signs/MACs:
    /// <c>#6.24(bstr .cbor [ "DeviceAuthentication", SessionTranscript, docType, DeviceNameSpacesBytes ])</c>.
    /// <paramref name="sessionTranscript"/> and <paramref name="deviceNameSpacesBytes"/> are spliced
    /// in verbatim as already-encoded CBOR.
    /// </summary>
    public static byte[] BuildDeviceAuthentication(
        byte[] sessionTranscript, string docType, byte[] deviceNameSpacesBytes)
    {
        var inner = MdocCbor.Encode(w =>
        {
            w.WriteStartArray(4);
            w.WriteTextString("DeviceAuthentication");
            w.WriteEncodedValue(sessionTranscript);
            w.WriteTextString(docType);
            w.WriteEncodedValue(deviceNameSpacesBytes);
            w.WriteEndArray();
        });
        return MdocCbor.WrapTag24(inner);
    }

    // ---- generic CBOR value (elementValue) ------------------------------------

    private static void WriteGeneric(CborWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNull(); break;
            case bool b: w.WriteBoolean(b); break;
            case string s: w.WriteTextString(s); break;
            case byte[] bytes: w.WriteByteString(bytes); break;
            case int i: w.WriteInt32(i); break;
            case uint ui: w.WriteUInt32(ui); break;
            case long l: w.WriteInt64(l); break;
            case ulong ul: w.WriteUInt64(ul); break;
            case double d: w.WriteDouble(d); break;
            case DateTimeOffset dto: w.WriteDateTimeOffset(dto); break;
            default: throw new NotSupportedException($"Unsupported mdoc element value type {value.GetType()}.");
        }
    }

    private static object? ReadGeneric(CborReader r)
    {
        switch (r.PeekState())
        {
            case CborReaderState.Null: r.ReadNull(); return null;
            case CborReaderState.Boolean: return r.ReadBoolean();
            case CborReaderState.TextString: return r.ReadTextString();
            case CborReaderState.ByteString: return r.ReadByteString();
            case CborReaderState.UnsignedInteger: return r.ReadUInt64();
            case CborReaderState.NegativeInteger: return r.ReadInt64();
            case CborReaderState.HalfPrecisionFloat:
            case CborReaderState.SinglePrecisionFloat:
            case CborReaderState.DoublePrecisionFloat: return r.ReadDouble();
            case CborReaderState.StartArray:
                var list = new List<object?>();
                r.ReadStartArray();
                while (r.PeekState() != CborReaderState.EndArray) list.Add(ReadGeneric(r));
                r.ReadEndArray();
                return list;
            case CborReaderState.StartMap:
                var map = new Dictionary<object, object?>();
                r.ReadStartMap();
                while (r.PeekState() != CborReaderState.EndMap)
                {
                    var key = ReadGeneric(r)!;
                    map[key] = ReadGeneric(r);
                }
                r.ReadEndMap();
                return map;
            case CborReaderState.Tag:
                // Date tags (0 date-time string, 1004 full-date) and any other semantic tag —
                // return the inner value (string/number) for claim surfacing.
                var tag = r.ReadTag();
                if (tag == CborTag.DateTimeString) return r.ReadTextString();
                return ReadGeneric(r);
            default:
                throw new CborContentException($"Unsupported CBOR state {r.PeekState()} for an mdoc element value.");
        }
    }
}
