// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Models;

/// <summary>
/// A sealed docket as PERSISTED by the Register Service: the header over a set of transactions that
/// are stored as separate documents, referenced here by <see cref="TransactionIds"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Named <c>DocketHeader</c>, not <c>Docket</c> (Feature 187 / #1371).</b> It used to be
/// <c>Docket</c>, colliding with <c>Sorcha.Validator.Service.Models.Docket</c> — a genuinely
/// different shape (the consensus working set, which carries its transactions and votes inline).
/// The two are not interchangeable, and the shared name meant every file touching both namespaces
/// needed a disambiguating alias, which is how they came to be conflated in the first place.
/// </para>
/// <para>
/// The normalisation is deliberate and stays: transactions are separate documents; this is the
/// header over them. Project the validator's working docket onto the ledger contract via
/// <c>DocketRegisterProjection</c> — never by casting or re-declaring.
/// </para>
/// </remarks>
public class DocketHeader
{
    /// <summary>
    /// Docket identifier (docket height)
    /// </summary>
    public ulong Id { get; set; }

    /// <summary>
    /// Register identifier this docket belongs to
    /// </summary>
    [Required]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Hash of previous docket for chain integrity
    /// </summary>
    public string PreviousHash { get; set; } = string.Empty;

    /// <summary>
    /// Hash of this docket
    /// </summary>
    [Required]
    public string Hash { get; set; } = string.Empty;

    /// <summary>
    /// List of transaction IDs sealed in this docket
    /// </summary>
    public List<string> TransactionIds { get; set; } = new();

    /// <summary>
    /// Docket creation timestamp (UTC)
    /// </summary>
    public DateTime TimeStamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Current docket lifecycle state
    /// </summary>
    public DocketState State { get; set; } = DocketState.Init;

    /// <summary>
    /// Docket metadata
    /// </summary>
    public TransactionMetaData? MetaData { get; set; }

    /// <summary>
    /// Identifier of the validator that proposed this docket.
    /// </summary>
    /// <remarks>
    /// A first-class field since Feature 187 (#1371). It used to be smuggled through a property named
    /// <c>Votes</c> — <c>Register.Service</c> wrote <c>Votes = request.ProposerValidatorId</c> and
    /// <c>RegisterServiceClient</c> read it straight back out as the proposer id. It round-tripped
    /// correctly <i>by accident</i>, through a field whose name, type and documentation all disagreed
    /// with its contents.
    /// </remarks>
    public string ProposerValidatorId { get; set; } = string.Empty;

    /// <summary>
    /// Merkle root over this docket's transaction set, as sealed by the proposing validator.
    /// </summary>
    /// <remarks>
    /// Persisted since Feature 187 (#1372). Previously the sealed value was discarded at write time
    /// and the Register Service recomputed a root on demand from the stored
    /// <see cref="TransactionIds"/> — so a docket could not verify itself: recomputation over altered
    /// data yields a different but internally self-consistent root that inclusion proofs then verify
    /// against. Keeping the sealed commitment is what makes a recomputed-versus-sealed cross-check
    /// possible at the points where integrity is actually asserted.
    /// </remarks>
    public string MerkleRoot { get; set; } = string.Empty;

    /// <summary>
    /// The validator votes that carried this docket to consensus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Real, persisted quorum evidence since Feature 187 (#1371). This property previously existed as
    /// a <c>string?</c> documented "Consensus votes (implementation TBD)"; it was neither — it
    /// carried <see cref="ProposerValidatorId"/> (above), and actual <c>ConsensusVote</c> instances
    /// were never written to the register at all. "This docket achieved quorum, and here are the
    /// signed votes" was therefore not recoverable from the ledger.
    /// </para>
    /// <para>
    /// <b>Empty is valid, not an error.</b> A node running without a consensus engine
    /// (single-validator mode — what local dev and single-node deployments use) seals dockets with no
    /// votes to record. Do not add a guard that rejects an empty list.
    /// </para>
    /// </remarks>
    public List<ConsensusVote> Votes { get; set; } = new();
}
