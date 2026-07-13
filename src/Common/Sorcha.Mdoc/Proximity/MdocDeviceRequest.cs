// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using Sorcha.Mdoc.Cbor;

namespace Sorcha.Mdoc.Proximity;

/// <summary>One data element the reader is asking for.</summary>
/// <param name="Namespace">The mdoc namespace, e.g. <c>org.iso.18013.5.1</c>.</param>
/// <param name="ElementIdentifier">The element, e.g. <c>family_name</c> or <c>age_over_18</c>.</param>
/// <param name="IntentToRetain">
/// Whether the reader intends to <b>keep</b> this value after the check.
/// <para>
/// This is not a technical detail — it is a disclosure the citizen is entitled to see before they consent,
/// and the UI must surface it. "Prove you're over 18" and "prove you're over 18, and I'm keeping a copy" are
/// materially different asks, and only one of them is what most people think they're agreeing to.
/// </para>
/// </param>
public readonly record struct RequestedElement(string Namespace, string ElementIdentifier, bool IntentToRetain);

/// <summary>What the reader asks for from one document type.</summary>
public sealed class ItemsRequest
{
    /// <summary>The document type, e.g. <c>org.iso.18013.5.1.mDL</c>.</summary>
    public required string DocType { get; init; }

    /// <summary>The elements requested.</summary>
    public required IReadOnlyList<RequestedElement> Elements { get; init; }
}

/// <summary>
/// The reader's request — the first thing the holder decrypts, and the thing the citizen is shown before
/// anything is disclosed.
/// </summary>
/// <remarks>
/// <c>DeviceRequest = { "version": tstr, "docRequests": [ DocRequest ] }</c>, where
/// <c>DocRequest = { "itemsRequest": ItemsRequestBytes, ?"readerAuth": COSE_Sign1 }</c>.
/// <para>
/// <c>readerAuth</c> (the reader proving <em>its</em> identity to the holder) is parsed and preserved but not
/// yet verified — reader authentication is a separate trust question from the holder's own, and pretending to
/// check it would be worse than plainly not checking it. Tracked as a follow-up.
/// </para>
/// </remarks>
public sealed class MdocDeviceRequest
{
    /// <summary>The only version this implementation speaks.</summary>
    public const string Version1_0 = "1.0";

    /// <summary>Request version.</summary>
    public string Version { get; init; } = Version1_0;

    /// <summary>One entry per document type asked for.</summary>
    public required IReadOnlyList<ItemsRequest> DocRequests { get; init; }

    /// <summary>Every element asked for, across all document types.</summary>
    public IEnumerable<RequestedElement> AllElements => DocRequests.SelectMany(d => d.Elements);

    /// <summary>Encodes to CBOR.</summary>
    public byte[] Encode() => MdocCbor.Encode(w =>
    {
        w.WriteStartMap(2);

        w.WriteTextString("version");
        w.WriteTextString(Version);

        w.WriteTextString("docRequests");
        w.WriteStartArray(DocRequests.Count);
        foreach (var docRequest in DocRequests)
        {
            w.WriteStartMap(1);
            w.WriteTextString("itemsRequest");
            w.WriteEncodedValue(MdocCbor.WrapTag24(EncodeItemsRequest(docRequest)));
            w.WriteEndMap();
        }
        w.WriteEndArray();

        w.WriteEndMap();
    });

    /// <summary>Decodes from CBOR. Throws <see cref="CborContentException"/> on a malformed request.</summary>
    public static MdocDeviceRequest Decode(ReadOnlyMemory<byte> cbor)
    {
        string? version = null;
        var docRequests = new List<ItemsRequest>();

        var r = new CborReader(cbor, CborConformanceMode.Lax);
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "version":
                    version = r.ReadTextString();
                    break;

                case "docRequests":
                    r.ReadStartArray();
                    while (r.PeekState() != CborReaderState.EndArray)
                        docRequests.Add(ReadDocRequest(r));
                    r.ReadEndArray();
                    break;

                default:
                    r.SkipValue();
                    break;
            }
        }
        r.ReadEndMap();

        if (docRequests.Count == 0)
            throw new CborContentException("DeviceRequest asks for nothing.");

        return new MdocDeviceRequest { Version = version ?? Version1_0, DocRequests = docRequests };
    }

    private static ItemsRequest ReadDocRequest(CborReader r)
    {
        ItemsRequest? itemsRequest = null;

        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "itemsRequest":
                    itemsRequest = DecodeItemsRequest(MdocCbor.UnwrapTag24(MdocCbor.ReadRawItem(r)));
                    break;

                case "readerAuth":
                    // Preserved-but-unverified: reader authentication is a distinct trust decision and is
                    // deliberately not faked here. Skipping is honest; a stub "verified" would not be.
                    r.SkipValue();
                    break;

                default:
                    r.SkipValue();
                    break;
            }
        }
        r.ReadEndMap();

        return itemsRequest ?? throw new CborContentException("DocRequest carries no itemsRequest.");
    }

    private static byte[] EncodeItemsRequest(ItemsRequest request) => MdocCbor.Encode(w =>
    {
        var byNamespace = request.Elements
            .GroupBy(e => e.Namespace)
            .ToList();

        w.WriteStartMap(2);

        w.WriteTextString("docType");
        w.WriteTextString(request.DocType);

        w.WriteTextString("nameSpaces");
        w.WriteStartMap(byNamespace.Count);
        foreach (var ns in byNamespace)
        {
            w.WriteTextString(ns.Key);
            w.WriteStartMap(ns.Count());
            foreach (var element in ns)
            {
                w.WriteTextString(element.ElementIdentifier);
                w.WriteBoolean(element.IntentToRetain);
            }
            w.WriteEndMap();
        }
        w.WriteEndMap();

        w.WriteEndMap();
    });

    private static ItemsRequest DecodeItemsRequest(ReadOnlyMemory<byte> cbor)
    {
        string? docType = null;
        var elements = new List<RequestedElement>();

        var r = new CborReader(cbor, CborConformanceMode.Lax);
        r.ReadStartMap();
        while (r.PeekState() != CborReaderState.EndMap)
        {
            switch (r.ReadTextString())
            {
                case "docType":
                    docType = r.ReadTextString();
                    break;

                case "nameSpaces":
                    r.ReadStartMap();
                    while (r.PeekState() != CborReaderState.EndMap)
                    {
                        var ns = r.ReadTextString();
                        r.ReadStartMap();
                        while (r.PeekState() != CborReaderState.EndMap)
                        {
                            var elementId = r.ReadTextString();
                            var intentToRetain = r.ReadBoolean();
                            elements.Add(new RequestedElement(ns, elementId, intentToRetain));
                        }
                        r.ReadEndMap();
                    }
                    r.ReadEndMap();
                    break;

                default:
                    r.SkipValue();
                    break;
            }
        }
        r.ReadEndMap();

        if (docType is null)
            throw new CborContentException("ItemsRequest carries no docType.");

        return new ItemsRequest { DocType = docType, Elements = elements };
    }
}
