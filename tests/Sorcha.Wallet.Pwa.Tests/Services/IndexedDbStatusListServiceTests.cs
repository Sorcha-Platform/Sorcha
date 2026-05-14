// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Unit tests for the bitstring decoding + Token Status List 2024 JWT parsing
/// in <see cref="IndexedDbStatusListService"/>. The IJSRuntime + HttpClient
/// surface is exercised in integration; these tests cover the pure functions.
/// </summary>
public sealed class IndexedDbStatusListServiceTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(7, true)]
    [InlineData(8, true)]
    [InlineData(9, false)]
    [InlineData(15, false)]
    public void BitstringHasIndexSet_ReadsBitsLittleEndianWithinByte(int index, bool expected)
    {
        // Byte 0 = 0b1000_0001 (bit 0 set, bit 7 set)
        // Byte 1 = 0b0000_0001 (bit 8 set, in global numbering)
        var bits = new byte[] { 0b1000_0001, 0b0000_0001 };
        IndexedDbStatusListService.BitstringHasIndexSet(bits, index).Should().Be(expected);
    }

    [Fact]
    public void BitstringHasIndexSet_OutOfRangeReturnsFalse()
    {
        IndexedDbStatusListService.BitstringHasIndexSet(new byte[] { 0xFF }, 8).Should().BeFalse();
        IndexedDbStatusListService.BitstringHasIndexSet(new byte[] { 0xFF }, -1).Should().BeFalse();
    }

    [Fact]
    public void ParseStatusListJwt_RoundtripsBitstringAndExpiry()
    {
        // 16-bit bitstring with bit 5 set: byte 0 = 0b0010_0000 = 0x20.
        var bits = new byte[] { 0b0010_0000, 0x00 };
        var compressed = ZlibDeflate(bits);
        var lst = Base64Url.EncodeToString(compressed);

        var payload = new
        {
            iss = "did:sorcha:org:test",
            iat = 1_700_000_000,
            exp = 1_700_086_400,
            sub = "https://verify.test/status/0.statuslist+jwt",
            status_list = new { bits = 1, lst }
        };

        var jwt = $"{Base64Url.EncodeToString(Encoding.UTF8.GetBytes("{}"))}." +
                  $"{Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload))}." +
                  Base64Url.EncodeToString(new byte[] { 0xDE, 0xAD });

        var row = IndexedDbStatusListService.ParseStatusListJwt("https://x", jwt);

        row.Should().NotBeNull();
        row!.Iat.Should().Be(1_700_000_000);
        row.Exp.Should().Be(1_700_086_400);
        IndexedDbStatusListService.BitstringHasIndexSet(row.Bitstring, 5).Should().BeTrue();
        IndexedDbStatusListService.BitstringHasIndexSet(row.Bitstring, 4).Should().BeFalse();
    }

    [Fact]
    public void ParseStatusListJwt_InvalidShapeReturnsNull()
    {
        IndexedDbStatusListService.ParseStatusListJwt("https://x", "not-a-jwt").Should().BeNull();
    }

    private static byte[] ZlibDeflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var deflate = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }
        return ms.ToArray();
    }
}
