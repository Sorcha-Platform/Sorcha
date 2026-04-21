// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Sorcha.Register.Models.Enums;

namespace Sorcha.Register.Models;

/// <summary>
/// Represents a distributed ledger register
/// </summary>
public class Register
{
    /// <summary>
    /// Unique identifier (GUID without hyphens)
    /// </summary>
    [Required]
    [StringLength(32, MinimumLength = 32)]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable register name
    /// </summary>
    [Required]
    [StringLength(38, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Purpose and scope of the register
    /// </summary>
    [StringLength(2048)]
    public string? Description { get; set; }

    /// <summary>
    /// Current docket height (number of sealed dockets)
    /// </summary>
    public uint Height { get; set; }

    /// <summary>
    /// Register operational status
    /// </summary>
    public RegisterStatus Status { get; set; } = RegisterStatus.Offline;

    /// <summary>
    /// Whether register is advertised to network peers
    /// </summary>
    public bool Advertise { get; set; }

    /// <summary>
    /// Whether this node maintains full transaction history
    /// </summary>
    public bool IsFullReplica { get; set; } = true;

    /// <summary>
    /// Classification of this register's intended use
    /// </summary>
    public RegisterPurpose Purpose { get; set; } = RegisterPurpose.General;

    /// <summary>
    /// Register creation timestamp (UTC)
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp (UTC)
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Consensus votes (implementation TBD)
    /// </summary>
    public string? Votes { get; set; }

    /// <summary>
    /// When true, payloads are stored as plaintext with disclosure filtering at read time.
    /// When false (default), payloads use envelope encryption with disclosure groups.
    /// </summary>
    public bool DevMode { get; set; }

    /// <summary>
    /// Replication state for remotely-subscribed registers (Feature 108).
    /// null = locally created (no sync needed) — semantically Indeterminate at read time;
    /// <see cref="RegisterSyncState.Indeterminate"/> = subscribed, no evidence yet;
    /// <see cref="RegisterSyncState.Syncing"/> = pulling dockets;
    /// <see cref="RegisterSyncState.CaughtUp"/> = local height matches network high-water-mark;
    /// <see cref="RegisterSyncState.Error"/> = pull pipeline in unrecoverable state.
    /// </summary>
    /// <remarks>
    /// Persisted as the enum name. Legacy string values ("Subscribing"/"Syncing"/"Synced"/"Error")
    /// are mapped on read by <c>RegisterSyncStateBsonSerializer</c>; first write of a register
    /// document after Feature 108 persists the new enum-name form.
    /// </remarks>
    public RegisterSyncState? SyncState { get; set; }

    /// <summary>
    /// Control record stashed at register creation time so local-relationship derivation
    /// can resolve the roster before the genesis docket is sealed. Denormalised from the
    /// authoritative copy that lives in the genesis transaction payload (docket 0).
    /// Once docket 0 is sealed the local-relationship service prefers the docket-sourced
    /// control record; this stash is the pre-seal fallback that breaks the validator-
    /// enrolment chicken-and-egg for freshly-created local registers.
    /// Null on legacy records and on registers synced from a peer.
    /// </summary>
    public RegisterControlRecord? InitialControlRecord { get; set; }
}
