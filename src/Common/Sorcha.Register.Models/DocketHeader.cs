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
    /// Consensus votes (implementation TBD)
    /// </summary>
    public string? Votes { get; set; }
}
