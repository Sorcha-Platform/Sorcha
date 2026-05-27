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
/// Admin tool for creating tenants. Writes via the typed <see cref="ITenantServiceClient"/>
/// onto the correct platform-admin provisioning route (POST /api/platform/organizations) — spec 139 US4.
/// </summary>
[McpServerToolType]
public sealed class TenantCreateTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<TenantCreateTool> _logger;

    public TenantCreateTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<TenantCreateTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new tenant/organization.
    /// </summary>
    /// <param name="name">The tenant name.</param>
    /// <param name="adminEmail">Email address for the initial admin user.</param>
    /// <param name="adminName">Display name for the initial admin user (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Creation result with the new tenant ID.</returns>
    [McpServerTool(Name = "sorcha_tenant_create")]
    [Description("Provisions a new tenant (organisation boundary) and creates an initial admin user identified by the supplied email, returning the new tenant ID and the created admin user record. Call this when onboarding a new organisation that does not yet exist on the platform; prefer this over sorcha_tenant_update when no tenant record exists at all, and call before sorcha_user_manage or sorcha_user_list (those tools require an existing tenant ID), and confirm absence first with sorcha_tenant_list to avoid creating a duplicate organisation.")]
    public async Task<TenantCreateResult> CreateTenantAsync(
        [Description("The tenant/organization name")] string name,
        [Description("Email address for the initial admin user")] string adminEmail,
        [Description("Display name for the initial admin user (optional)")] string? adminName = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_tenant_create"))
        {
            return new TenantCreateResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Validate inputs
        if (string.IsNullOrWhiteSpace(name))
        {
            return new TenantCreateResult
            {
                Status = "Error",
                Message = "Tenant name is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            return new TenantCreateResult
            {
                Status = "Error",
                Message = "Admin email is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Basic email validation
        if (!adminEmail.Contains('@') || !adminEmail.Contains('.'))
        {
            return new TenantCreateResult
            {
                Status = "Error",
                Message = "Invalid email format.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return new TenantCreateResult
            {
                Status = "Unavailable",
                Message = "Tenant service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Creating tenant '{Name}' with admin '{AdminEmail}'", name, adminEmail);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build the platform-admin provisioning request body (AdminCreateOrganizationRequest shape).
            var requestBody = JsonSerializer.Serialize(new
            {
                name,
                adminEmail,
                adminDisplayName = adminName ?? adminEmail.Split('@')[0]
            });

            // Typed client forwards the caller's bearer and pins the correct route
            // (POST api/platform/organizations).
            var responseContent = await _tenantClient.CreateOrganizationAsync(requestBody, cancellationToken);

            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(responseContent))
            {
                _availabilityTracker.RecordSuccess("Tenant");

                return new TenantCreateResult
                {
                    Status = "Error",
                    Message = "Tenant creation failed.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _availabilityTracker.RecordSuccess("Tenant");

            var result = JsonSerializer.Deserialize<CreateResponse>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                return new TenantCreateResult
                {
                    Status = "Error",
                    Message = "Failed to parse tenant creation response.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            _logger.LogInformation(
                "Created tenant '{Name}' with ID {TenantId} in {ElapsedMs}ms",
                name, result.OrganizationId, stopwatch.ElapsedMilliseconds);

            return new TenantCreateResult
            {
                Status = "Success",
                Message = $"Tenant '{name}' created successfully.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                TenantId = result.OrganizationId ?? "",
                TenantName = result.OrganizationName ?? name,
                AdminUserId = result.InvitationId,
                AdminEmail = adminEmail
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant");

            return new TenantCreateResult
            {
                Status = "Timeout",
                Message = "Tenant creation request timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Tenant", ex);

            return new TenantCreateResult
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

            _logger.LogError(ex, "Unexpected error creating tenant");

            return new TenantCreateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while creating tenant.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    // Internal response model (AdminCreateOrganizationResponse shape)
    private sealed class CreateResponse
    {
        public string? OrganizationId { get; set; }
        public string? OrganizationName { get; set; }
        public string? InvitationId { get; set; }
    }
}

/// <summary>
/// Result of creating a tenant.
/// </summary>
public sealed record TenantCreateResult
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
    /// The new tenant ID.
    /// </summary>
    public string TenantId { get; init; } = "";

    /// <summary>
    /// The tenant name.
    /// </summary>
    public string TenantName { get; init; } = "";

    /// <summary>
    /// The admin user ID.
    /// </summary>
    public string? AdminUserId { get; init; }

    /// <summary>
    /// The admin email.
    /// </summary>
    public string AdminEmail { get; init; } = "";
}
