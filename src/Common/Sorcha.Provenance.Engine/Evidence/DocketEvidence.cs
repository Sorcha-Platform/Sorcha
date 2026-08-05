// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine.Evidence;

/// <summary>
/// Everything the engine is told about one sealed docket, assembled by the service from the
/// register's stored docket header.
/// </summary>
/// <remarks>
/// <para>
/// The engine cannot reach storage (plan D1), so this shape is the honest cost of that boundary. It
/// is a snapshot of stored evidence, never a live handle.
/// </para>
/// <para>
/// <b>Three of the fields below are nullable, and every one of them means the same thing: the check
/// that consumes it reports <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/>,
/// never <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Failed"/>.</b> That is the
/// shape of the whole feature. A node holding part of a register, a deployment with one validator,
/// and a docket sealed before the platform kept a given piece of evidence are all ordinary healthy
/// states; reporting them as failure would tell an operator their ledger had been tampered with.
/// </para>
/// </remarks>
public sealed record DocketEvidence
{
    /// <summary>Docket height. Zero is the genesis docket.</summary>
    public required ulong DocketNumber { get; init; }

    /// <summary>This docket's own hash, as stored.</summary>
    public required string Hash { get; init; }

    /// <summary>
    /// The predecessor hash this docket claims, as stored on its header.
    /// </summary>
    /// <remarks>
    /// Absent (null or blank) on the genesis docket, which has no predecessor to claim — a genesis
    /// docket's Chain check is <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/>
    /// by nature, not <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Failed"/>.
    /// </remarks>
    public string? ClaimedPreviousHash { get; init; }

    /// <summary>
    /// The hash of the docket actually preceding this one, as held by this node.
    /// </summary>
    /// <remarks>
    /// <b>Null when this node does not hold the predecessor.</b> A partial replica is a normal
    /// deployment, not a compromised one: the Chain check reports
    /// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/> with a reason
    /// naming the missing docket, and MUST NOT report
    /// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Failed"/>.
    /// </remarks>
    public string? PredecessorHash { get; init; }

    /// <summary>The transaction ids sealed in this docket, as stored.</summary>
    public required IReadOnlyList<string> TransactionIds { get; init; }

    /// <summary>
    /// The Merkle root the proposing validator committed over this docket's transaction set.
    /// </summary>
    /// <remarks>
    /// <b>Null for dockets sealed before Feature 187</b>, which discarded the sealed commitment at
    /// write time. Without it there is nothing to compare a recomputation against, so the Seal check
    /// reports <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/> with a
    /// reason. Treating a missing commitment as tampering would mark every historical docket on the
    /// network as compromised.
    /// </remarks>
    public string? SealedMerkleRoot { get; init; }

    /// <summary>
    /// The validator that proposed this docket.
    /// </summary>
    /// <remarks>
    /// Null on dockets sealed before Feature 187 made the proposer a first-class field ⇒ the Proposer
    /// check is <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/>.
    /// </remarks>
    public string? ProposerValidatorId { get; init; }

    /// <summary>
    /// The consensus votes recorded for this docket.
    /// </summary>
    /// <remarks>
    /// <b>Empty is valid and expected</b>, not an error and not missing data. A node running without
    /// a consensus engine — single-validator mode, which is what local development and every current
    /// single-node deployment use — seals dockets with no votes to record. There is then no quorum
    /// evidence to check, so the Signers check reports
    /// <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Unverified"/> with a reason and
    /// MUST NOT report <see cref="Sorcha.Verification.Abstractions.VerificationStatus.Verified"/>.
    /// A green tick here on a single-validator node is the most serious defect this feature can have.
    /// </remarks>
    public IReadOnlyList<DocketVoteEvidence> Votes { get; init; } = [];
}
