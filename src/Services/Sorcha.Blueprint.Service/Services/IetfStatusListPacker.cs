// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Service.Services;

/// <summary>
/// Projects Sorcha's two 1-bit status lists into the single multi-bit array IETF Token Status List
/// expects.
/// </summary>
/// <remarks>
/// <para>
/// The two specifications model suspension differently and both are supported, so the internal
/// representation cannot match both. W3C Bitstring Status List uses a SEPARATE LIST per purpose —
/// which is what Sorcha stores. IETF uses ONE list of <c>bits</c>-wide entries with suspension as a
/// distinct VALUE:
/// </para>
/// <list type="bullet">
///   <item><c>0x00</c> VALID</item>
///   <item><c>0x01</c> INVALID — revoked, annulled, recalled or cancelled</item>
///   <item><c>0x02</c> SUSPENDED — temporarily invalid</item>
/// </list>
/// <para>
/// So the IETF rail is a PROJECTION of the two W3C lists, not a relabelling of one. Handing a 1-bit
/// array over while declaring <c>bits: 2</c> is not a cosmetic error: a conformant reader takes
/// entry N from bits 2N..2N+1, so revoking entry 1 makes entry 0 report as not-valid. It fabricates
/// a status for a credential nobody touched.
/// </para>
/// </remarks>
public static class IetfStatusListPacker
{
    /// <summary>IETF status value for a credential in good standing.</summary>
    public const byte Valid = 0x00;

    /// <summary>IETF status value for a revoked credential.</summary>
    public const byte Invalid = 0x01;

    /// <summary>IETF status value for a suspended credential.</summary>
    public const byte Suspended = 0x02;

    /// <summary>
    /// Packs <paramref name="entryCount"/> entries into a 2-bit-per-entry array, MSB-first within
    /// each byte.
    /// </summary>
    /// <param name="revocation">The revocation list — a set bit means INVALID.</param>
    /// <param name="suspension">The suspension list — a set bit means SUSPENDED.</param>
    /// <param name="entryCount">How many entries to emit.</param>
    /// <remarks>
    /// Revocation outranks suspension for the same entry. Revocation is terminal in both specs, so a
    /// credential that is somehow both must read INVALID; reporting SUSPENDED would imply it could
    /// come back.
    /// </remarks>
    public static byte[] PackTwoBit(
        BitstringStatusList revocation,
        BitstringStatusList suspension,
        int entryCount)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentNullException.ThrowIfNull(suspension);
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);

        var packed = new byte[(entryCount * 2 + 7) / 8];

        for (var i = 0; i < entryCount; i++)
        {
            var value = revocation.GetBit(i) ? Invalid
                      : suspension.GetBit(i) ? Suspended
                      : Valid;

            if (value == Valid) continue;

            // Entry i occupies bits [2i, 2i+1]; write the 2-bit value MSB-first.
            WriteBit(packed, 2 * i, (value & 0b10) != 0);
            WriteBit(packed, 2 * i + 1, (value & 0b01) != 0);
        }

        return packed;
    }

    private static void WriteBit(byte[] buffer, int bitPosition, bool value)
    {
        if (!value) return;

        var byteIndex = bitPosition / 8;
        var bitIndex = bitPosition % 8;
        buffer[byteIndex] |= (byte)(1 << (7 - bitIndex));
    }
}
