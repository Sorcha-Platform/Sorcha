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
    /// Packs a 1-bit (VALID/INVALID) IETF Token Status List from the revocation list, least
    /// significant bit first (RFC 9972 §4.1).
    /// </summary>
    /// <param name="revocation">The revocation list — a set bit means INVALID.</param>
    /// <param name="entryCount">How many entries to emit.</param>
    public static byte[] PackOneBit(BitstringStatusList revocation, int entryCount)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentOutOfRangeException.ThrowIfNegative(entryCount);

        // #1761 — a 1-bit IETF list is NOT the W3C bitstring's bytes relabelled: W3C packs entries
        // most-significant-bit first, IETF least-significant-bit first, so a pass-through put every
        // entry at the mirror position within its byte. Projected entry by entry instead.
        var packed = new byte[(entryCount + 7) / 8];
        for (var i = 0; i < entryCount; i++)
        {
            if (revocation.GetBit(i))
                packed[i / 8] |= (byte)(1 << (i % 8));
        }
        return packed;
    }

    /// <summary>
    /// Packs <paramref name="entryCount"/> entries into a 2-bit-per-entry array, in the IETF Token
    /// Status List byte layout: entries fill each byte from the LEAST significant bit (#1761).
    /// </summary>
    /// <remarks>
    /// <para>
    /// RFC 9972 (draft-ietf-oauth-status-list) §4.1: "packed into bytes from the least significant
    /// bit ('0') to the most significant bit ('7')". The spec's own worked examples only decode
    /// under that order. This was MSB-first — the W3C Bitstring Status List convention, which is a
    /// different specification — so Sorcha's own reader agreed with it and every external IETF
    /// verifier read the wrong entry. Do not "harmonise" this with <c>BitstringStatusList</c>: the
    /// two specs genuinely differ.
    /// </para>
    /// </remarks>
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

            // Entry i occupies bits [2i, 2i+1] counted from each byte's least significant bit, and
            // its value is read with its own low bit at the lower position. Four entries per byte,
            // never straddling one.
            var bitOffset = (2 * i) % 8;
            packed[(2 * i) / 8] |= (byte)(value << bitOffset);
        }

        return packed;
    }
}
