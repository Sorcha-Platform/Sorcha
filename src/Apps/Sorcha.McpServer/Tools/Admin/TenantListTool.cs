// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for listing tenants. Reads via the typed <see cref="ITenantServiceClient"/>
/// (spec 139 US4) so the caller's bearer is forwarded and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class TenantListTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<TenantListTool> _logger;

    public TenantListTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<TenantListTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists all tenants/organizations in the system.
    /// </summary>
    /// <param name="status">Filter by status: Active, Suspended, Inactive (optional).</param>
    /// <param name="search">Search text in tenant name or ID (optional).</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tenants.</returns>
    [McpServerTool(Name = "sorcha_tenant_list")]
    [Description("Returns a paged list of tenants (organisations) with id, name, status, and basic metadata, filtered by status or by name/id text search. Call this when you need to discover a tenant ID, audit which organisations exist, or check whether an organisation is already provisioned before creating a new one; prefer this over sorcha_tenant_create when you only need to look up or audit existing tenants rather than create one, and call before sorcha_tenant_update or sorcha_token_revoke so subsequent mutations target the correct tenant ID.")]
    public async Task<TenantListResult> ListTenantsAsync(
        [Description("Filter by status: Active, Suspended, Inactive")] string? status = null,
        [Description("Search text in tenant name or ID")] string? search = null,
        [Description("Page number (1-based, default: 1)")] int page = 1,
        [Description("Items per page (default: 20, max: 100)")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_tenant_list"))
        {
            return new TenantListResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate pagination
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        // Validate status if provided
        if (!string.IsNullOrWhiteSpace(status))
        {
            var validStatuses = new[] { "Active", "Suspended", "Inactive" };
            if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return new TenantListResult
                {
                    Status = "Error",
                    Message = "Invalid status. Must be Active, Suspended, or Inactive.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return new TenantListResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Listing tenants. Status: {Status}, Search: {Search}, Page: {Page}",
            status ?? "all", search ?? "none", page);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build query string
            var queryParams = new List<string>
            {
                $"page={page}",
                $"pageSize={pageSize}"
            };

            if (!string.IsNullOrWhiteSpace(status))
                queryParams.Add($"status={Uri.EscapeDataString(status)}");

            if (!string.IsNullOrWhiteSpace(search))
                queryParams.Add($"search={Uri.EscapeDataString(search)}");

            // Typed client forwards the caller's bearer and pins the route (GET api/organizations).
            var responseContent = await _tenantClient.ListOrganizationsAsync(
                string.Join("&", queryParams), cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Tenant");

                return new TenantListResult
                {
                    Status = "Error",
                    Message = "Failed to retrieve tenants.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            var result = JsonSerializer.Deserialize<TenantListResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new TenantListResult
                {
                    Status = "Error",
                    Message = "Failed to parse tenant list response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved {Count} tenants in {ElapsedMs}ms",
                result.Items?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new TenantListResult
            {
                Status = "Success",
                Message = $"Retrieved {result.Items?.Count ?? 0} tenant(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Tenants = result.Items?.Select(t => new TenantInfo
                {
                    TenantId = t.OrganizationId ?? "",
                    Name = t.Name ?? "",
                    Status = t.Status ?? "Active",
                    UserCount = t.UserCount,
                    BlueprintCount = t.BlueprintCount,
                    CreatedAt = t.CreatedAt,
                    LastActivityAt = t.LastActivityAt
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

            return new TenantListResult
            {
                Status = "Timeout",
                Message = "Tenant list request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            return new TenantListResult
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

            _logger.LogError(ex, "Unexpected error listing tenants");

            return new TenantListResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while listing tenants.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class TenantListResponse
    {
        public List<TenantDto>? Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class TenantDto
    {
        public string? OrganizationId { get; set; }
        public string? Name { get; set; }
        public string? Status { get; set; }
        public int UserCount { get; set; }
        public int BlueprintCount { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? LastActivityAt { get; set; }
    }
}

/// <summary>
/// Result of listing tenants.
/// </summary>
public sealed record TenantListResult
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
    /// List of tenants.
    /// </summary>
    public IReadOnlyList<TenantInfo> Tenants { get; init; } = [];

    /// <summary>
    /// Total number of tenants matching the filter.
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
/// Information about a tenant/organization.
/// </summary>
public sealed record TenantInfo
{
    /// <summary>
    /// Unique tenant/organization ID.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Tenant name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Tenant status: Active, Suspended, Inactive.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Number of users in the tenant.
    /// </summary>
    public int UserCount { get; init; }

    /// <summary>
    /// Number of blueprints owned by the tenant.
    /// </summary>
    public int BlueprintCount { get; init; }

    /// <summary>
    /// When the tenant was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    public DateTimeOffset? LastActivityAt { get; init; }
}
