// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Utilities;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Derives a roster member's public key from its <c>did:sorcha:w:{walletAddress}</c> subject
/// (issue #1464).
/// </summary>
/// <remarks>
/// <para>
/// A governance <c>Add</c> used to record <c>PublicKey = string.Empty</c> — "entitled, not yet
/// keyed" — and nothing ever filled it in, because the "first attest" path the comment described
/// was never built. Roster authority is matched BY KEY, so a governance-added member could never
/// sign anything, and transferring ownership to one left the register permanently ungovernable.
/// </para>
/// <para>
/// The key does not need to be asserted, carried on the proposal, or looked up: a classical
/// <c>ws1</c> wallet address is Bech32 over <c>[network][publicKey]</c>, so the key IS the address.
/// Deriving it is
/// </para>
/// <list type="bullet">
///   <item><b>deterministic</b> — a pure function of the subject DID, which is sealed proposal
///   content, so every node folding the same proposal produces byte-identical bytes. That is the
///   constraint that rules out resolving the key from a local participant registry at enactment.</item>
///   <item><b>unforgeable by the proposer</b> — the proposer chooses WHO to add, which is the
///   governance decision the quorum approves; the key then follows mathematically from that
///   identity. Naming someone else's address yields their key, which the proposer cannot sign for.</item>
/// </list>
/// <para>
/// ⚠ <b>PQC (<c>ws2</c>) addresses are deliberately NOT derivable here.</b> A <c>ws2</c> address is
/// Bech32m over <c>[network][SHA-256(publicKey)]</c> — a hash, not the key. Storing that hash in
/// <c>PublicKey</c> would compare a hash against raw signature key bytes in
/// <c>GovernanceKeyMatcher</c>, never match, and silently reproduce the exact ungovernable state
/// this exists to prevent. So a <c>ws2</c> subject returns null and the member stays unkeyed, where
/// the fail-closed guard in <c>GovernanceRosterService.ApplyOperation</c> stops it being promoted.
/// Supporting PQC members needs hash-aware matching, which is a separate change.
/// </para>
/// </remarks>
public static class RosterKeyDerivation
{
    private const string WalletDidPrefix = "did:sorcha:w:";
    private const string ClassicalPrefix = "ws1";

    private static readonly WalletUtilities Wallets = new();

    /// <summary>
    /// Returns the base64 public key encoded in <paramref name="subject"/>'s wallet address, or
    /// null when the subject is not a wallet-bearing DID, is not a classical <c>ws1</c> address, or
    /// does not decode.
    /// </summary>
    /// <remarks>
    /// Null means "cannot be keyed from the identity alone" — never "any key will do". Callers must
    /// leave the attestation unkeyed rather than substitute a placeholder.
    /// </remarks>
    public static string? TryDerivePublicKey(string? subject)
    {
        var address = ExtractWalletAddress(subject);
        if (address is null)
        {
            return null;
        }

        // Only ws1 carries the key itself; see the PQC note above.
        if (!address.StartsWith(ClassicalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var decoded = Wallets.WalletToPublicKey(address);
        if (decoded is null || decoded.Value.PublicKey.Length == 0)
        {
            return null;
        }

        // Base64 — the encoding every other attestation on the roster uses, and the one
        // GovernanceKeyMatcher decodes.
        return Convert.ToBase64String(decoded.Value.PublicKey);
    }

    /// <summary>
    /// Extracts the wallet address from a <c>did:sorcha:w:{address}</c> subject, or null when the
    /// subject is not wallet-bearing.
    /// </summary>
    public static string? ExtractWalletAddress(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject) ||
            !subject.StartsWith(WalletDidPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var address = subject[WalletDidPrefix.Length..];
        return string.IsNullOrWhiteSpace(address) ? null : address;
    }
}
