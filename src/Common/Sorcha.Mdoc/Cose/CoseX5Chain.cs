// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Formats.Cbor;
using System.Security.Cryptography.Cose;

namespace Sorcha.Mdoc.Cose;

/// <summary>
/// Reads and writes the COSE <c>x5chain</c> header (RFC 9360 label 33) in the unprotected bucket
/// of a COSE message (feature 135). A single certificate encodes as a <c>bstr</c>; multiple
/// certificates encode as an array of <c>bstr</c>, leaf-first. The BCL has no named constant for
/// label 33, so this centralises <c>new CoseHeaderLabel(33)</c> + <c>CoseHeaderValue</c> handling.
/// </summary>
public static class CoseX5Chain
{
    /// <summary>The COSE header label for x5chain (RFC 9360).</summary>
    public static readonly CoseHeaderLabel Label = new(33);

    /// <summary>
    /// Reads the leaf-first DER certificate chain from <paramref name="message"/>'s unprotected
    /// headers, or null when no x5chain header is present (or it is malformed).
    /// </summary>
    public static IReadOnlyList<byte[]>? Read(CoseMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.UnprotectedHeaders.TryGetValue(Label, out var headerValue))
            return null;

        try
        {
            var reader = new CborReader(headerValue.EncodedValue, CborConformanceMode.Lax);
            switch (reader.PeekState())
            {
                case CborReaderState.ByteString:
                    return [reader.ReadByteString()];

                case CborReaderState.StartArray:
                    var count = reader.ReadStartArray();
                    var chain = new List<byte[]>(count ?? 1);
                    while (reader.PeekState() != CborReaderState.EndArray)
                        chain.Add(reader.ReadByteString());
                    reader.ReadEndArray();
                    return chain.Count > 0 ? chain : null;

                default:
                    return null;
            }
        }
        catch (CborContentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a <see cref="CoseHeaderValue"/> for label 33 from a leaf-first DER chain: a single
    /// cert as a <c>bstr</c>, multiple as an array of <c>bstr</c>.
    /// </summary>
    public static CoseHeaderValue Encode(IReadOnlyList<byte[]> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
            throw new ArgumentException("x5chain must contain at least one certificate.", nameof(chain));

        var writer = new CborWriter(CborConformanceMode.Canonical);
        if (chain.Count == 1)
        {
            writer.WriteByteString(chain[0]);
        }
        else
        {
            writer.WriteStartArray(chain.Count);
            foreach (var cert in chain)
                writer.WriteByteString(cert);
            writer.WriteEndArray();
        }

        return CoseHeaderValue.FromEncodedValue(writer.Encode());
    }
}
