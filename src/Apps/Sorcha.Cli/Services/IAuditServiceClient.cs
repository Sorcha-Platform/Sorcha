// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for audit log endpoints on the Tenant Service.
/// </summary>
public interface IAuditServiceClient
{
    /// <summary>
    /// Lists audit log entries for an organization.
    /// </summary>
    [Get("/api/organizations/{orgId}/audit-log")]
    Task<AuditLogResponse> ListAuditEntriesAsync(
        string orgId,
        [Query] string? since,
        [Query] string? until,
        [Query] string? action,
        [Query] string? user,
        [Query] int? page,
        [Query] int? pageSize,
        [Header("Authorization")] string authorization);
}

// --- Request/Response DTOs ---

/// <summary>
/// A single audit event.
/// </summary>
/// <remarks>
/// Mirrors <c>Sorcha.Tenant.Service.Models.Dtos.AuditEventResponse</c> exactly; the pairing is
/// asserted by <c>CliWireContractTests</c>. This previously declared <c>Action</c>, <c>UserId</c>,
/// <c>UserName</c>, <c>ResourceType</c> and <c>ResourceId</c> — none of which the server sends —
/// so every column of <c>sorcha audit query</c> rendered blank even when events existed.
/// </remarks>
public class AuditLogEntry
{
    /// <summary>Monotonic audit event id.</summary>
    public long Id { get; set; }

    /// <summary>When the event occurred (UTC).</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>The event type, e.g. <c>identity.login</c>.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>The acting identity, when the event had one.</summary>
    public Guid? IdentityId { get; set; }

    /// <summary>Source IP, when recorded.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Whether the audited operation succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Free-form event detail.</summary>
    public Dictionary<string, object>? Details { get; set; }
}

/// <summary>
/// Paginated audit log response.
/// </summary>
/// <remarks>
/// The collection is <c>events</c> on the wire, not <c>entries</c>. Reading the wrong name meant
/// the list deserialised empty and <c>sorcha audit query</c> reported "No audit entries found"
/// regardless of what the server returned.
/// </remarks>
public class AuditLogResponse
{
    /// <summary>The audit events on this page.</summary>
    public List<AuditLogEntry> Events { get; set; } = new();

    /// <summary>Total events matching the query across all pages.</summary>
    public int TotalCount { get; set; }

    /// <summary>1-based page number.</summary>
    public int Page { get; set; }

    /// <summary>Page size used for this query.</summary>
    public int PageSize { get; set; }
}
