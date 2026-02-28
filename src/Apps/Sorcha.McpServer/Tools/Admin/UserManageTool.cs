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
/// Admin tool for managing users.
/// </summary>
[McpServerToolType]
public sealed class UserManageTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UserManageTool> _logger;
    private readonly string _tenantServiceEndpoint;

    public UserManageTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<UserManageTool> logger)
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
    /// Manages a user's status or roles.
    /// </summary>
    /// <param name="userId">The user ID to manage.</param>
    /// <param name="action">Action to perform: Activate, Deactivate, Lock, Unlock, AddRole, RemoveRole.</param>
    /// <param name="role">Role to add/remove: Admin, Designer, Participant (required for AddRole/RemoveRole).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Management result.</returns>
    [McpServerTool(Name = "sorcha_user_manage")]
    [Description("Manage a user's status or roles. Can activate, deactivate, lock, or unlock users. Can also add or remove roles. Use with caution.")]
    public async Task<UserManageResult> ManageUserAsync(
        [Description("The user ID to manage")] string userId,
        [Description("Action: Activate, Deactivate, Lock, Unlock, AddRole, RemoveRole")] string action,
        [Description("Role for AddRole/RemoveRole: Admin, Designer, Participant")] string? role = null,
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

        // Validate action
        var validActions = new[] { "Activate", "Deactivate", "Lock", "Unlock", "AddRole", "RemoveRole" };
        if (!validActions.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            return new UserManageResult
            {
                Status = "Error",
                Message = "Invalid action. Must be Activate, Deactivate, Lock, Unlock, AddRole, or RemoveRole.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate role for role-related actions
        var roleActions = new[] { "AddRole", "RemoveRole" };
        if (roleActions.Contains(action, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return new UserManageResult
                {
                    Status = "Error",
                    Message = "Role is required for AddRole/RemoveRole actions.",
                    CheckedAt = DateTimeOffset.UtcNow
                };
            }

            var validRoles = new[] { "Admin", "Designer", "Participant" };
            if (!validRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                return new UserManageResult
                {
                    Status = "Error",
                    Message = "Invalid role. Must be Admin, Designer, or Participant.",
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

        _logger.LogInformation("Managing user {UserId}. Action: {Action}, Role: {Role}",
            userId, action, role ?? "N/A");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_tenantServiceEndpoint.TrimEnd('/')}/api/users/{userId}/actions";

            var requestBody = JsonSerializer.Serialize(new
            {
                action,
                role
            });

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("User management failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Tenant");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new UserManageResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "User management action failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new UserManageResult
                    {
                        Status = "Error",
                        Message = $"User management failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            _availabilityTracker.RecordSuccess("Tenant");

            _logger.LogInformation(
                "User {UserId} action {Action} completed in {ElapsedMs}ms",
                userId, action, stopwatch.ElapsedMilliseconds);

            var actionDescription = action.ToLowerInvariant() switch
            {
                "activate" => "activated",
                "deactivate" => "deactivated",
                "lock" => "locked",
                "unlock" => "unlocked",
                "addrole" => $"granted {role} role",
                "removerole" => $"removed {role} role",
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

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
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
    /// The role that was affected (for role actions).
    /// </summary>
    public string? RoleAffected { get; init; }
}
