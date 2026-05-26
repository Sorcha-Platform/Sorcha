// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool returning a read-only, paginated user list for an organisation
/// (Feature 140 Wave 4 audit view). Routes through the typed <see cref="ITenantServiceClient"/>
/// so the caller's bearer is forwarded and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class OrgUserAuditTool
{
    private const string ToolName = "sorcha_org_user_audit";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<OrgUserAuditTool> _logger;

    public OrgUserAuditTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<OrgUserAuditTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns a read-only paginated user list for an organisation.
    /// </summary>
    /// <param name="orgId">The organisation ID to audit.</param>
    /// <param name="page">1-based page number (default 1).</param>
    /// <param name="pageSize">Page size, 1-100 (default 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The org-user list, or an error result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Returns a read-only, paginated list of the users in one organisation — each user's email, display name, role, status, join date and last login — for audit and review. Call this to see who has access to an organisation before changing its status or before provisioning/removing users; prefer this over sorcha_user_list, which spans all users platform-wide, when the question is scoped to a single organisation. This is strictly read-only: it never changes membership, so use sorcha_user_provision or sorcha_org_status when an actual change is intended.")]
    public async Task<OrgUserAuditResult> InvokeAsync(
        [Description("The organisation ID to audit")] string orgId,
        [Description("1-based page number (default 1)")] int page = 1,
        [Description("Page size, 1-100 (default 50)")] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new OrgUserAuditResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(orgId))
        {
            return new OrgUserAuditResult
            {
                Status = "Error",
                Message = "Organisation ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new OrgUserAuditResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var clampedPage = Math.Max(1, page);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var queryString = $"page={clampedPage}&pageSize={clampedPageSize}";

        _logger.LogInformation("Auditing users for organisation {OrgId} (page {Page}, size {PageSize})",
            orgId, clampedPage, clampedPageSize);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await _tenantClient.GetOrganizationUsersAsync(orgId, queryString, cancellationToken);
            stopwatch.Stop();

            if (body is null)
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return new OrgUserAuditResult
                {
                    Status = "NotFound",
                    Message = $"Organisation '{orgId}' was not found or has no accessible user list.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess(ServiceName);
            return new OrgUserAuditResult
            {
                Status = "Success",
                Message = $"User list for organisation '{orgId}' retrieved.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                OrganizationId = orgId,
                Users = body
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new OrgUserAuditResult
            {
                Status = "Timeout",
                Message = "Request to tenant service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to audit users for organisation {OrgId}", orgId);
            return new OrgUserAuditResult
            {
                Status = "Error",
                Message = $"Failed to read organisation users: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of an organisation user-audit query.</summary>
public sealed record OrgUserAuditResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the query was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The organisation that was audited (on success).</summary>
    public string? OrganizationId { get; init; }

    /// <summary>The paginated org-user-list JSON body (on success).</summary>
    public string? Users { get; init; }
}
