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
/// Admin tool for revoking authentication tokens. Writes via the typed <see cref="ITenantServiceClient"/>
/// (spec 139 US4) so the caller's bearer is forwarded and the route is contract-pinned.
/// </summary>
[McpServerToolType]
public sealed class TokenRevokeTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<TokenRevokeTool> _logger;

    public TokenRevokeTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<TokenRevokeTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Revokes authentication tokens for a user or all users in a tenant.
    /// </summary>
    /// <param name="userId">Revoke all tokens for this user ID (optional if tenantId provided).</param>
    /// <param name="tenantId">Revoke all tokens for all users in this tenant (optional if userId provided).</param>
    /// <param name="reason">Reason for revocation (required for audit trail).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Revocation result.</returns>
    [McpServerTool(Name = "sorcha_token_revoke")]
    [Description("Invalidates outstanding JWT access and refresh tokens for a single user or for every user in a tenant, returning success/failure (the server does not report a token or user count, and the supplied reason is not persisted server-side — it is logged locally only). Provide exactly one of userId or tenantId; they target two different endpoints, not one combined route. Call this when responding to a credential compromise, an offboarded user, or any incident that requires immediate forced re-authentication; prefer this over sorcha_user_manage Suspend when you need to invalidate sessions already in flight rather than only block future logins, and prefer it over sorcha_tenant_update Suspend when you want to force re-authentication without changing the tenant's status. Call after sorcha_audit_query so the revocation has a documented trigger.")]
    public async Task<TokenRevokeResult> RevokeTokensAsync(
        [Description("Revoke tokens for this user ID")] string? userId = null,
        [Description("Revoke tokens for all users in this tenant")] string? tenantId = null,
        [Description("Reason for revocation (required for audit)")] string reason = "",
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_token_revoke"))
        {
            return new TokenRevokeResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs - at least one target required
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(tenantId))
        {
            return new TokenRevokeResult
            {
                Status = "Error",
                Message = "Either userId or tenantId is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // The two target endpoints are mutually exclusive — each targets a different server route
        // (POST .../token/revoke-user vs POST .../token/revoke-organization), so a caller supplying
        // both is ambiguous rather than "revoke both".
        if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(tenantId))
        {
            return new TokenRevokeResult
            {
                Status = "Error",
                Message = "Provide either userId or tenantId, not both — they target different endpoints.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Reason is required for audit trail
        if (string.IsNullOrWhiteSpace(reason))
        {
            return new TokenRevokeResult
            {
                Status = "Error",
                Message = "Reason for revocation is required for audit trail.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return new TokenRevokeResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var targetDescription = !string.IsNullOrWhiteSpace(userId)
            ? $"user {userId}"
            : $"tenant {tenantId}";

        _logger.LogWarning("Revoking tokens for {Target}. Reason: {Reason}",
            targetDescription, reason);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Typed client forwards the caller's bearer and pins the route (POST
            // api/auth/token/revoke-user, or POST api/auth/token/revoke-organization).
            var responseContent = await _tenantClient.RevokeTokenAsync(userId, tenantId, cancellationToken);

            stopwatch.Stop();

            if (responseContent is null)
            {
                _availabilityTracker.RecordSuccess("Tenant");

                return new TokenRevokeResult
                {
                    Status = "Error",
                    Message = "Token revocation failed.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            // The endpoint's response is { success, message } — there is no token/user count on
            // this contract (SuccessResponse). Reporting one that always reads "0" would be worse
            // than not reporting one at all, so this tool no longer claims a count it cannot know.
            var result = string.IsNullOrWhiteSpace(responseContent)
                ? null
                : JsonSerializer.Deserialize<RevokeResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            _logger.LogInformation(
                "Token revocation for {Target} completed in {ElapsedMs}ms (success={Success})",
                targetDescription, stopwatch.ElapsedMilliseconds, result?.Success ?? true);

            return new TokenRevokeResult
            {
                Status = "Success",
                Message = result?.Message ?? $"Successfully revoked tokens for {targetDescription}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                UserId = userId,
                TenantId = tenantId,
                Reason = reason
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant");

            return new TokenRevokeResult
            {
                Status = "Timeout",
                Message = "Token revocation request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            return new TokenRevokeResult
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

            _logger.LogError(ex, "Unexpected error revoking tokens");

            return new TokenRevokeResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while revoking tokens.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response model — mirrors Sorcha.Tenant.Service.Models.Dtos.SuccessResponse, the
    // actual wire shape of both POST .../token/revoke-user and .../token/revoke-organization.
    // There is no token/user count on this contract; the old TokensRevoked/UsersAffected fields
    // never matched anything the server returned.
    private sealed class RevokeResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

}

/// <summary>
/// Result of revoking tokens.
/// </summary>
public sealed record TokenRevokeResult
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
    /// User ID if user-specific revocation.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Tenant ID if tenant-wide revocation.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Reason for the revocation.
    /// </summary>
    public string Reason { get; init; } = "";
}
