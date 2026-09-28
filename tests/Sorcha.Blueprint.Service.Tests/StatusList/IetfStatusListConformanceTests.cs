// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Services;

using Xunit;

namespace Sorcha.Blueprint.Service.Tests.StatusList;

/// <summary>
/// IETF Token Status List conformance for the <c>statuslist+jwt</c> rail.
/// </summary>
/// <remarks>
/// <para>
/// IETF and W3C model suspension differently, and the difference is the whole point of these tests.
/// W3C uses a SEPARATE LIST per purpose. IETF uses ONE list whose entries are <c>bits</c> wide, with
/// suspension as a distinct VALUE:
/// </para>
/// <list type="bullet">
///   <item><c>0x00</c> VALID</item>
///   <item><c>0x01</c> INVALID — "revoked, annulled, taken back, recalled or cancelled"</item>
///   <item><c>0x02</c> SUSPENDED — "temporarily invalid… usually temporary"</item>
/// </list>
/// <para>
/// Expressing SUSPENDED therefore needs at least 2 bits per entry, and the byte array must actually
/// BE 2 bits per entry. Sorcha's internal <see cref="BitstringStatusList"/> is 1 bit per entry, and
/// the serve path previously declared <c>bits = 2</c> for the suspension list while handing over
/// that 1-bit array unchanged. A conformant reader takes entry N from bits 2N..2N+1, so every index
/// was misread — and because a set bit anywhere in the group reads as "not valid", revoking entry 1
/// made entry 0 report as revoked. It did not merely mislabel a status; it invented one for a
/// different credential.
/// </para>
/// </remarks>
public class IetfStatusListConformanceTests
{
    private const string Issuer = "ws11qissuer";
    private const string Register = "2141b08339d34c27824536ec250b025e";

    [Fact]
    public void OneBitDataDeclaredAsTwoBits_MisreadsEveryIndex()
    {
        // Pins the defect itself, so the fix cannot be undone quietly. Entry 1 is revoked and
        // nothing else is; read back as a 2-bit list, entry 0 comes out as not-valid.
        var oneBitPerEntry = new byte[] { 0b0000_0010 };   // only our entry 1 is set (IETF: LSB-first, #1761)

        var entry0AsTwoBit = ReadEntry(oneBitPerEntry, index: 0, bitsPerEntry: 2);

        entry0AsTwoBit.Should().NotBe(0,
            "entry 0 is untouched, but a 2-bit reader swallows entry 1's bit — which is exactly why "
            + "the byte array must be re-encoded rather than relabelled");
    }

    [Fact]
    public void TwoBitEncodingPlacesEachStatusInItsOwnEntry()
    {
        // The fix: build a genuine 2-bit array from the two internal 1-bit lists.
        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        var suspension = BitstringStatusList.Create(Issuer, Register, "suspension");

        revocation.SetBit(1, true);   // entry 1 is revoked
        suspension.SetBit(2, true);   // entry 2 is suspended

        var packed = IetfStatusListPacker.PackTwoBit(revocation, suspension, entryCount: 4);

        ReadEntry(packed, 0, 2).Should().Be(0, "entry 0 is untouched → VALID");
        ReadEntry(packed, 1, 2).Should().Be(1, "entry 1 is revoked → INVALID (0x01)");
        ReadEntry(packed, 2, 2).Should().Be(2, "entry 2 is suspended → SUSPENDED (0x02)");
        ReadEntry(packed, 3, 2).Should().Be(0, "entry 3 is untouched → VALID");
    }

    [Fact]
    public void RevocationWinsOverSuspensionForTheSameEntry()
    {
        // Revocation is terminal; a credential that is both should read INVALID, not SUSPENDED.
        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        var suspension = BitstringStatusList.Create(Issuer, Register, "suspension");
        revocation.SetBit(0, true);
        suspension.SetBit(0, true);

        var packed = IetfStatusListPacker.PackTwoBit(revocation, suspension, entryCount: 2);

        ReadEntry(packed, 0, 2).Should().Be(1, "INVALID outranks SUSPENDED — revocation is terminal");
    }

    [Fact]
    public void AOneBitList_IsProjectedIntoTheIetfLayout_NotPassedThrough()
    {
        // #1761 — this used to assert the W3C bytes WERE the IETF list. They are not: W3C packs
        // most-significant-bit first and IETF least-significant-bit first, so a pass-through put
        // every entry at its mirror position. A W3C bit set at index 3 must land at IETF bit 3.
        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        revocation.SetBit(3, true);

        var packed = IetfStatusListPacker.PackOneBit(revocation, entryCount: 16);

        ReadEntry(packed, 3, 1).Should().Be(1, "entry 3 is revoked");
        ReadEntry(packed, 2, 1).Should().Be(0, "entry 2 is not");
        packed[0].Should().Be(0b0000_1000);
    }

    [Fact]
    public void ThePackerReproducesTheSpecsOwnOneBitExample()
    {
        // RFC 9972 §4.1 worked example: statuses 1,0,0,1,1,1,0,1, 1,1,0,0,0,1,0,1 pack to 0xB9 0xA3.
        // Hand-written expected bytes can only pin the author's assumption; the spec's cannot.
        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        int[] statuses = [1, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 1, 0, 1];
        for (var i = 0; i < statuses.Length; i++)
            revocation.SetBit(i, statuses[i] == 1);

        IetfStatusListPacker.PackOneBit(revocation, statuses.Length).Should().Equal(0xB9, 0xA3);
    }

    [Fact]
    public void TwoBitEntriesFillEachByteFromTheLeastSignificantBit()
    {
        // entry0 VALID, entry1 INVALID (0x01), entry2 SUSPENDED (0x02), entry3 VALID
        //   → bits 0-1 = 00, 2-3 = 01, 4-5 = 10, 6-7 = 00 → 0b00_10_01_00
        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        var suspension = BitstringStatusList.Create(Issuer, Register, "suspension");
        revocation.SetBit(1, true);
        suspension.SetBit(2, true);

        IetfStatusListPacker.PackTwoBit(revocation, suspension, entryCount: 4).Should().Equal(0b00_10_01_00);
    }

    /// <summary>
    /// Reads the unsigned value of entry <paramref name="index"/> in the IETF Token Status List
    /// layout: entries fill each byte from the least significant bit (RFC 9972 §4.1, #1761).
    /// </summary>
    private static int ReadEntry(byte[] raw, int index, int bitsPerEntry)
    {
        var value = 0;
        var start = index * bitsPerEntry;

        for (var i = 0; i < bitsPerEntry; i++)
        {
            var bit = start + i;
            var set = (raw[bit / 8] & (1 << (bit % 8))) != 0;
            value |= (set ? 1 : 0) << i;   // the entry's low bit sits at the lower position
        }

        return value;
    }
}
