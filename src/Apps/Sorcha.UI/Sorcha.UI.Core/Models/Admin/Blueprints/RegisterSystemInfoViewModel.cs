// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Blueprints;

/// <summary>
/// The caller's governance role on a register, derived from the local-relationship role flags
/// (Feature 142, Go-live system-info card). Drives whether the author may publish to the register
/// (Owner / Admin / Designer) and is surfaced on the detail card alongside the register metadata.
/// </summary>
public enum RegisterGovernanceRole
{
    /// <summary>The caller holds no publishing-capable role on the register (subscriber/auditor/unknown).</summary>
    None = 0,

    /// <summary>The caller is a Designer — may publish.</summary>
    Designer = 1,

    /// <summary>The caller is an Admin — may publish.</summary>
    Admin = 2,

    /// <summary>The caller is an Owner — may publish.</summary>
    Owner = 3
}

/// <summary>
/// Aggregated, read-only system information for a candidate Go-live register (Feature 142 / D6 /
/// FR-026). Built by <see cref="Sorcha.UI.Core.Services.IRegisterSystemInfoService"/> via a
/// client-side fan-out over existing register reads — there is no dedicated server aggregate.
/// </summary>
/// <remarks>
/// Resilience: each field is sourced from an independent read; a failed sub-read degrades that one
/// field (the corresponding <c>*Available</c> flag goes false / the value stays at its default)
/// rather than failing the whole card (spec edge case "register not caught up / owned elsewhere").
/// </remarks>
public sealed record RegisterSystemInfoViewModel
{
    /// <summary>The register this card describes.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Human-readable register name.</summary>
    public string Name { get; init; } = string.Empty;

    // ---- Ownership / relationship (GetLocalRelationshipAsync) -----------------------------

    /// <summary>True when the relationship sub-read succeeded.</summary>
    public bool OwnershipAvailable { get; init; }

    /// <summary>True when the local node owns the register (signed the Owner attestation).</summary>
    public bool IsLocallyOwned { get; init; }

    /// <summary>
    /// Plain-language ownership summary — "Owned by this node" when locally owned, otherwise
    /// "Owned by a remote node" (or "Unknown" when the relationship could not be read).
    /// </summary>
    public string OwnershipText =>
        !OwnershipAvailable ? "Unknown" : IsLocallyOwned ? "Owned by this node" : "Owned by a remote node";

    // ---- Validation (GetGovernanceRosterAsync — validator members + required signatures) --

    /// <summary>True when the governance roster sub-read succeeded.</summary>
    public bool ValidationAvailable { get; init; }

    /// <summary>Count of validator members on the governance roster.</summary>
    public int ValidatorCount { get; init; }

    /// <summary>The number of signatures required to seal (validator count is the roster size).</summary>
    public int RequiredSignatures { get; init; }

    // ---- Visibility (Register.Advertise) --------------------------------------------------

    /// <summary>True when the register is advertised to the peer network (public).</summary>
    public bool IsPublic { get; init; }

    /// <summary>Plain-language visibility — "Public" when advertised, otherwise "Private".</summary>
    public string VisibilityText => IsPublic ? "Public" : "Private";

    // ---- Sync state (GetSyncStateAsync) ---------------------------------------------------

    /// <summary>True when the sync-state sub-read succeeded.</summary>
    public bool SyncStateAvailable { get; init; }

    /// <summary>Plain-language sync state (e.g. "Caught up", "Syncing", "Unknown").</summary>
    public string SyncStateText { get; init; } = "Unknown";

    // ---- Developer mode (Register.DevMode) ------------------------------------------------

    /// <summary>True when the register runs in developer mode (field-level encryption NOT enforced).</summary>
    public bool DevMode { get; init; }

    /// <summary>Plain-language mode — "Developer mode" vs "Live".</summary>
    public string ModeText => DevMode ? "Developer mode" : "Live";

    // ---- Published-services count (GetPublishedBlueprintCountAsync) -----------------------

    /// <summary>Count of services (blueprints) already published to the register.</summary>
    public int PublishedServiceCount { get; init; }

    // ---- Caller's governance role (derived from local relationship) -----------------------

    /// <summary>The caller's governance role on the register.</summary>
    public RegisterGovernanceRole CallerRole { get; init; }

    /// <summary>
    /// True when the caller holds a publishing-capable role (Owner / Admin / Designer). When false,
    /// the Go-live UI marks the register as one the author may not publish to (FR-025/FR-027).
    /// </summary>
    public bool CallerCanPublish => CallerRole != RegisterGovernanceRole.None;
}
