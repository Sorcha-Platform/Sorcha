// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Published W3C DID document for one organisation. One row covers both the
/// canonical <c>did:sorcha:org:{addr}</c> and the federated
/// <c>did:web:{platform}:orgs:{orgId}</c> identifiers — the document declares
/// both and links them via <c>alsoKnownAs</c> (Feature 120 data-model §1).
/// </summary>
/// <remarks>
/// Cache-style storage — rebuildable from the wallet's <c>IssuanceKeyState</c>
/// rows if lost (Feature 113 storage audit; not on the fail-fast list).
/// </remarks>
public class OrgDidDocument
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → Organization.Id. Unique (one document per org).</summary>
    public required Guid OrganizationId { get; set; }

    /// <summary>Canonical primary DID — <c>did:sorcha:org:{addr}</c>. Indexed.</summary>
    public required string PrimaryDid { get; set; }

    /// <summary>Federated DID — <c>did:web:{platform}:orgs:{orgId}</c>. Indexed.</summary>
    public required string FederatedDid { get; set; }

    /// <summary>Serialized W3C DID document — the same JSON served at the public endpoint. Max 16KB.</summary>
    public required string DocumentJson { get; set; }

    /// <summary>
    /// Hash of <c>(PrimaryDid, all-active-VMs sorted by id, alsoKnownAs sorted)</c>.
    /// Used to detect when regeneration is a no-op.
    /// </summary>
    public required string KeyVersionFingerprint { get; set; }

    /// <summary>When the document was last regenerated.</summary>
    public DateTimeOffset LastRegeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>What triggered the most recent regeneration.</summary>
    public KeyEventReason LastRegenerationReason { get; set; } = KeyEventReason.Bootstrap;

    /// <summary>Monotonic version counter; incremented on every regeneration. v1 starts at 1.</summary>
    public int Version { get; set; } = 1;
}
