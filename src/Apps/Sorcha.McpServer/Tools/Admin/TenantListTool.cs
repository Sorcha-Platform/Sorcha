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
    /// <param name="status">Filter by status: Active, Suspended, Deleted (optional). Applied
    /// client-side to the requested page only — see remarks on <see cref="ListTenantsAsync"/>.</param>
    /// <param name="search">Search text in tenant name or ID (optional). Applied client-side to
    /// the requested page only — see remarks on <see cref="ListTenantsAsync"/>.</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of tenants.</returns>
    /// <remarks>
    /// The Tenant Service's <c>GET /api/organizations/</c> endpoint has no server-side status or
    /// search filter at all — it binds only <c>includeInactive</c>, <c>pageNumber</c>, and
    /// <c>pageSize</c>. <paramref name="status"/> and <paramref name="search"/> are therefore
    /// applied HERE, client-side, to the single page of organisations the server returns for the
    /// requested <paramref name="page"/>/<paramref name="pageSize"/> — they do not reach into
    /// other pages, and <see cref="TenantListResult.TotalCount"/>/<see
    /// cref="TenantListResult.TotalPages"/> report the server's unfiltered totals regardless.
    /// </remarks>
    [McpServerTool(Name = "sorcha_tenant_list")]
    [Description("Returns a paged list of tenants (organisations) with id, name, status, and creation date, optionally narrowed by status or by name/id text search. NOTE: status/search are applied client-side to the returned page only — the Tenant Service has no server-side filter, so matches on other pages are not found; call multiple pages if you need an exhaustive search. Call this when you need to discover a tenant ID, audit which organisations exist, or check whether an organisation is already provisioned before creating a new one; prefer this over sorcha_tenant_create when you only need to look up or audit existing tenants rather than create one, and call before sorcha_tenant_update or sorcha_token_revoke so subsequent mutations target the correct tenant ID.")]
    public async Task<TenantListResult> ListTenantsAsync(
        [Description("Filter by status: Active, Suspended, Deleted. Applied client-side to this page only.")] string? status = null,
        [Description("Search text in tenant name or ID. Applied client-side to this page only.")] string? search = null,
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

        // Validate status if provided. These are the Tenant Service's actual OrganizationStatus
        // enum values (Active/Suspended/Deleted) — the previous list offered "Inactive", a value
        // the server never sends, so filtering on it would silently match nothing forever.
        if (!string.IsNullOrWhiteSpace(status))
        {
            var validStatuses = new[] { "Active", "Suspended", "Deleted" };
            if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return new TenantListResult
                {
                    Status = "Error",
                    Message = "Invalid status. Must be Active, Suspended, or Deleted.",
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
            // Typed client forwards the caller's bearer and pins the route (GET api/organizations).
            // status/search are NOT sent — the endpoint has no server-side filter for either.
            var responseContent = await _tenantClient.ListOrganizationsAsync(
                BuildQueryString(page, pageSize), cancellationToken);

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

            var result = ParseTenantList(responseContent);

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

            IEnumerable<TenantInfo> tenants = result.Organizations.Select(t => new TenantInfo
            {
                TenantId = t.Id ?? "",
                Name = t.Name ?? "",
                Status = t.Status ?? "Active",
                CreatedAt = t.CreatedAt
            });

            // Client-side only: the server has no status/search filter (see the <remarks> above),
            // so this narrows the page already returned rather than searching across all pages.
            var filtersApplied = !string.IsNullOrWhiteSpace(status) || !string.IsNullOrWhiteSpace(search);

            if (!string.IsNullOrWhiteSpace(status))
            {
                tenants = tenants.Where(t => string.Equals(t.Status, status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                tenants = tenants.Where(t =>
                    t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    t.TenantId.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var tenantList = tenants.ToList();

            _logger.LogInformation(
                "Retrieved {Count} tenants in {ElapsedMs}ms",
                tenantList.Count, stopwatch.ElapsedMilliseconds);

            return new TenantListResult
            {
                Status = "Success",
                Message = filtersApplied
                    ? $"Retrieved {tenantList.Count} of {result.Organizations.Count} tenant(s) on this page matching the filter. " +
                      "status/search are applied client-side (the Tenant Service has no server-side filter), so other pages were not searched."
                    : $"Retrieved {tenantList.Count} tenant(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Tenants = tenantList,
                TotalCount = result.TotalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = pageSize > 0 ? (int)Math.Ceiling(result.TotalCount / (double)pageSize) : 0
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

    /// <summary>
    /// Mirrors the Tenant Service's <c>OrganizationListResponse</c> from
    /// <c>GET /api/organizations/</c>. Property names here ARE the wire contract — the previous
    /// shape (<c>Items</c>/<c>Page</c>/<c>PageSize</c>/<c>TotalPages</c>) matched nothing the
    /// server sends, so every field silently defaulted.
    /// </summary>
    internal sealed class TenantListResponse
    {
        public List<TenantDto> Organizations { get; set; } = [];

        public int TotalCount { get; set; }
    }

    /// <summary>Deserializes the Tenant Service list body. Returns null on unparseable input.</summary>
    internal static TenantListResponse? ParseTenantList(string body) =>
        JsonSerializer.Deserialize<TenantListResponse>(
            body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    /// <summary>
    /// Builds the list query string. The endpoint binds <c>pageNumber</c>, not <c>page</c> — and
    /// binds ONLY <c>includeInactive</c>/<c>pageNumber</c>/<c>pageSize</c>
    /// (<c>OrganizationEndpoints.ListOrganizations</c>). There is no server-side status or search
    /// parameter to send, so this method deliberately does not accept them — <see
    /// cref="ListTenantsAsync"/> applies status/search client-side instead of sending query
    /// parameters nothing on the server would read.
    /// </summary>
    internal static string BuildQueryString(int page, int pageSize) =>
        $"pageNumber={page}&pageSize={pageSize}";

    /// <summary>
    /// Mirrors one element of the Tenant Service's <c>organizations</c> array
    /// (<c>OrganizationResponse</c>). There is no <c>userCount</c>, <c>blueprintCount</c>, or
    /// <c>lastActivityAt</c> on the wire — those were invented fields that always deserialized to
    /// zero/null, so they are not modelled here or surfaced on <see cref="TenantInfo"/>.
    /// </summary>
    internal sealed class TenantDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
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
    /// Total number of tenants across all pages, as reported by the Tenant Service. This is the
    /// server's unfiltered total — status/search filtering happens client-side against
    /// <see cref="Tenants"/> only (see the <c>sorcha_tenant_list</c> tool's remarks), so it does
    /// not reflect the filter when one is supplied.
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
    /// Tenant status: Active, Suspended, Deleted.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// When the tenant was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
