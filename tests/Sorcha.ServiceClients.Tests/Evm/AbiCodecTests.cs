// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;
using Sorcha.ServiceClients.Evm;

namespace Sorcha.ServiceClients.Tests.Evm;

public class AbiCodecTests
{
    [Fact]
    public void EventTopic_Erc20Transfer_MatchesCanonicalHash()
    {
        // Anchors the keccak-of-signature machinery against a universally-known topic hash.
        AbiCodec.EventTopic("Transfer(address,address,uint256)")
            .Should().Be("0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef");
    }

    [Fact]
    public void Selector_Erc20Transfer_MatchesCanonicalSelector()
    {
        // The famous ERC-20 transfer(address,uint256) selector 0xa9059cbb.
        AbiCodec.Selector("transfer(address,uint256)").Should().Be("0xa9059cbb");
    }

    [Fact]
    public void Selector_And_Topics_Erc1056_AreWellFormedAndStable()
    {
        AbiCodec.Selector("changed(address)").Should().MatchRegex("^0x[0-9a-f]{8}$");
        AbiCodec.Selector("identityOwner(address)").Should().MatchRegex("^0x[0-9a-f]{8}$");
        AbiCodec.EventTopic("DIDOwnerChanged(address,address,uint256)").Should().MatchRegex("^0x[0-9a-f]{64}$");
        AbiCodec.EventTopic("DIDDelegateChanged(address,bytes32,address,uint256,uint256)").Should().MatchRegex("^0x[0-9a-f]{64}$");
        AbiCodec.EventTopic("DIDAttributeChanged(address,bytes32,bytes,uint256,uint256)").Should().MatchRegex("^0x[0-9a-f]{64}$");

        // Deterministic
        AbiCodec.Selector("changed(address)").Should().Be(AbiCodec.Selector("changed(address)"));
    }

    [Fact]
    public void Pad32Topic_LeftPadsAddressToThirtyTwoBytes()
    {
        var topic = AbiCodec.Pad32Topic("0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B");
        topic.Should().Be("0x000000000000000000000000ab5801a7d398351b8be11c439e05c5b3259aec9b");
        topic.Length.Should().Be(66);
    }

    [Fact]
    public void EncodeDecodeAddress_RoundTrips()
    {
        const string addr = "0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B";
        var word = AbiCodec.EncodeAddress(addr);

        word.Length.Should().Be(32);
        AbiCodec.DecodeAddress(word).Should().Be(addr.ToLowerInvariant());
    }

    [Fact]
    public void DecodeUInt_ReadsBigEndianWord()
    {
        var word = new byte[32];
        word[31] = 0xff; // 255
        AbiCodec.DecodeUInt(word).Should().Be(new BigInteger(255));

        word[30] = 0x01; // 0x01ff = 511
        AbiCodec.DecodeUInt(word).Should().Be(new BigInteger(511));
    }

    [Fact]
    public void DecodeDynamicBytes_ReadsOffsetLengthAndValue()
    {
        // ABI data with one dynamic `bytes` at head word 0: head=[offset=0x20], tail=[len=3, "abc"].
        var hex =
            "0000000000000000000000000000000000000000000000000000000000000020" + // offset = 32
            "0000000000000000000000000000000000000000000000000000000000000003" + // length = 3
            "6162630000000000000000000000000000000000000000000000000000000000";  // "abc" padded
        var data = Convert.FromHexString(hex);

        var value = AbiCodec.DecodeDynamicBytes(data, headWordIndex: 0);
        System.Text.Encoding.ASCII.GetString(value).Should().Be("abc");
    }

    [Fact]
    public void DecodeBytes32Ascii_TrimsNulPadding()
    {
        // "veriKey" right-zero-padded in a 32-byte word.
        var word = new byte[32];
        "veriKey"u8.CopyTo(word);
        AbiCodec.DecodeBytes32Ascii(word).Should().Be("veriKey");
    }
}
