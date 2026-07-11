// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// Feature 181 US3 (T037) — platform-admin client for the trusted-list snapshot surface
/// (<c>/api/v1/trust/trustlists</c> via the gateway). Import an ETSI TS 119 612 document, list
/// snapshots with freshness, inspect anchors, and delete.
/// </summary>
public interface ITrustedListAdminService
{
    /// <summary>List Active snapshots with freshness state.</summary>
    Task<IReadOnlyList<TrustListSummary>> ListAsync(CancellationToken ct = default);

    /// <summary>Get a snapshot's detail (anchors + extraction summary), or null when unknown.</summary>
    Task<TrustListDetail?> GetAsync(string trustListId, CancellationToken ct = default);

    /// <summary>Import a trusted-list document under the given id (multipart upload).</summary>
    Task<TrustListImportOutcome> ImportAsync(string trustListId, Stream document, string fileName, CancellationToken ct = default);

    /// <summary>Delete every version of a snapshot. Returns whether the request succeeded.</summary>
    Task<bool> DeleteAsync(string trustListId, CancellationToken ct = default);
}

/// <summary>Client view of a trusted-list snapshot summary.</summary>
public sealed record TrustListSummary
{
    public string TrustListId { get; init; } = string.Empty;
    public long SequenceNumber { get; init; }
    public string? SchemeTerritory { get; init; }
    public string? SchemeOperatorName { get; init; }
    public DateTimeOffset ListIssueDateTime { get; init; }
    public DateTimeOffset? NextUpdate { get; init; }
    public string Freshness { get; init; } = "Fresh";
    public int AnchorCount { get; init; }
    public string SignerCertSubject { get; init; } = string.Empty;
    public string SignerCertThumbprint { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; }
    public string Status { get; init; } = "Active";

    /// <summary>True when the snapshot is past its next-update window (drives the freshness chip).</summary>
    public bool IsStale => string.Equals(Freshness, "Stale", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Client view of a snapshot's detail.</summary>
public sealed record TrustListDetail
{
    public TrustListSummary Summary { get; init; } = new();
    public IReadOnlyList<TrustListAnchor> Anchors { get; init; } = [];
    public string ExtractionSummary { get; init; } = "{}";
}

/// <summary>Client view of one extracted CA anchor.</summary>
public sealed record TrustListAnchor
{
    public string SubjectDn { get; init; } = string.Empty;
    public string Thumbprint { get; init; } = string.Empty;
    public string ServiceTypeIdentifier { get; init; } = string.Empty;
    public string ServiceStatus { get; init; } = string.Empty;
    public DateTimeOffset NotBefore { get; init; }
    public DateTimeOffset NotAfter { get; init; }
}

/// <summary>Result of an import attempt — the stored summary, or a typed failure.</summary>
public sealed record TrustListImportOutcome(bool Success, string? ErrorCode, string? ErrorMessage, TrustListSummary? Summary)
{
    public static TrustListImportOutcome Ok(TrustListSummary summary) => new(true, null, null, summary);
    public static TrustListImportOutcome Fail(string? code, string? message) => new(false, code, message, null);
}
