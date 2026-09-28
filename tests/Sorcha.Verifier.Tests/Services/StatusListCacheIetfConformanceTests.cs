// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading.Tasks;

using FluentAssertions;

using Sorcha.Verifier.Engine;

using Xunit;

namespace Sorcha.Verifier.Tests.Services;

/// <summary>
/// Issue #1499. Pins <see cref="StatusListCache"/>'s bit read against the IETF Token Status List
/// draft's own worked examples (draft-ietf-oauth-status-list-09 §4.1), and against the "bits" field
/// it previously ignored.
/// </summary>
/// <remarks>
/// <para>
/// #1499 was filed on the belief that this rail's bit order was backwards (LSB-first) against "both
/// specs" (MSB-first). Re-verified against the actual IETF draft rather than the sibling IETF rail's
/// comments: draft-ietf-oauth-status-list-09 §4.1 states entries are "packed into bytes from the
/// least significant bit ('0') to the most significant bit ('7')" — i.e. LSB-first — and the draft's
/// own worked examples only reconstruct under that ordering. Decompressing the draft's literal
/// base64url <c>lst</c> values independently confirms this:
/// </para>
/// <list type="bullet">
///   <item><c>eNrbuRgAAhcBXQ</c> (bits=1, 16 entries) → raw bytes <c>0xb9 0xa3</c></item>
///   <item><c>eNo76fITAAPfAgc</c> (bits=2, 12 entries) → raw bytes <c>0xc9 0x44 0xf9</c></item>
/// </list>
/// <para>
/// So the bit-ORDER half of #1499's diagnosis does not hold for this spec — LSB-first was already
/// correct here, and W3C Bitstring Status List's MSB-first convention (<c>BitstringStatusList</c>)
/// does not apply to this rail. What WAS real and IS fixed: <see cref="StatusListCache.CheckAsync"/>
/// hardcoded a 1-bit stride regardless of the envelope's declared <c>status_list.bits</c>, so a
/// <c>bits=2</c> (or wider) list had every entry index N read from bit N instead of bits
/// <c>[2N, 2N+1]</c> — the exact aliasing failure #1492 fixed on the write side of the sibling IETF
/// rail. <see cref="Bits2_Index1_WasMisreadAsActiveUnderTheOldOneBitStride"/> demonstrates the
/// concrete misread.
/// </para>
/// </remarks>
public sealed class StatusListCacheIetfConformanceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-05-25T12:00:00Z");

    // draft-ietf-oauth-status-list-09 §4.1 worked example (bits=1, 16 entries). Status values,
    // index 0..15: 1,0,0,1,1,1,0,1, 1,1,0,0,0,1,0,1 — the spec states this packs to bytes 0xb9, 0xa3.
    private static readonly byte[] SpecVectorBits1 = [0xb9, 0xa3];
    private static readonly int[] SpecStatusBits1 =
        [1, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 1, 0, 1];

    // draft-ietf-oauth-status-list-09 §4.1 worked example (bits=2, 12 entries). Status values,
    // index 0..11: 1,2,0,3,0,1,0,1,1,2,3,3 — the spec states this packs to bytes 0xc9, 0x44, 0xf9.
    private static readonly byte[] SpecVectorBits2 = [0xc9, 0x44, 0xf9];
    private static readonly int[] SpecStatusBits2 = [1, 2, 0, 3, 0, 1, 0, 1, 1, 2, 3, 3];

    [Fact]
    public void SpecVectorBits1_DecompressesToTheStatedBytes()
    {
        // Sanity check on the golden vector itself, independent of StatusListCache: decode the
        // spec's OWN base64url `lst` string and confirm it is exactly what the spec claims.
        StatusListTestHelpers.ZlibDecompressBase64Url("eNrbuRgAAhcBXQ").Should().Equal(SpecVectorBits1);
    }

    [Fact]
    public void SpecVectorBits2_DecompressesToTheStatedBytes()
    {
        StatusListTestHelpers.ZlibDecompressBase64Url("eNo76fITAAPfAgc").Should().Equal(SpecVectorBits2);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    public async Task Bits1_GoldenVector_EveryIndexMatchesTheSpecsStatedStatus(int index)
    {
        var key = StatusListTestHelpers.NewKey();
        var cache = StatusListTestHelpers.BuildCache(
            StatusListTestHelpers.BuildSignedList(SpecVectorBits1, Now.AddHours(24), key, statusListBits: 1),
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now));

        var expected = SpecStatusBits1[index] == 0 ? StatusListVerdict.Active : StatusListVerdict.Revoked;

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, index, StatusListTestHelpers.Issuer))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public async Task Bits2_GoldenVector_EveryIndexMatchesTheSpecsStatedStatus(int index)
    {
        var key = StatusListTestHelpers.NewKey();
        var cache = StatusListTestHelpers.BuildCache(
            StatusListTestHelpers.BuildSignedList(SpecVectorBits2, Now.AddHours(24), key, statusListBits: 2),
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now));

        var expected = SpecStatusBits2[index] == 0 ? StatusListVerdict.Active : StatusListVerdict.Revoked;

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, index, StatusListTestHelpers.Issuer))
            .Should().Be(expected);
    }

    [Fact]
    public async Task Bits2_Index1_WasMisreadAsActiveUnderTheOldOneBitStride()
    {
        // The single assertion this file exists for. Index 1's real value is 0b10 (non-zero →
        // Revoked), but the pre-#1499 code always tested bit `index` of the raw bitstring regardless
        // of `bits` — for index=1 that is bit 1 of byte 0 (0xc9 = 0b1100_1001), which is 0. The old
        // code would have reported Active for a credential the list actually marks not-good-standing.
        var key = StatusListTestHelpers.NewKey();
        var cache = StatusListTestHelpers.BuildCache(
            StatusListTestHelpers.BuildSignedList(SpecVectorBits2, Now.AddHours(24), key, statusListBits: 2),
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now));

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 1, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Revoked)
            .And.NotBe(StatusListVerdict.Active);
    }

    [Fact]
    public async Task ParseJwt_NoBitsClaim_DefaultsToOne()
    {
        // Back-compat: every list this rail has published before #1499 either omits `bits` or sets
        // it to 1 explicitly. Both MUST keep reading as 1-bit entries — no republish is required.
        var jwt = StatusListTestHelpers.BuildSignedList(
            SpecVectorBits1, Now.AddHours(24), StatusListTestHelpers.NewKey(), omitBits: true);

        var parsed = StatusListCache.ParseJwt(jwt);

        parsed.Bits.Should().Be(1);
    }

    [Fact]
    public async Task CheckAsync_UnsupportedBitsWidth_FailsClosed()
    {
        // The spec permits exactly 1, 2, 4 or 8. Anything else means the envelope was misread, and
        // guessing a layout would invent a status for whichever entry we happened to land on.
        var key = StatusListTestHelpers.NewKey();
        var cache = StatusListTestHelpers.BuildCache(
            StatusListTestHelpers.BuildSignedList(SpecVectorBits1, Now.AddHours(24), key, statusListBits: 3),
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now));

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 0, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Unverifiable);
    }

    [Fact]
    public async Task CheckAsync_Bits2_IndexPastEndOfList_FailsClosed()
    {
        // 3 bytes * 8 bits / 2 bits-per-entry = 12 entries (index 0..11); index 12 is past the end.
        var key = StatusListTestHelpers.NewKey();
        var cache = StatusListTestHelpers.BuildCache(
            StatusListTestHelpers.BuildSignedList(SpecVectorBits2, Now.AddHours(24), key, statusListBits: 2),
            StatusListTestHelpers.ResolverFor(key.PublicJwk),
            new FixedTimeProvider(Now));

        (await cache.CheckAsync(StatusListTestHelpers.ListUri, 12, StatusListTestHelpers.Issuer))
            .Should().Be(StatusListVerdict.Unverifiable);
    }
}
