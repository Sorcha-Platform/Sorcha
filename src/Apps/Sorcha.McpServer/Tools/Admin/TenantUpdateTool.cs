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
/// Admin tool for updating tenant settings. Writes via the typed <see cref="ITenantServiceClient"/>
/// (spec 139 US4) so the caller's bearer is forwarded and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class TenantUpdateTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<TenantUpdateTool> _logger;

    public TenantUpdateTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<TenantUpdateTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Updates a tenant's settings or status.
    /// </summary>
    /// <param name="tenantId">The tenant ID to update.</param>
    /// <param name="name">New tenant name (optional).</param>
    /// <param name="status">New status: Active, Suspended (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update result.</returns>
    [McpServerTool(Name = "sorcha_tenant_update")]
    [Description("Mutates an existing tenant — renames it, or moves it between Active and Suspended — and returns the updated tenant record. Call this when you need to rebrand an organisation or to disable platform access for every user under that tenant in a single action; prefer this over sorcha_user_manage when the action should affect every user in the organisation rather than a single user, and prefer it over sorcha_token_revoke when the goal is durable suspension rather than a one-off forced re-authentication. Suspending propagates to all users under the tenant — verify the target with sorcha_tenant_list first.")]
    public async Task<TenantUpdateResult> UpdateTenantAsync(
        [Description("The tenant/organization ID to update")] string tenantId,
        [Description("New tenant name (optional)")] string? name = null,
        [Description("New status: Active, Suspended (optional)")] string? status = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_tenant_update"))
        {
            return new TenantUpdateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return new TenantUpdateResult
            {
                Status = "Error",
                Message = "Tenant ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate status if provided
        if (!string.IsNullOrWhiteSpace(status))
        {
            var validStatuses = new[] { "Active", "Suspended" };
            if (!validStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                return new TenantUpdateResult
                {
                    Status = "Error",
                    Message = "Invalid status. Must be Active or Suspended.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check if at least one update field is provided
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(status))
        {
            return new TenantUpdateResult
            {
                Status = "Error",
                Message = "At least one update field (name or status) is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return new TenantUpdateResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Updating tenant {TenantId}. Name: {Name}, Status: {Status}",
            tenantId, name ?? "unchanged", status ?? "unchanged");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var updateData = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(name))
                updateData["name"] = name;
            if (!string.IsNullOrWhiteSpace(status))
                updateData["status"] = status;

            // Typed client forwards the caller's bearer and pins the route (PUT api/organizations/{id}).
            var responseContent = await _tenantClient.UpdateOrganizationAsync(
                tenantId, JsonSerializer.Serialize(updateData), cancellationToken);

            stopwatch.Stop();

            if (responseContent is null)
            {
                _availabilityTracker.RecordSuccess("Tenant");

                return new TenantUpdateResult
                {
                    Status = "Error",
                    Message = "Tenant update failed.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            _logger.LogInformation(
                "Updated tenant {TenantId} in {ElapsedMs}ms",
                tenantId, stopwatch.ElapsedMilliseconds);

            var changes = new List<string>();
            if (!string.IsNullOrWhiteSpace(name)) changes.Add($"name to '{name}'");
            if (!string.IsNullOrWhiteSpace(status)) changes.Add($"status to '{status}'");

            return new TenantUpdateResult
            {
                Status = "Success",
                Message = $"Tenant updated: {string.Join(", ", changes)}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                TenantId = tenantId,
                UpdatedName = name,
                UpdatedStatus = status
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant");

            return new TenantUpdateResult
            {
                Status = "Timeout",
                Message = "Tenant update request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            return new TenantUpdateResult
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

            _logger.LogError(ex, "Unexpected error updating tenant");

            return new TenantUpdateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while updating tenant.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

}

/// <summary>
/// Result of updating a tenant.
/// </summary>
public sealed record TenantUpdateResult
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
    /// The tenant ID that was updated.
    /// </summary>
    public string TenantId { get; init; } = "";

    /// <summary>
    /// The updated name if changed.
    /// </summary>
    public string? UpdatedName { get; init; }

    /// <summary>
    /// The updated status if changed.
    /// </summary>
    public string? UpdatedStatus { get; init; }
}
