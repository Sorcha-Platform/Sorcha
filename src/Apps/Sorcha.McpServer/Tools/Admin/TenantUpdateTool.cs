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
/// Admin tool for updating tenant settings.
/// </summary>
[McpServerToolType]
public sealed class TenantUpdateTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TenantUpdateTool> _logger;
    private readonly string _tenantServiceEndpoint;

    public TenantUpdateTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<TenantUpdateTool> logger)
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
    /// Updates a tenant's settings or status.
    /// </summary>
    /// <param name="tenantId">The tenant ID to update.</param>
    /// <param name="name">New tenant name (optional).</param>
    /// <param name="status">New status: Active, Suspended (optional).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Update result.</returns>
    [McpServerTool(Name = "sorcha_tenant_update")]
    [Description("Update a tenant's settings or status. Can rename or suspend/activate a tenant. Use with caution as suspending affects all users.")]
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
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            var url = $"{_tenantServiceEndpoint.TrimEnd('/')}/api/organizations/{tenantId}";

            var updateData = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(name))
                updateData["name"] = name;
            if (!string.IsNullOrWhiteSpace(status))
                updateData["status"] = status;

            var requestBody = JsonSerializer.Serialize(updateData);
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = content };
            var response = await client.SendAsync(request, cancellationToken);

            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Tenant update failed: HTTP {StatusCode} - {Error}", response.StatusCode, errorContent);

                _availabilityTracker.RecordSuccess("Tenant");

                try
                {
                    var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(errorContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    return new TenantUpdateResult
                    {
                        Status = "Error",
                        Message = errorResponse?.Error ?? "Tenant update failed.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
                catch (JsonException)
                {
                    return new TenantUpdateResult
                    {
                        Status = "Error",
                        Message = $"Tenant update failed with status {(int)response.StatusCode}.",
                        CheckedAt = DateTimeOffset.UtcNow,
                        ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                    };
                }
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

    private sealed class ErrorResponse
    {
        public string? Error { get; set; }
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
