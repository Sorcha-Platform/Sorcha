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
        var oneBitPerEntry = new byte[] { 0b0100_0000 };   // only our entry 1 is set

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
    public void APurelyRevocationListStaysOneBitAndIsUnchanged()
    {
        // bits=1 expresses VALID/INVALID only, which is conformant and needs no re-encoding — so the
        // common case keeps its cheap pass-through.
        var revocation = BitstringStatusList.Create(Issuer, Register, "revocation");
        revocation.SetBit(3, true);

        var raw = BitstringStatusList.DecompressBitstring(revocation.EncodedList);

        ReadEntry(raw, 3, 1).Should().Be(1, "entry 3 is revoked");
        ReadEntry(raw, 2, 1).Should().Be(0, "entry 2 is not");
    }

    /// <summary>
    /// Reads the unsigned value of entry <paramref name="index"/>, MSB-first within each byte —
    /// the layout both IETF and W3C use.
    /// </summary>
    private static int ReadEntry(byte[] raw, int index, int bitsPerEntry)
    {
        var value = 0;
        var start = index * bitsPerEntry;

        for (var i = 0; i < bitsPerEntry; i++)
        {
            var bit = start + i;
            var set = (raw[bit / 8] & (1 << (7 - (bit % 8)))) != 0;
            value = (value << 1) | (set ? 1 : 0);
        }

        return value;
    }
}
