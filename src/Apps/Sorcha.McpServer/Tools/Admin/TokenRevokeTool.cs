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
    [Description("Invalidates outstanding JWT access and refresh tokens for a single user or for every user under a tenant, recording the supplied reason for audit, and returns the count of tokens revoked. Call this when responding to a credential compromise, an offboarded user, or any incident that requires immediate forced re-authentication; prefer this over sorcha_user_manage Lock when you need to invalidate sessions already in flight rather than only block future logins, and prefer it over sorcha_tenant_update Suspend when you want to force re-authentication without changing the tenant's status. Call after sorcha_audit_query so the revocation has a documented trigger.")]
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
            var requestBody = JsonSerializer.Serialize(new
            {
                userId,
                organizationId = tenantId,
                reason
            });

            // Typed client forwards the caller's bearer and pins the route (POST api/tokens/revoke).
            var responseContent = await _tenantClient.RevokeTokenAsync(requestBody, cancellationToken);

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

            var result = string.IsNullOrWhiteSpace(responseContent)
                ? null
                : JsonSerializer.Deserialize<RevokeResponse>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            _logger.LogInformation(
                "Revoked {Count} tokens for {Target} in {ElapsedMs}ms",
                result?.TokensRevoked ?? 0, targetDescription, stopwatch.ElapsedMilliseconds);

            return new TokenRevokeResult
            {
                Status = "Success",
                Message = $"Successfully revoked {result?.TokensRevoked ?? 0} token(s) for {targetDescription}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                TokensRevoked = result?.TokensRevoked ?? 0,
                UsersAffected = result?.UsersAffected ?? 0,
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

    // Internal response models
    private sealed class RevokeResponse
    {
        public int TokensRevoked { get; set; }
        public int UsersAffected { get; set; }
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
    /// Number of tokens revoked.
    /// </summary>
    public int TokensRevoked { get; init; }

    /// <summary>
    /// Number of users affected.
    /// </summary>
    public int UsersAffected { get; init; }

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
