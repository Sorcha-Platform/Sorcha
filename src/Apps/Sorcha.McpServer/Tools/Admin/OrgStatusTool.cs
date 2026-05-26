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
/// Administrator tool that suspends or reactivates an organisation via the platform-admin route
/// (Feature 140 Wave 4). Routes through the typed <see cref="ITenantServiceClient"/> so the
/// caller's bearer is forwarded and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class OrgStatusTool
{
    private const string ToolName = "sorcha_org_status";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<OrgStatusTool> _logger;

    public OrgStatusTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<OrgStatusTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Sets an organisation's status to Active or Suspended.
    /// </summary>
    /// <param name="orgId">The organisation ID to update.</param>
    /// <param name="status">New status: "Active" or "Suspended".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated organisation summary, or an error result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Suspends or reactivates a single organisation by setting its status to Active or Suspended, and returns the updated organisation summary. Call this when an operator needs to disable (or restore) every user's access under one organisation in a single action — e.g. an abuse response or a billing hold; prefer this over sorcha_user_password_reset or sorcha_token_revoke, which act on a single user rather than the whole organisation. Platform organisations cannot be suspended (the service rejects the attempt); verify the target organisation first with sorcha_org_user_audit.")]
    public async Task<OrgStatusResult> InvokeAsync(
        [Description("The organisation ID to update")] string orgId,
        [Description("New status: 'Active' or 'Suspended'")] string status,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new OrgStatusResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(orgId))
        {
            return new OrgStatusResult
            {
                Status = "Error",
                Message = "Organisation ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var normalised = status?.Trim() ?? string.Empty;
        if (!normalised.Equals("Active", StringComparison.OrdinalIgnoreCase)
            && !normalised.Equals("Suspended", StringComparison.OrdinalIgnoreCase))
        {
            return new OrgStatusResult
            {
                Status = "Error",
                Message = "Status must be 'Active' or 'Suspended'.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new OrgStatusResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Canonicalise to the service's PascalCase enum value.
        var canonical = normalised.Equals("Active", StringComparison.OrdinalIgnoreCase) ? "Active" : "Suspended";
        _logger.LogInformation("Setting organisation {OrgId} status to {Status}", orgId, canonical);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await _tenantClient.SetOrganizationStatusAsync(
                orgId, JsonSerializer.Serialize(new { status = canonical }), cancellationToken);
            stopwatch.Stop();

            if (body is null)
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return new OrgStatusResult
                {
                    Status = "Error",
                    Message = $"Status change for organisation '{orgId}' was not accepted "
                        + "(it may not exist, or it may be a platform organisation that cannot be suspended).",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess(ServiceName);
            return new OrgStatusResult
            {
                Status = "Success",
                Message = $"Organisation '{orgId}' status set to {canonical}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                OrganizationId = orgId,
                NewStatus = canonical,
                Organization = body
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new OrgStatusResult
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
            _logger.LogError(ex, "Failed to set status for organisation {OrgId}", orgId);
            return new OrgStatusResult
            {
                Status = "Error",
                Message = $"Failed to set organisation status: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of setting an organisation's status.</summary>
public sealed record OrgStatusResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The organisation whose status was changed (on success).</summary>
    public string? OrganizationId { get; init; }

    /// <summary>The new status applied (on success).</summary>
    public string? NewStatus { get; init; }

    /// <summary>The updated organisation-summary JSON body (on success).</summary>
    public string? Organization { get; init; }
}
