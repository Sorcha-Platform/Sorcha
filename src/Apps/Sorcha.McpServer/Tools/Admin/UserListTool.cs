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
/// Admin tool for listing users.
/// </summary>
[McpServerToolType]
public sealed class UserListTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserListTool> _logger;
    private readonly string _tenantServiceEndpoint;

    public UserListTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<UserListTool> logger)
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
    /// Lists users, optionally filtered by tenant.
    /// </summary>
    /// <param name="tenantId">Filter by tenant/organization ID (optional).</param>
    /// <param name="role">Filter by role: Admin, Designer, Participant (optional).</param>
    /// <param name="status">Filter by status: Active, Inactive, Locked (optional).</param>
    /// <param name="search">Search text in user name or email (optional).</param>
    /// <param name="page">Page number (1-based, default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of users.</returns>
    [McpServerTool(Name = "sorcha_user_list")]
    [Description("Returns a paged list of users with id, email, display name, status, and roles, scoped to a specific tenant or platform-wide, with optional filters on role, status, and free-text name/email search. Call this when you need to discover a user ID, audit role assignments, or confirm a user exists before mutating them; prefer this over sorcha_user_manage when the goal is read-only enquiry rather than mutation, and call before sorcha_user_manage or sorcha_token_revoke so the subsequent mutation targets the correct user ID.")]
    public async Task<UserListResult> ListUsersAsync(
        [Description("Filter by tenant/organization ID")] string? tenantId = null,
        [Description("Filter by role: Admin, Designer, Participant")] string? role = null,
        [Description("Filter by status: Active, Inactive, Locked")] string? status = null,
        [Description("Search text in user name or email")] string? search = null,
        [Description("Page number (1-based, default: 1)")] int page = 1,
        [Description("Items per page (default: 20, max: 100)")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_user_list"))
        {
            return new UserListResult
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

        // Validate role if provided
        if (!string.IsNullOrWhiteSpace(role))
        {
            var validRoles = new[] { "Admin", "Designer", "Participant" };
            if (!validRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return new UserListResult
                {
                    Status = "Error",
                    Message = "Invalid role. Must be Admin, Designer, or Participant.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Validate status if provided
        if (!string.IsNullOrWhiteSpace(status))
        {
            var validStatuses = new[] { "Active", "Inactive", "Locked" };
            if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return new UserListResult
                {
                    Status = "Error",
                    Message = "Invalid status. Must be Active, Inactive, or Locked.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return new UserListResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Listing users. Tenant: {Tenant}, Role: {Role}, Status: {Status}, Page: {Page}",
            tenantId ?? "all", role ?? "all", status ?? "all", page);

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

            if (!string.IsNullOrWhiteSpace(role))
                queryParams.Add($"role={Uri.EscapeDataString(role)}");

            if (!string.IsNullOrWhiteSpace(status))
                queryParams.Add($"status={Uri.EscapeDataString(status)}");

            if (!string.IsNullOrWhiteSpace(search))
                queryParams.Add($"search={Uri.EscapeDataString(search)}");

            var url = $"{_tenantServiceEndpoint.TrimEnd('/')}/api/users?{string.Join("&", queryParams)}";

            var response = await client.GetAsync(url, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("User list request failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Tenant");

                return new UserListResult
                {
                    Status = "Error",
                    Message = $"Request failed with status {(int)response.StatusCode}.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<UserListResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new UserListResult
                {
                    Status = "Error",
                    Message = "Failed to parse user list response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Retrieved {Count} users in {ElapsedMs}ms",
                result.Items?.Count ?? 0, stopwatch.ElapsedMilliseconds);

            return new UserListResult
            {
                Status = "Success",
                Message = $"Retrieved {result.Items?.Count ?? 0} user(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Users = result.Items?.Select(u => new UserInfo
                {
                    UserId = u.UserId ?? "",
                    Email = u.Email ?? "",
                    DisplayName = u.DisplayName ?? "",
                    TenantId = u.OrganizationId ?? "",
                    TenantName = u.OrganizationName,
                    Roles = u.Roles ?? [],
                    Status = u.Status ?? "Active",
                    LastLoginAt = u.LastLoginAt,
                    CreatedAt = u.CreatedAt
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

            return new UserListResult
            {
                Status = "Timeout",
                Message = "User list request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            return new UserListResult
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

            _logger.LogError(ex, "Unexpected error listing users");

            return new UserListResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while listing users.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response models
    private sealed class UserListResponse
    {
        public List<UserDto>? Items { get; set; }
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    private sealed class UserDto
    {
        public string? UserId { get; set; }
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public string? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
        public List<string>? Roles { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }
}

/// <summary>
/// Result of listing users.
/// </summary>
public sealed record UserListResult
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
    /// List of users.
    /// </summary>
    public IReadOnlyList<UserInfo> Users { get; init; } = [];

    /// <summary>
    /// Total number of users matching the filter.
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
/// Information about a user.
/// </summary>
public sealed record UserInfo
{
    /// <summary>
    /// Unique user ID.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// User email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Tenant/organization ID.
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Tenant/organization name.
    /// </summary>
    public string? TenantName { get; init; }

    /// <summary>
    /// User roles.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>
    /// User status: Active, Inactive, Locked.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Last login timestamp.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; init; }

    /// <summary>
    /// When the user was created.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
