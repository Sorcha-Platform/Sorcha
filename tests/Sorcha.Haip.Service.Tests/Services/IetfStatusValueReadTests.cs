// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Haip.Service.Services;

using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Feature 192 — the READ half of the IETF Token Status List two-bit encoding.
/// </summary>
/// <remarks>
/// <para>
/// #1492 fixed the WRITE side: <c>IetfStatusListPacker.PackTwoBit</c> projects Sorcha's two W3C
/// lists into a real 2-bit array with <c>0x01</c> INVALID and <c>0x02</c> SUSPENDED. The read side
/// was never fixed with it — <c>ReadBit</c> returned "set" if ANY bit in the entry was set, so the
/// checker could not tell apart the two values the serializer had just gone to the trouble of
/// writing distinctly. Sorcha misread its own conformant output.
/// </para>
/// <para>
/// These tests pin the values against the specification rather than against the packer, because the
/// packer lives in the Blueprint Service and the checker in the HAIP Service — no shared assembly,
/// so nothing but a test can hold the two ends together.
/// </para>
/// </remarks>
public class IetfStatusValueReadTests
{
    // One byte holds four 2-bit entries filled from the LEAST significant bit (RFC 9972 §4.1,
    // #1761): bits 0-1 = entry0, 2-3 = entry1, 4-5 = entry2, 6-7 = entry3.
    // 0b11_10_01_00 → entry0 VALID, entry1 INVALID, entry2 SUSPENDED, entry3 reserved.
    private static readonly byte[] AllFourValues = [0b11_10_01_00];

    private static byte[] SpecLst(string lst)
    {
        var compressed = System.Buffers.Text.Base64Url.DecodeFromChars(lst);
        using var input = new System.IO.MemoryStream(compressed);
        using var z = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new System.IO.MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }

    // #1761 — the spec's OWN worked examples (draft-ietf-oauth-status-list / RFC 9972 §4.1). A test
    // built from hand-written bytes can only pin whatever convention its author assumed — which is
    // how this rail shipped MSB-first with a green suite. These can only pass on the spec's layout.
    [Theory]
    [InlineData(0, CredentialStatusValue.Invalid)]
    [InlineData(1, CredentialStatusValue.Valid)]
    [InlineData(2, CredentialStatusValue.Valid)]
    [InlineData(3, CredentialStatusValue.Invalid)]
    [InlineData(7, CredentialStatusValue.Invalid)]
    [InlineData(10, CredentialStatusValue.Valid)]
    [InlineData(13, CredentialStatusValue.Invalid)]
    [InlineData(15, CredentialStatusValue.Invalid)]
    public void TheSpecsOneBitExample_ReadsAsTheSpecSays(int index, CredentialStatusValue expected)
    {
        // "lst":"eNrbuRgAAhcBXQ" — statuses 1,0,0,1,1,1,0,1, 1,1,0,0,0,1,0,1
        IetfTokenStatusListChecker.ReadBit(SpecLst("eNrbuRgAAhcBXQ"), index, bitsPerEntry: 1)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(0, CredentialStatusValue.Invalid)]    // 1
    [InlineData(1, CredentialStatusValue.Suspended)]  // 2
    [InlineData(2, CredentialStatusValue.Valid)]      // 0
    [InlineData(3, CredentialStatusValue.Unresolved)] // 3 — application-specific
    [InlineData(9, CredentialStatusValue.Suspended)]  // 2
    public void TheSpecsTwoBitExample_ReadsAsTheSpecSays(int index, CredentialStatusValue expected)
    {
        // "lst":"eNo76fITAAPfAgc" — statuses 1,2,0,3, 0,1,0,1, 1,2,3,3
        IetfTokenStatusListChecker.ReadBit(SpecLst("eNo76fITAAPfAgc"), index, bitsPerEntry: 2)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(0, CredentialStatusValue.Valid)]      // 0x00 — in good standing
    [InlineData(1, CredentialStatusValue.Invalid)]    // 0x01 — "revoked, annulled, taken back…"
    [InlineData(2, CredentialStatusValue.Suspended)]  // 0x02 — "temporarily invalid"
    public void EachSpecifiedStatusValueIsReadAsItself(int index, CredentialStatusValue expected)
    {
        IetfTokenStatusListChecker.ReadBit(AllFourValues, idx: index, bitsPerEntry: 2)
            .Should().Be(expected);
    }

    [Fact]
    public void SuspendedIsNotReportedAsInvalid()
    {
        // The single assertion this file exists for. Stated separately from the Theory so a
        // mutation that collapses the two values names THIS test when it fails.
        IetfTokenStatusListChecker.ReadBit(AllFourValues, idx: 2, bitsPerEntry: 2)
            .Should().Be(CredentialStatusValue.Suspended)
            .And.NotBe(CredentialStatusValue.Invalid);
    }

    [Fact]
    public void AnApplicationSpecificValueIsUnresolvedRatherThanAStatus()
    {
        // 0x03+ is reserved by the spec for application-specific use. We cannot interpret it, and
        // guessing "revoked" would be a false accusation against a credential whose issuer may have
        // meant something entirely benign. Unresolved routes it to the fail-closed policy instead,
        // which still refuses — it just stops us claiming to know why.
        IetfTokenStatusListChecker.ReadBit(AllFourValues, idx: 3, bitsPerEntry: 2)
            .Should().Be(CredentialStatusValue.Unresolved);
    }

    [Fact]
    public void AOneBitListCanOnlyEverSayValidOrInvalid()
    {
        // A 1-bit list has no room for SUSPENDED — which is exactly why #1492 had to re-encode
        // rather than relabel when a suspension list appeared.
        byte[] raw = [0b0000_0001];   // entry 0 is the LEAST significant bit

        IetfTokenStatusListChecker.ReadBit(raw, idx: 0, bitsPerEntry: 1)
            .Should().Be(CredentialStatusValue.Invalid);
        IetfTokenStatusListChecker.ReadBit(raw, idx: 1, bitsPerEntry: 1)
            .Should().Be(CredentialStatusValue.Valid);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(16)]
    [InlineData(0)]
    [InlineData(-1)]
    public void AWidthTheSpecDoesNotDefineIsRefusedRatherThanGuessed(int bitsPerEntry)
    {
        // `bits` may only be 1, 2, 4 or 8. Any other value means we have misread the envelope, and
        // reading entries at a made-up stride would invent a status for whichever entry we landed
        // on — the exact failure #1492 was: a declared width that did not match the byte layout.
        IetfTokenStatusListChecker.ReadBit(AllFourValues, idx: 0, bitsPerEntry: bitsPerEntry)
            .Should().Be(CredentialStatusValue.Unresolved);
    }

    [Fact]
    public void ReadingPastTheEndOfTheListIsUnresolved()
    {
        IetfTokenStatusListChecker.ReadBit(AllFourValues, idx: 4, bitsPerEntry: 2)
            .Should().Be(CredentialStatusValue.Unresolved);
    }
}
