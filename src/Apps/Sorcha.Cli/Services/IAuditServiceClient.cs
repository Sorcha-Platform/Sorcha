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
/// Audit log entry.
/// </summary>
public class AuditLogEntry
{
    public string Id { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// Paginated audit log response.
/// </summary>
public class AuditLogResponse
{
    public List<AuditLogEntry> Entries { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
