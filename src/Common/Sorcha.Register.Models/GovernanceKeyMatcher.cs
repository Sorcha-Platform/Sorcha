// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;

namespace Sorcha.Register.Models;

/// <summary>
/// Compares governance public keys by their decoded bytes, tolerating every encoding the platform
/// stores them in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (Feature 189 / R-003).</b> Roster attestations store the public key as
/// <b>standard base64</b> — padded, <c>+/</c> alphabet (e.g. <c>uS780HTirYETFCiao2cn5mWe2bQ7EQCkuPxNfpkpyYc=</c>).
/// The Validator's rights enforcement compared that string against
/// <c>Base64Url.EncodeToString(signature.PublicKey)</c>, which produces <b>unpadded base64url</b> —
/// a different alphabet (<c>-_</c>) and no padding. The two strings cannot be equal for any key
/// requiring padding or containing <c>+</c> or <c>/</c>, so the comparison failed <i>even for the
/// correct key</i>.
/// </para>
/// <para>
/// That defect was independent of, and hidden behind, the separate defect where governance
/// transactions were signed by the node rather than by an organisation on the roster. Both produce
/// the identical symptom — "submitter not found in roster" — so fixing one alone reads as an
/// unfixed bug and costs a deploy cycle to diagnose.
/// </para>
/// <para>
/// <b>Compare bytes, never strings.</b> Encoding is a transport detail; the key is the bytes. The
/// comparison is fixed-time because a roster match is an authorisation decision.
/// </para>
/// </remarks>
public static class GovernanceKeyMatcher
{
    /// <summary>
    /// Decodes a public key from base64 or base64url, padded or unpadded.
    /// </summary>
    /// <param name="encoded">The encoded key, or null/whitespace.</param>
    /// <param name="bytes">The decoded bytes on success.</param>
    /// <returns><c>true</c> when the input decoded successfully; otherwise <c>false</c>.</returns>
    public static bool TryDecode(string? encoded, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        // Normalise the base64url alphabet onto standard base64, then restore padding. Doing it in
        // this order means a single path handles all four shapes (base64/base64url × padded/unpadded).
        var normalised = encoded.Trim().Replace('-', '+').Replace('_', '/');

        var remainder = normalised.Length % 4;
        if (remainder == 2)
        {
            normalised += "==";
        }
        else if (remainder == 3)
        {
            normalised += "=";
        }
        else if (remainder == 1)
        {
            // Not a valid base64 length under any padding rule.
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(normalised);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Determines whether two encoded public keys represent the same key material.
    /// </summary>
    /// <remarks>
    /// Returns <c>false</c> when either input fails to decode — an undecodable key is never a match,
    /// and must never be treated as one.
    /// </remarks>
    public static bool Matches(string? left, string? right)
    {
        if (!TryDecode(left, out var leftBytes) || !TryDecode(right, out var rightBytes))
        {
            return false;
        }

        return Matches(leftBytes, rightBytes);
    }

    /// <summary>
    /// Determines whether an encoded public key matches raw key bytes (the shape a transaction
    /// signature carries).
    /// </summary>
    public static bool Matches(string? encoded, ReadOnlySpan<byte> keyBytes)
    {
        if (!TryDecode(encoded, out var decoded))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(decoded, keyBytes);
    }

    /// <summary>
    /// Determines whether two raw public keys are equal, in fixed time.
    /// </summary>
    public static bool Matches(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
}
