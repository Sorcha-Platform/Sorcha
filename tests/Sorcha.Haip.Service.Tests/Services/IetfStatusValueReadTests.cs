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
    // One byte holds four 2-bit entries, MSB-first: [e0 e0][e1 e1][e2 e2][e3 e3].
    // 0b00_01_10_11 → entry0 VALID, entry1 INVALID, entry2 SUSPENDED, entry3 reserved.
    private static readonly byte[] AllFourValues = [0b00_01_10_11];

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
        byte[] raw = [0b1000_0000];

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
