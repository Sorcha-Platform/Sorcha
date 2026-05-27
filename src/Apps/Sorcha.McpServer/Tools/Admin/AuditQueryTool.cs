// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for querying audit logs.
/// </summary>
/// <remarks>
/// Spec 139 US4 (LOCKED DECISION): the platform exposes no audit-query API yet, so this tool
/// is marked <c>NotSupported</c>. It keeps its admin authorization gate and stays advertised so
/// the catalogue/manifest is stable, but it fails honestly instead of calling a phantom endpoint.
/// Wire it up when an audit surface lands.
/// </remarks>
[McpServerToolType]
public sealed class AuditQueryTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly ILogger<AuditQueryTool> _logger;

    public AuditQueryTool(
        IMcpAuthorizationService authService,
        ILogger<AuditQueryTool> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Queries audit logs for security and compliance. Currently not supported (no backend API).
    /// </summary>
    /// <param name="tenantId">Filter by tenant/organization ID (optional).</param>
    /// <param name="userId">Filter by user ID (optional).</param>
    /// <param name="eventType">Filter by event type: Login, Logout, Create, Update, Delete, Access (optional).</param>
    /// <param name="resourceType">Filter by resource type: User, Tenant, Blueprint, Workflow (optional).</param>
    /// <param name="startTime">Start time for audit range (ISO 8601, optional).</param>
    /// <param name="endTime">End time for audit range (ISO 8601, optional).</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 50, max: 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A NotSupported result until an audit API exists.</returns>
    [McpServerTool(Name = "sorcha_audit_query")]
    [Description("Returns paged audit-log entries describing who took which administrative action against which resource and when, filtered by tenant, user, event type, resource type, or time window. NOTE: the platform exposes no audit-query API yet, so this tool currently returns a NotSupported result; it will be wired up when an audit surface lands. Call this when investigating a security incident, building a compliance report, or reconstructing a sequence of admin or user actions; prefer this over sorcha_log_query when the question is about user or admin behaviour rather than service-level diagnostic output.")]
    public Task<AuditQueryResult> QueryAuditLogsAsync(
        [Description("Filter by tenant/organization ID")] string? tenantId = null,
        [Description("Filter by user ID")] string? userId = null,
        [Description("Filter by event type: Login, Logout, Create, Update, Delete, Access")] string? eventType = null,
        [Description("Filter by resource type: User, Tenant, Blueprint, Workflow")] string? resourceType = null,
        [Description("Start time for audit range (ISO 8601 format)")] string? startTime = null,
        [Description("End time for audit range (ISO 8601 format)")] string? endTime = null,
        [Description("Page number (1-based, default: 1)")] int page = 1,
        [Description("Items per page (default: 50, max: 200)")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_audit_query"))
        {
            return Task.FromResult(new AuditQueryResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation("sorcha_audit_query invoked but no audit-query API is available (NotSupported).");

        return Task.FromResult(new AuditQueryResult
        {
            Status = "NotSupported",
            Message = "The platform exposes no audit-query API yet; this tool will be wired up when an audit surface lands.",
            CheckedAt = DateTimeOffset.UtcNow
        });
    }
}

/// <summary>
/// Result of querying audit logs.
/// </summary>
public sealed record AuditQueryResult
{
    /// <summary>
    /// Operation status: Success, Error, Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the operation result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the operation was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// List of audit entries.
    /// </summary>
    public IReadOnlyList<AuditEntry> Entries { get; init; } = [];

    /// <summary>
    /// Total number of entries matching the filter.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>
    /// Current page number.
    /// </summary>
    public int Page { get; init; }

    /// <summary>
    /// Items per page.
    /// </summary>
    public int PageSize { get; init; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages { get; init; }
}

/// <summary>
/// An audit log entry.
/// </summary>
public sealed record AuditEntry
{
    /// <summary>
    /// Unique audit entry ID.
    /// </summary>
    public required string AuditId { get; init; }

    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>
    /// Tenant/organization ID.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// User ID who performed the action.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// User email who performed the action.
    /// </summary>
    public string? UserEmail { get; init; }

    /// <summary>
    /// Event type: Login, Logout, Create, Update, Delete, Access.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Resource type affected.
    /// </summary>
    public string? ResourceType { get; init; }

    /// <summary>
    /// Resource ID affected.
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Description of the action performed.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// IP address of the client.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// User agent string.
    /// </summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// Additional details in JSON format.
    /// </summary>
    public string? Details { get; init; }
}
