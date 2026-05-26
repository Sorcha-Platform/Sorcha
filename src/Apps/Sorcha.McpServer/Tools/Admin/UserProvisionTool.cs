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
/// Administrator tool that provisions a platform user into an organisation (Feature 140 Wave 4).
/// Routes through the typed <see cref="ITenantServiceClient"/> so the caller's bearer is
/// forwarded and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class UserProvisionTool
{
    private const string ToolName = "sorcha_user_provision";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<UserProvisionTool> _logger;

    public UserProvisionTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<UserProvisionTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Provisions a platform user into an organisation.
    /// </summary>
    /// <param name="email">The user's email (unique across the platform).</param>
    /// <param name="displayName">The user's display name.</param>
    /// <param name="organizationId">Target organisation to provision into.</param>
    /// <param name="role">Role to assign in the organisation (e.g. Consumer, Member, Admin).</param>
    /// <param name="password">Optional initial password (NIST policy enforced server-side).</param>
    /// <param name="skipEmailVerification">If true, mark the email verified immediately (no verification email).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provisioned-user result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Provisions a platform user directly into an organisation with a chosen role — creating the user, identity and org-membership atomically, reusing an existing platform user if the email already exists. Call this when an operator needs to add a known person to an organisation without waiting for an email-invitation round-trip (the optional skipEmailVerification marks them verified immediately); prefer this over the self-registration / invitation flows when administrative, no-touch onboarding is required. Use sorcha_user_password_reset afterwards to set or rotate the password, and sorcha_org_user_audit to confirm the membership.")]
    public async Task<UserProvisionResult> InvokeAsync(
        [Description("The user's email (unique across the platform)")] string email,
        [Description("The user's display name")] string displayName,
        [Description("Target organisation ID to provision into")] string organizationId,
        [Description("Role to assign in the organisation (e.g. Consumer, Member, Admin)")] string role,
        [Description("Optional initial password (NIST policy enforced)")] string? password = null,
        [Description("If true, mark the email verified immediately (no verification email)")] bool skipEmailVerification = false,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new UserProvisionResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName)
            || string.IsNullOrWhiteSpace(organizationId) || string.IsNullOrWhiteSpace(role))
        {
            return new UserProvisionResult
            {
                Status = "Error",
                Message = "email, displayName, organizationId and role are all required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new UserProvisionResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Provisioning user {Email} into organisation {OrgId} as {Role}",
            email, organizationId, role);

        var requestBody = new Dictionary<string, object>
        {
            ["email"] = email,
            ["displayName"] = displayName,
            ["organizationId"] = organizationId,
            ["role"] = role,
            ["skipEmailVerification"] = skipEmailVerification
        };
        if (!string.IsNullOrWhiteSpace(password))
        {
            requestBody["password"] = password;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await _tenantClient.ProvisionPlatformUserAsync(
                JsonSerializer.Serialize(requestBody), cancellationToken);
            stopwatch.Stop();

            if (body is null)
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return new UserProvisionResult
                {
                    Status = "Error",
                    Message = "User provisioning was not accepted (the organisation may not exist, "
                        + "the email/role may be invalid, or a conflicting membership may already exist).",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess(ServiceName);
            return new UserProvisionResult
            {
                Status = "Success",
                Message = $"User '{email}' provisioned into organisation '{organizationId}' as {role}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                User = body
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new UserProvisionResult
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
            _logger.LogError(ex, "Failed to provision user {Email}", email);
            return new UserProvisionResult
            {
                Status = "Error",
                Message = $"Failed to provision user: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of provisioning a platform user.</summary>
public sealed record UserProvisionResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The provisioned-user JSON body (on success).</summary>
    public string? User { get; init; }
}
