// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Durable state for a Bitstring Status List.
/// </summary>
/// <remarks>
/// <para>
/// The register is the source of truth for a credential's STATUS — a sealed
/// <c>CredentialStatusChange</c> transaction is what replicates to other nodes and what an auditor
/// reads. This row is the working cache of that, plus the one piece the ledger cannot supply:
/// <see cref="NextAvailableIndex"/>.
/// </para>
/// <para>
/// <b>Why allocation must be durable.</b> Indices are handed out sequentially at issuance, and an
/// allocation is not a ledger event (it happens before the credential is signed, and the index lives
/// inside a payload that may be encrypted to the recipient). If the counter resets, a newly-issued
/// credential is handed an index an older credential already holds — and revoking the new one then
/// silently marks the old one revoked too. That is worse than losing the list, so allocation is
/// persisted rather than derived.
/// </para>
/// <para>
/// Before this existed, <c>StatusListManager</c> held every list in a process-memory dictionary:
/// a restart destroyed them all, every status-list URL 404'd, and because fail-closed genuinely
/// works, every credential-gated action refused (#1482).
/// </para>
/// </remarks>
public class StatusListEntity
{
    /// <summary>Status list id — <c>{issuerWallet}-{registerId}-{purpose}-1</c>.</summary>
    [Required]
    public string Id { get; set; } = default!;

    /// <summary>Wallet address of the issuing organisation.</summary>
    [Required]
    public string IssuerWallet { get; set; } = default!;

    /// <summary>Register the credentials in this list were issued against.</summary>
    [Required]
    public string RegisterId { get; set; } = default!;

    /// <summary>Status purpose — <c>revocation</c> or <c>suspension</c>.</summary>
    [Required]
    public string Purpose { get; set; } = default!;

    /// <summary>GZip'd, base64 bitstring exactly as served in the status list credential.</summary>
    [Required]
    public string EncodedList { get; set; } = default!;

    /// <summary>Number of entries the list can hold.</summary>
    public int Size { get; set; }

    /// <summary>Next index to hand out. The field the ledger cannot reconstruct.</summary>
    public int NextAvailableIndex { get; set; }

    /// <summary>Monotonic version, bumped on every bit change.</summary>
    public int Version { get; set; }

    /// <summary>When the list last changed.</summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Docket number this list has been reconciled up to, or null if never reconciled.
    /// </summary>
    /// <remarks>
    /// The replay watermark. Suspend and reinstate share the revocation bit, so the bit is NOT
    /// monotonic and events must be applied in ledger order — replaying from a recorded position
    /// keeps that ordering meaningful and makes the fold resumable rather than all-or-nothing.
    /// </remarks>
    public long? ReconciledToDocket { get; set; }
}
