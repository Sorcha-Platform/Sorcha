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
/// Admin tool for listing tenants.
/// </summary>
[McpServerToolType]
public sealed class TenantListTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TenantListTool> _logger;
    private readonly string _tenantServiceEndpoint;

    public TenantListTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TenantListTool> logger)
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
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

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

            var url = $"{_tenantServiceEndpoint.TrimEnd('/')}/api/organizations?{string.Join("&", queryParams)}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Tenant list request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Tenant");

                return new TenantListResult
                {
                    Status = "Error",
                    Message = $"Request failed with status {(int)response.StatusCode}.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
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
