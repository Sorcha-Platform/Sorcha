// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine.Evidence;

/// <summary>
/// One recorded consensus vote, reduced to what the Signers check needs: who it claims to be from,
/// and which key it claims to have been made with.
/// </summary>
/// <remarks>
/// Deliberately does not carry the signature bytes. The engine is dependency-free (plan D1) and
/// therefore cannot perform ED25519 or P-256 verification; carrying bytes it cannot check would
/// invite a future reader to assume it does. What the Signers check establishes is stated on
/// <see cref="DocketProvenanceVerifier"/> and surfaced to the reader in
/// <see cref="ProvenanceCheck.CheckedAgainst"/>.
/// </remarks>
public sealed record DocketVoteEvidence
{
    /// <summary>The validator this vote claims to be from.</summary>
    public required string ValidatorId { get; init; }

    /// <summary>
    /// The public key this vote claims to have been made with, in the same encoding the roster uses
    /// (base64). Null when the stored vote carried no signer key.
    /// </summary>
    public string? PublicKey { get; init; }

    /// <summary>The decision recorded, verbatim from the ledger (e.g. "Approve"). For display.</summary>
    public string? Decision { get; init; }
}
