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
/// Administrator tool that resets a platform user's password (Feature 140 Wave 4). Routes
/// through the typed <see cref="ITenantServiceClient"/> so the caller's bearer is forwarded
/// and the route is contract-pinned. The new password value is never logged.
/// </summary>
[McpServerToolType]
public sealed class UserPasswordResetTool
{
    private const string ToolName = "sorcha_user_password_reset";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<UserPasswordResetTool> _logger;

    public UserPasswordResetTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<UserPasswordResetTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Resets a platform user's password.
    /// </summary>
    /// <param name="userId">The platform user's ID.</param>
    /// <param name="newPassword">The new password (NIST policy enforced server-side).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The password-reset result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Sets a new password for one platform user by their user ID, enforcing the platform's NIST password policy (length and breach-list checks) server-side. Call this when an operator must reset a locked-out or onboarding user's credential, or rotate a compromised one; prefer this over sorcha_token_revoke, which only forces re-authentication with the existing password, when the password itself needs to change. This acts on a single user — use sorcha_org_status to disable an entire organisation instead. The password value is never logged.")]
    public async Task<UserPasswordResetResult> InvokeAsync(
        [Description("The platform user's ID")] string userId,
        [Description("The new password (NIST policy enforced)")] string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new UserPasswordResetResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new UserPasswordResetResult
            {
                Status = "Error",
                Message = "User ID is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(newPassword))
        {
            return new UserPasswordResetResult
            {
                Status = "Error",
                Message = "A new password is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new UserPasswordResetResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Resetting password for platform user {UserId}", userId);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await _tenantClient.ResetPlatformUserPasswordAsync(
                userId, JsonSerializer.Serialize(new { newPassword }), cancellationToken);
            stopwatch.Stop();

            if (body is null)
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return new UserPasswordResetResult
                {
                    Status = "Error",
                    Message = "Password reset was not accepted (the user may not exist, or the new "
                        + "password may not meet the platform's NIST policy).",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess(ServiceName);
            return new UserPasswordResetResult
            {
                Status = "Success",
                Message = $"Password reset for user '{userId}'.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                UserId = userId
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new UserPasswordResetResult
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
            _logger.LogError(ex, "Failed to reset password for user {UserId}", userId);
            return new UserPasswordResetResult
            {
                Status = "Error",
                Message = $"Failed to reset password: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of resetting a platform user's password.</summary>
public sealed record UserPasswordResetResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The user whose password was reset (on success).</summary>
    public string? UserId { get; init; }
}
