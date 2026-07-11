// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Feature 181 US3 — a versioned snapshot of an imported ETSI TS 119 612 trusted list (LOTL or a
/// member-state trusted list). Each import is stored immutably with the list's own sequence number,
/// scheme identity, freshness dates, extracted CA anchors, and import provenance. The newest
/// <see cref="TrustedListSnapshotStatus.Active"/> snapshot per <see cref="TrustListId"/> is
/// authoritative; superseded snapshots remain for audit (FR-013). Freshness (Fresh/Stale) is computed
/// on read from <see cref="NextUpdate"/>, never stored (FR-016).
/// </summary>
public sealed class TrustedListSnapshot
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Operator-chosen stable list identity — matches <c>TrustSourceRef.trustListId</c>. Indexed.</summary>
    public string TrustListId { get; set; } = string.Empty;

    /// <summary>The list's own <c>TSLSequenceNumber</c>. Per-<see cref="TrustListId"/> monotonic — an import
    /// of ≤ the current Active sequence is rejected <c>TRUSTLIST_SEQUENCE_REGRESSION</c>.</summary>
    public long SequenceNumber { get; set; }

    /// <summary>Scheme territory (e.g. <c>EU</c>, <c>IE</c>), when present.</summary>
    public string? SchemeTerritory { get; set; }

    /// <summary>Human-readable scheme operator name, when present.</summary>
    public string? SchemeOperatorName { get; set; }

    /// <summary><c>ListIssueDateTime</c> from the document.</summary>
    public DateTimeOffset ListIssueDateTime { get; set; }

    /// <summary><c>NextUpdate</c> from the document; null ⇒ treated as stale after the configured default.</summary>
    public DateTimeOffset? NextUpdate { get; set; }

    /// <summary>Subject DN of the XMLDSig signer certificate (surfaced at import for out-of-band confirmation — R5).</summary>
    public string SignerCertSubject { get; set; } = string.Empty;

    /// <summary>SHA-256 thumbprint (hex) of the XMLDSig signer certificate.</summary>
    public string SignerCertThumbprint { get; set; } = string.Empty;

    /// <summary>When the snapshot was imported.</summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>Platform user who imported the snapshot (system principal for automated imports).</summary>
    public Guid ImportedByPlatformUserId { get; set; }

    /// <summary>Source URL when imported by fetch-once; null for direct upload.</summary>
    public string? SourceUrl { get; set; }

    /// <summary>SHA-256 (hex) of the raw imported document — audit tie to the exact bytes.</summary>
    public string RawDocumentSha256 { get; set; } = string.Empty;

    /// <summary>Snapshot lifecycle state; newest <see cref="TrustedListSnapshotStatus.Active"/> per list wins.</summary>
    public TrustedListSnapshotStatus Status { get; set; } = TrustedListSnapshotStatus.Active;

    /// <summary>Extracted CA/QC anchors (cascade-deleted with the snapshot).</summary>
    public List<TrustedListAnchor> Anchors { get; set; } = [];

    /// <summary>JSON summary of extracted vs skipped service entries (FR-012).</summary>
    public string ExtractionSummary { get; set; } = "{}";
}

/// <summary>Trusted-list snapshot lifecycle state (FR-013).</summary>
public enum TrustedListSnapshotStatus
{
    /// <summary>The authoritative snapshot for its list identity.</summary>
    Active = 0,

    /// <summary>Replaced by a newer import; retained for audit.</summary>
    Superseded = 1,
}

/// <summary>
/// Feature 181 US3 — a single CA anchor extracted from a granted CA/QC service entry of a trusted list.
/// The verifying trust source loads <see cref="CertificateDer"/> into the X.509 chain's custom trust store.
/// </summary>
public sealed class TrustedListAnchor
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Owning snapshot (cascade delete).</summary>
    public Guid SnapshotId { get; set; }

    /// <summary>The CA certificate, DER-encoded.</summary>
    public byte[] CertificateDer { get; set; } = [];

    /// <summary>Subject distinguished name (display + dedupe).</summary>
    public string SubjectDn { get; set; } = string.Empty;

    /// <summary>SHA-256 thumbprint (hex) of the certificate (display + dedupe).</summary>
    public string Thumbprint { get; set; } = string.Empty;

    /// <summary>Originating <c>TSPService</c> service-type URI.</summary>
    public string ServiceTypeIdentifier { get; set; } = string.Empty;

    /// <summary>Originating service-status URI.</summary>
    public string ServiceStatus { get; set; } = string.Empty;

    /// <summary>Certificate validity start.</summary>
    public DateTimeOffset NotBefore { get; set; }

    /// <summary>Certificate validity end.</summary>
    public DateTimeOffset NotAfter { get; set; }
}
