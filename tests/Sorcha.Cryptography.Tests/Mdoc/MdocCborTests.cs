// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Cryptography.Mdoc.Cbor;

using Xunit;

namespace Sorcha.Cryptography.Tests.Mdoc;

/// <summary>
/// Feature 135 / T036 — CBOR tag-24 (#6.24(bstr .cbor X)) wrapping primitives. Tag-24 wrapping is
/// load-bearing: digests and signatures are computed over the tagged outer bytes, so the helpers
/// must preserve the inner bytes verbatim and never re-encode.
/// </summary>
public class MdocCborTests
{
    [Fact]
    public void WrapTag24_PrefixesTagAndByteString()
    {
        var inner = new byte[] { 0xA0 }; // an empty CBOR map
        var tagged = MdocCbor.WrapTag24(inner);

        // 0xD8 0x18 = tag(24); 0x41 = bstr of length 1; then the inner byte.
        tagged.Should().Equal(0xD8, 0x18, 0x41, 0xA0);
    }

    [Fact]
    public void UnwrapTag24_ReturnsInnerBytesVerbatim()
    {
        var inner = new byte[] { 0xA1, 0x01, 0x02 }; // {1: 2}
        var tagged = MdocCbor.WrapTag24(inner);

        MdocCbor.UnwrapTag24(tagged).Should().Equal(inner);
    }

    [Fact]
    public void WrapUnwrap_RoundTrips_LargerPayload()
    {
        var inner = Enumerable.Range(0, 200).Select(i => (byte)i).ToArray();
        var roundTripped = MdocCbor.UnwrapTag24(MdocCbor.WrapTag24(inner));
        roundTripped.Should().Equal(inner);
    }

    [Fact]
    public void UnwrapTag24_RejectsUntaggedValue()
    {
        var notTagged = new byte[] { 0x41, 0xA0 }; // bstr without the tag
        var act = () => MdocCbor.UnwrapTag24(notTagged);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void EncodeBytes_DecodeBytes_RoundTripByteString()
    {
        var value = new byte[] { 1, 2, 3, 4, 5 };
        var encoded = MdocCbor.Encode(w => w.WriteByteString(value));
        MdocCbor.Decode(encoded, r => r.ReadByteString()).Should().Equal(value);
    }
}
