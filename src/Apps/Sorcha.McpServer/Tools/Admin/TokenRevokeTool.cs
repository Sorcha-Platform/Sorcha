// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Admin tool for revoking authentication tokens.
/// </summary>
[McpServerToolType]
public sealed class TokenRevokeTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TokenRevokeTool> _logger;
    private readonly string _tenantServiceEndpoint;

    public TokenRevokeTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TokenRevokeTool> logger)
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
    /// Revokes authentication tokens for a user or all users in a tenant.
    /// </summary>
    /// <param name="userId">Revoke all tokens for this user ID (optional if tenantId provided).</param>
    /// <param name="tenantId">Revoke all tokens for all users in this tenant (optional if userId provided).</param>
    /// <param name="reason">Reason for revocation (required for audit trail).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Revocation result.</returns>
    [McpServerTool(Name = "sorcha_token_revoke")]
    [Description("Revoke authentication tokens for a user or tenant. Forces re-authentication. Use for security incidents, user lockouts, or when a user leaves an organization.")]
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
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_tenantServiceEndpoint.TrimEnd('/')}/api/tokens/revoke";

            var requestBody = JsonSerializer.Serialize(new
            {
                userId,
                organizationId = tenantId,
                reason
            });

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Token revocation failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Tenant");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new TokenRevokeResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Token revocation failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new TokenRevokeResult
                    {
                        Status = "Error",
                        Message = $"Token revocation failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            _availabilityTracker.RecordSuccess("Tenant");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RevokeResponse>(responseContent, new JsonSerializerOptions
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

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
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
