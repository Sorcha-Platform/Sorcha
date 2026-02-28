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
/// Admin tool for creating tenants.
/// </summary>
[McpServerToolType]
public sealed class TenantCreateTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TenantCreateTool> _logger;
    private readonly string _tenantServiceEndpoint;

    public TenantCreateTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TenantCreateTool> logger)
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
    /// Creates a new tenant/organization.
    /// </summary>
    /// <param name="name">The tenant name.</param>
    /// <param name="adminEmail">Email address for the initial admin user.</param>
    /// <param name="adminName">Display name for the initial admin user (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Creation result with the new tenant ID.</returns>
    [McpServerTool(Name = "sorcha_tenant_create")]
    [Description("Create a new tenant/organization. Sets up the tenant with an initial admin user. Use this to onboard new organizations.")]
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
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_tenantServiceEndpoint.TrimEnd('/')}/api/organizations";

            var requestBody = JsonSerializer.Serialize(new
            {
                name,
                adminEmail,
                adminName = adminName ?? adminEmail.Split('@')[0]
            });

            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Tenant creation failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Tenant");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new TenantCreateResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Tenant creation failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new TenantCreateResult
                    {
                        Status = "Error",
                        Message = $"Tenant creation failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
            }

            _availabilityTracker.RecordSuccess("Tenant");

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
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
                TenantName = name,
                AdminUserId = result.AdminUserId,
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

    // Internal response models
    private sealed class CreateResponse
    {
        public string? OrganizationId { get; set; }
        public string? AdminUserId { get; set; }
    }

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
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
