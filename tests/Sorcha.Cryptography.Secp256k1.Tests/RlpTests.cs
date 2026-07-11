// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;
using System.Text;

namespace Sorcha.Cryptography.Secp256k1.Tests;

/// <summary>
/// RLP encoder unit tests against the canonical vectors from the Ethereum RLP specification (Feature 182).
/// The scalar/minimal-integer rule (zero → <c>0x80</c>, leading zeros stripped) is the correctness core.
/// </summary>
public class RlpTests
{
    private static string Hex(byte[] b) => Convert.ToHexStringLower(b);

    [Fact]
    public void EncodeString_Empty_Is0x80() => Hex(Rlp.EncodeString(ReadOnlySpan<byte>.Empty)).Should().Be("80");

    [Fact]
    public void EncodeString_SingleZeroByte_IsItself() => Hex(Rlp.EncodeString(new byte[] { 0x00 })).Should().Be("00");

    [Fact]
    public void EncodeString_SingleByteBelow0x80_IsItself() => Hex(Rlp.EncodeString(new byte[] { 0x0f })).Should().Be("0f");

    [Fact]
    public void EncodeString_Dog_MatchesSpec() =>
        Hex(Rlp.EncodeString(Encoding.ASCII.GetBytes("dog"))).Should().Be("83646f67");

    [Fact]
    public void EncodeString_LongString_UsesTwoByteLengthPrefix()
    {
        // 56-byte string → 0xb7+1 = 0xb8, then length 0x38 (=56). Canonical RLP spec example.
        var s = "Lorem ipsum dolor sit amet, consectetur adipisicing elit";
        s.Length.Should().Be(56);
        Hex(Rlp.EncodeString(Encoding.ASCII.GetBytes(s))).Should().StartWith("b838");
    }

    [Fact]
    public void EncodeScalar_Zero_IsEmptyString0x80() => Hex(Rlp.EncodeScalar(BigInteger.Zero)).Should().Be("80");

    [Fact]
    public void EncodeScalar_Fifteen_Is0x0f() => Hex(Rlp.EncodeScalar(15)).Should().Be("0f");

    [Fact]
    public void EncodeScalar_1024_StripsToTwoBytes() => Hex(Rlp.EncodeScalar(1024)).Should().Be("820400");

    [Fact]
    public void EncodeScalar_Negative_Throws() =>
        ((Action)(() => Rlp.EncodeScalar(BigInteger.MinusOne))).Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public void EncodeList_Empty_Is0xc0() => Hex(Rlp.EncodeList()).Should().Be("c0");

    [Fact]
    public void EncodeList_CatDog_MatchesSpec()
    {
        var encoded = Rlp.EncodeList(
            Rlp.EncodeString(Encoding.ASCII.GetBytes("cat")),
            Rlp.EncodeString(Encoding.ASCII.GetBytes("dog")));
        Hex(encoded).Should().Be("c88363617483646f67");
    }
}
