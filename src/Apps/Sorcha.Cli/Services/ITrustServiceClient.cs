// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

using Refit;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client for the Tenant Service's trusted-list admin endpoints (Feature 181 US3).
/// Operators import signed ETSI TS 119 612 trusted lists; verifying services resolve CA anchors
/// from the imported snapshots for the external-EUDI trust rail.
/// </summary>
/// <remarks>
/// These endpoints require an administrator on a platform-tier token, which a normal
/// <c>sorcha auth login</c> as an admin provides. CLI-local by design — trusted-list admin has no
/// second consumer. DTOs are covered by <c>Sorcha.Cli.ContractTests</c>.
/// </remarks>
public interface ITrustServiceClient
{
    /// <summary>Lists imported trusted-list snapshots (newest per trustListId is authoritative).</summary>
    [Get("/api/v1/trust/trustlists")]
    Task<List<TrustListSnapshotSummary>> ListTrustListsAsync([Header("Authorization")] string authorization);

    /// <summary>Gets a trusted-list snapshot with its anchors and extraction summary.</summary>
    [Get("/api/v1/trust/trustlists/{trustListId}")]
    Task<TrustListSnapshotDetail> GetTrustListAsync(string trustListId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Imports a trusted-list document by uploading the XML file (multipart). The server verifies
    /// the enveloped XMLDSig signature, extracts granted CA/QC anchors, and stores a versioned
    /// snapshot whose sequence number must exceed the current Active one for the same trustListId.
    /// </summary>
    [Multipart]
    [Post("/api/v1/trust/trustlists/import")]
    Task<TrustListSnapshotSummary> ImportTrustListFileAsync(
        [AliasAs("trustListId")] string trustListId,
        [AliasAs("document")] StreamPart document,
        [Header("Authorization")] string authorization);

    /// <summary>Imports a trusted list by asking the server to fetch it once from a URL.</summary>
    [Multipart]
    [Post("/api/v1/trust/trustlists/import")]
    Task<TrustListSnapshotSummary> ImportTrustListUrlAsync(
        [AliasAs("trustListId")] string trustListId,
        [AliasAs("sourceUrl")] string sourceUrl,
        [Header("Authorization")] string authorization);

    /// <summary>Deletes every version of a trusted-list snapshot.</summary>
    [Delete("/api/v1/trust/trustlists/{trustListId}")]
    Task DeleteTrustListAsync(string trustListId, [Header("Authorization")] string authorization);
}

/// <summary>
/// Summary of one imported trusted-list snapshot. Mirrors
/// <c>Sorcha.Tenant.Service.Endpoints.TrustListSnapshotSummaryResponse</c>.
/// </summary>
public class TrustListSnapshotSummary
{
    [JsonPropertyName("trustListId")]
    public string TrustListId { get; set; } = string.Empty;

    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; set; }

    [JsonPropertyName("schemeTerritory")]
    public string? SchemeTerritory { get; set; }

    [JsonPropertyName("schemeOperatorName")]
    public string? SchemeOperatorName { get; set; }

    [JsonPropertyName("listIssueDateTime")]
    public DateTimeOffset ListIssueDateTime { get; set; }

    [JsonPropertyName("nextUpdate")]
    public DateTimeOffset? NextUpdate { get; set; }

    [JsonPropertyName("freshness")]
    public string Freshness { get; set; } = string.Empty;

    [JsonPropertyName("anchorCount")]
    public int AnchorCount { get; set; }

    [JsonPropertyName("signerCertSubject")]
    public string SignerCertSubject { get; set; } = string.Empty;

    [JsonPropertyName("signerCertThumbprint")]
    public string SignerCertThumbprint { get; set; } = string.Empty;

    [JsonPropertyName("importedAt")]
    public DateTimeOffset ImportedAt { get; set; }

    [JsonPropertyName("importedBy")]
    public Guid ImportedBy { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// A trusted-list snapshot with its anchors. Mirrors
/// <c>Sorcha.Tenant.Service.Endpoints.TrustListSnapshotDetailResponse</c>.
/// </summary>
public class TrustListSnapshotDetail
{
    [JsonPropertyName("summary")]
    public TrustListSnapshotSummary Summary { get; set; } = new();

    [JsonPropertyName("anchors")]
    public List<TrustListAnchor> Anchors { get; set; } = new();

    [JsonPropertyName("extractionSummary")]
    public string ExtractionSummary { get; set; } = string.Empty;
}

/// <summary>
/// One CA/QC anchor extracted from a trusted list. Mirrors
/// <c>Sorcha.Tenant.Service.Endpoints.TrustListAnchorResponse</c>.
/// </summary>
public class TrustListAnchor
{
    [JsonPropertyName("subjectDn")]
    public string SubjectDn { get; set; } = string.Empty;

    [JsonPropertyName("thumbprint")]
    public string Thumbprint { get; set; } = string.Empty;

    [JsonPropertyName("serviceTypeIdentifier")]
    public string ServiceTypeIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("serviceStatus")]
    public string ServiceStatus { get; set; } = string.Empty;

    [JsonPropertyName("notBefore")]
    public DateTimeOffset NotBefore { get; set; }

    [JsonPropertyName("notAfter")]
    public DateTimeOffset NotAfter { get; set; }
}
