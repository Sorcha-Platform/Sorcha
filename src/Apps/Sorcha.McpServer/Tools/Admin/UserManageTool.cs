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
/// Admin tool for managing users within an organisation. Writes via the typed
/// <see cref="ITenantServiceClient"/> (spec 139 US4) so the caller's bearer is forwarded
/// and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class UserManageTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<UserManageTool> _logger;

    public UserManageTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<UserManageTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Manages a user's status or role within an organisation.
    /// </summary>
    /// <param name="organizationId">The organisation the user belongs to (required — GUID).</param>
    /// <param name="userId">The user ID to manage.</param>
    /// <param name="action">Action to perform: Suspend, Reactivate, Unlock, ChangeRole.</param>
    /// <param name="role">New role: Administrator, Designer, Auditor, Consumer (required for ChangeRole).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Management result.</returns>
    [McpServerTool(Name = "sorcha_user_manage")]
    [Description("Applies a single state mutation to one user within an organisation — Suspend, Reactivate, Unlock, or ChangeRole — and returns success/failure. Call this when changing a single user's access level or role; prefer this over sorcha_tenant_update when the action should affect one user rather than every user in the organisation, prefer it over sorcha_token_revoke when the goal is to gate future logins rather than invalidate sessions already in flight, and call after sorcha_user_list to confirm the organizationId, userId, and current state before mutating. NOTE: there is no manual Lock action — accounts lock automatically after repeated failed logins, and ChangeRole REPLACES a user's role entirely (there is no additive AddRole/RemoveRole on this endpoint).")]
    public async Task<UserManageResult> ManageUserAsync(
        [Description("The organisation the user belongs to (required — GUID)")] string organizationId,
        [Description("The user ID to manage")] string userId,
        [Description("Action: Suspend, Reactivate, Unlock, ChangeRole")] string action,
        [Description("New role for ChangeRole: Administrator, Designer, Auditor, Consumer")] string? role = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_user_manage"))
        {
            return new UserManageResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(organizationId) || !Guid.TryParse(organizationId, out _))
        {
            return new UserManageResult
            {
                Status = "Error",
                Message = "Organization ID is required and must be a valid GUID.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new UserManageResult
            {
                Status = "Error",
                Message = "User ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return new UserManageResult
            {
                Status = "Error",
                Message = "Action is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate action — this is the complete set the server exposes: no Activate/Deactivate/Lock,
        // and no separate AddRole/RemoveRole (ChangeRole replaces the role outright).
        var validActions = new[] { "Suspend", "Reactivate", "Unlock", "ChangeRole" };
        if (!validActions.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            return new UserManageResult
            {
                Status = "Error",
                Message = "Invalid action. Must be Suspend, Reactivate, Unlock, or ChangeRole.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate role for ChangeRole
        if (string.Equals(action, "ChangeRole", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return new UserManageResult
                {
                    Status = "Error",
                    Message = "Role is required for the ChangeRole action.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            // SystemAdmin is deliberately excluded: the server refuses to assign it via this
            // endpoint (ValidationProblem), so rejecting it client-side saves a round trip.
            var validRoles = new[] { "Administrator", "Designer", "Auditor", "Consumer" };
            if (!validRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return new UserManageResult
                {
                    Status = "Error",
                    Message = "Invalid role. Must be Administrator, Designer, Auditor, or Consumer.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return new UserManageResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation(
            "Managing user {UserId} in organization {OrganizationId}. Action: {Action}, Role: {Role}",
            userId, organizationId, action, role ?? "N/A");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            string? requestBody = string.Equals(action, "ChangeRole", StringComparison.OrdinalIgnoreCase)
                ? JsonSerializer.Serialize(new { role })
                : null;

            // Typed client forwards the caller's bearer and pins the route:
            // POST api/organizations/{organizationId}/users/{userId}/suspend|reactivate|unlock, or
            // PUT api/organizations/{organizationId}/users/{userId}/role.
            var responseContent = await _tenantClient.ManageUserAsync(
                organizationId, userId, action, requestBody, cancellationToken);

            stopwatch.Stop();

            if (responseContent is null)
            {
                _availabilityTracker.RecordSuccess("Tenant");

                return new UserManageResult
                {
                    Status = "Error",
                    Message = "User management action failed.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            _logger.LogInformation(
                "User {UserId} action {Action} completed in {ElapsedMs}ms",
                userId, action, stopwatch.ElapsedMilliseconds);

            var actionDescription = action.ToLowerInvariant() switch
            {
                "suspend" => "suspended",
                "reactivate" => "reactivated",
                "unlock" => "unlocked",
                "changerole" => $"assigned the {role} role",
                _ => action
            };

            return new UserManageResult
            {
                Status = "Success",
                Message = $"User {actionDescription} successfully.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                UserId = userId,
                ActionPerformed = action,
                RoleAffected = role
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant");

            return new UserManageResult
            {
                Status = "Timeout",
                Message = "User management request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            return new UserManageResult
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

            _logger.LogError(ex, "Unexpected error managing user");

            return new UserManageResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while managing user.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

}

/// <summary>
/// Result of managing a user.
/// </summary>
public sealed record UserManageResult
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
    /// The user ID that was managed.
    /// </summary>
    public string UserId { get; init; } = "";

    /// <summary>
    /// The action that was performed.
    /// </summary>
    public string ActionPerformed { get; init; } = "";

    /// <summary>
    /// The role that was affected (for the ChangeRole action).
    /// </summary>
    public string? RoleAffected { get; init; }
}
