// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Models;

/// <summary>
/// A container of validated transactions forming a link in the blockchain for a specific register
/// </summary>
public class Docket
{
    // Identity
    /// <summary>
    /// Unique docket identifier (hash of docket content)
    /// </summary>
    public required string DocketId { get; init; }

    /// <summary>
    /// Target register ID
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Sequential docket number (0 = genesis)
    /// </summary>
    public required long DocketNumber { get; init; }

    // Chain Linkage
    /// <summary>
    /// Hash of previous docket (null for genesis)
    /// </summary>
    public string? PreviousHash { get; init; }

    /// <summary>
    /// SHA256 hash of this docket's content
    /// </summary>
    public required string DocketHash { get; init; }

    // Timestamps
    /// <summary>
    /// When the docket was created
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When consensus was achieved (null if still pending)
    /// </summary>
    public DateTimeOffset? ConsensusAchievedAt { get; set; }

    // Content
    /// <summary>
    /// Transactions included in this docket
    /// </summary>
    public required List<Transaction> Transactions { get; init; }

    /// <summary>
    /// Count of transactions for quick access
    /// </summary>
    public int TransactionCount => Transactions.Count;

    // Consensus
    /// <summary>
    /// Current status of the docket
    /// </summary>
    public DocketStatus Status { get; set; } = DocketStatus.Proposed;

    /// <summary>
    /// Validator that created this docket
    /// </summary>
    public required string ProposerValidatorId { get; init; }

    /// <summary>
    /// Proposer's signature on the docket
    /// </summary>
    public required RegisterSignature ProposerSignature { get; init; }

    /// <summary>
    /// Validator votes on this docket (empty for proposed, populated for confirmed)
    /// </summary>
    public List<ConsensusVote> Votes { get; init; } = new();

    // Metadata
    /// <summary>
    /// Merkle tree root of transaction hashes
    /// </summary>
    public required string MerkleRoot { get; init; }

    /// <summary>
    /// Extensible metadata
    /// </summary>
    public Dictionary<string, string>? Metadata { get; init; }
}
