// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for querying audit logs.
/// </summary>
[McpServerToolType]
public sealed class AuditQueryTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AuditQueryTool> _logger;
    private readonly string _tenantServiceEndpoint;

    public AuditQueryTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<AuditQueryTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _tenantServiceEndpoint = configuration["ServiceClients:TenantService:Address"] ?? "http://localhost:5110";
    }

    /// <summary>
    /// Queries audit logs for security and compliance.
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
    /// <returns>Audit log entries.</returns>
    [McpServerTool(Name = "sorcha_audit_query")]
    [Description("Returns paged audit-log entries describing who took which administrative action against which resource and when, filtered by tenant, user, event type, resource type, or time window. Call this when investigating a security incident, building a compliance report, or reconstructing a sequence of admin or user actions; prefer this over sorcha_log_query when the question is about user or admin behaviour rather than service-level diagnostic output, and call before sorcha_token_revoke or sorcha_user_manage so any remediation has a documented trigger.")]
    public async Task<AuditQueryResult> QueryAuditLogsAsync(
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
            return new AuditQueryResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        // Validate event type if provided
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var validTypes = new[] { "Login", "Logout", "Create", "Update", "Delete", "Access" };
            if (!validTypes.Contains(eventType, StringComparer.OrdinalIgnoreCase))
            {
                return new AuditQueryResult
                {
                    Status = "Error",
                    Message = "Invalid event type. Must be Login, Logout, Create, Update, Delete, or Access.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Validate resource type if provided
        if (!string.IsNullOrWhiteSpace(resourceType))
        {
            var validResources = new[] { "User", "Tenant", "Blueprint", "Workflow" };
            if (!validResources.Contains(resourceType, StringComparer.OrdinalIgnoreCase))
            {
                return new AuditQueryResult
                {
                    Status = "Error",
                    Message = "Invalid resource type. Must be User, Tenant, Blueprint, or Workflow.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return new AuditQueryResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Querying audit logs. Tenant: {Tenant}, User: {User}, Event: {Event}, Resource: {Resource}",
            tenantId ?? "all", userId ?? "all", eventType ?? "all", resourceType ?? "all");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Build query string
            var queryParams = new List<string>
            {
                $"page={page}",
                $"pageSize={pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(tenantId))
                queryParams.Add($"organizationId={Uri.EscapeDataString(tenantId)}");

            if (!string.IsNullOrWhiteSpace(userId))
                queryParams.Add($"userId={Uri.EscapeDataString(userId)}");

            if (!string.IsNullOrWhiteSpace(eventType))
                queryParams.Add($"eventType={Uri.EscapeDataString(eventType)}");

            if (!string.IsNullOrWhiteSpace(resourceType))
                queryParams.Add($"resourceType={Uri.EscapeDataString(resourceType)}");

            if (!string.IsNullOrWhiteSpace(startTime))
                queryParams.Add($"startTime={Uri.EscapeDataString(startTime)}");

            if (!string.IsNullOrWhiteSpace(endTime))
                queryParams.Add($"endTime={Uri.EscapeDataString(endTime)}");

            var url = $"{_tenantServiceEndpoint.TrimEnd('/')}/api/audit?{string.Join("&", queryParams)}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Audit query failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Tenant");

                return new AuditQueryResult
                {
                    Status = "Error",
                    Message = $"Audit query failed with status {(int)response.StatusCode}.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<AuditQueryResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new AuditQueryResult
                {
                    Status = "Error",
                    Message = "Failed to parse audit query response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved {Count} audit entries in {ElapsedMs}ms",
                result.Items?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new AuditQueryResult
            {
                Status = "Success",
                Message = $"Retrieved {result.Items?.Count ?? 0} audit entries.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Entries = result.Items?.Select(e => new AuditEntry
                {
                    AuditId = e.AuditId ?? "",
                    Timestamp = e.Timestamp,
                    TenantId = e.OrganizationId,
                    UserId = e.UserId,
                    UserEmail = e.UserEmail,
                    EventType = e.EventType ?? "Unknown",
                    ResourceType = e.ResourceType,
                    ResourceId = e.ResourceId,
                    Action = e.Action ?? "",
                    IpAddress = e.IpAddress,
                    UserAgent = e.UserAgent,
                    Details = e.Details
                }).ToList() ?? [],
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
                TotalPages = result.TotalPages
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant");

            return new AuditQueryResult
            {
                Status = "Timeout",
                Message = "Audit query request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            return new AuditQueryResult
            {
                Status = "Error",
                Message = $"Failed to connect to Tenant service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            _logger.LogError(ex, "Unexpected error querying audit logs");

            return new AuditQueryResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while querying audit logs.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class AuditQueryResponse
    {
        public List<AuditEntryDto>? Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class AuditEntryDto
    {
        public string? AuditId { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public string? OrganizationId { get; set; }
        public string? UserId { get; set; }
        public string? UserEmail { get; set; }
        public string? EventType { get; set; }
        public string? ResourceType { get; set; }
        public string? ResourceId { get; set; }
        public string? Action { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? Details { get; set; }
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
