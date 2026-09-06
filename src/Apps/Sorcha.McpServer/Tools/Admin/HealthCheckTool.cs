// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Configuration;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool for checking platform health.
/// </summary>
[McpServerToolType]
public sealed class HealthCheckTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthCheckTool> _logger;
    private readonly Dictionary<string, string> _serviceEndpoints;

    public HealthCheckTool(
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HealthCheckTool> logger)
    {
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Load service endpoints from configuration. Addresses resolve through
        // SorchaServiceAddresses so this tool honours the same key spellings as the service
        // clients it is reporting on; the local-dev port defaults stay per-service.
        _serviceEndpoints = new Dictionary<string, string>
        {
            ["Blueprint"] = Resolve(configuration, SorchaService.Blueprint, "http://localhost:5000"),
            ["Register"] = Resolve(configuration, SorchaService.Register, "http://localhost:5290"),
            ["Wallet"] = Resolve(configuration, SorchaService.Wallet, "http://localhost:5001"),
            ["Tenant"] = Resolve(configuration, SorchaService.Tenant, "http://localhost:5110"),
            ["Validator"] = Resolve(configuration, SorchaService.Validator, "http://localhost:5004"),
            ["Peer"] = Resolve(configuration, SorchaService.Peer, "http://localhost:5002"),
            ["ApiGateway"] = Resolve(configuration, SorchaService.ApiGateway, "http://localhost:80")
        };

        static string Resolve(IConfiguration configuration, SorchaService service, string localDevDefault) =>
            SorchaServiceAddresses.TryResolve(configuration, service) ?? localDevDefault;
    }

    /// <summary>
    /// Checks the health status of all Sorcha microservices.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health status for all services.</returns>
    [McpServerTool(Name = "sorcha_health_check")]
    [Description("Returns a per-service health snapshot (Blueprint, Register, Wallet, Tenant, Validator, Peer, API Gateway) with response times and any error messages, plus an aggregate Healthy/Degraded/Unhealthy verdict. Call this first when a user reports unexpected failures or before any other admin tool to confirm the platform is reachable; prefer this over sorcha_metrics when you need a binary up/down signal rather than throughput or latency trends, and call before sorcha_peer_status or sorcha_validator_status to localise an outage to a specific subsystem.")]
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_health_check"))
        {
            return new HealthCheckResult
            {
                OverallStatus = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                Services = [],
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Starting health check for all services");
        var stopwatch = Stopwatch.StartNew();

        // Check all services in parallel
        var healthCheckTasks = _serviceEndpoints.Select(async kvp =>
        {
            var (serviceName, endpoint) = (kvp.Key, kvp.Value);
            return (serviceName, await CheckServiceHealthAsync(serviceName, endpoint, cancellationToken));
        });

        var results = await Task.WhenAll(healthCheckTasks);
        stopwatch.Stop();

        var services = results.Select(r => r.Item2).ToList();

        // Calculate overall status
        var healthyCount = services.Count(s => s.Status == "Healthy");
        var degradedCount = services.Count(s => s.Status == "Degraded");
        var unhealthyCount = services.Count(s => s.Status == "Unhealthy" || s.Status == "Unknown");

        string overallStatus;
        string message;

        if (healthyCount == services.Count)
        {
            overallStatus = "Healthy";
            message = "All services are healthy.";
        }
        else if (unhealthyCount == services.Count)
        {
            overallStatus = "Unhealthy";
            message = "All services are unhealthy. Platform is not operational.";
        }
        else if (unhealthyCount > 0)
        {
            overallStatus = "Degraded";
            var unhealthyServices = services.Where(s => s.Status == "Unhealthy" || s.Status == "Unknown")
                .Select(s => s.Name);
            message = $"Platform is degraded. Unhealthy services: {string.Join(", ", unhealthyServices)}";
        }
        else
        {
            overallStatus = "Healthy";
            message = "All services are healthy.";
        }

        _logger.LogInformation(
            "Health check completed in {ElapsedMs}ms. Status: {Status} ({Healthy}/{Total} healthy)",
            stopwatch.ElapsedMilliseconds, overallStatus, healthyCount, services.Count);

        return new HealthCheckResult
        {
            OverallStatus = overallStatus,
            Message = message,
            Services = services,
            CheckedAt = DateTimeOffset.UtcNow,
            TotalCheckTimeMs = (int)stopwatch.ElapsedMilliseconds,
            Summary = new HealthSummary
            {
                TotalServices = services.Count,
                HealthyServices = healthyCount,
                DegradedServices = degradedCount,
                UnhealthyServices = unhealthyCount
            }
        };
    }

    private async Task<ServiceHealth> CheckServiceHealthAsync(
        string serviceName,
        string endpoint,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Use standard health endpoint
            var healthUrl = $"{endpoint.TrimEnd('/')}/health";
            var response = await client.GetAsync(healthUrl, cancellationToken);

            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var status = content.Trim().ToLowerInvariant() == "healthy" ? "Healthy" : "Degraded";

                // Record success with availability tracker
                _availabilityTracker.RecordSuccess(serviceName);

                _logger.LogDebug("Service {Service} health check: {Status} in {Ms}ms",
                    serviceName, status, stopwatch.ElapsedMilliseconds);

                return new ServiceHealth
                {
                    Name = serviceName,
                    Status = status,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Endpoint = endpoint
                };
            }
            else
            {
                // Record failure
                _availabilityTracker.RecordFailure(serviceName);

                _logger.LogWarning("Service {Service} health check failed: HTTP {StatusCode}",
                    serviceName, response.StatusCode);

                return new ServiceHealth
                {
                    Name = serviceName,
                    Status = "Unhealthy",
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Endpoint = endpoint,
                    ErrorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                };
            }
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(serviceName);

            _logger.LogWarning("Service {Service} health check timed out", serviceName);

            return new ServiceHealth
            {
                Name = serviceName,
                Status = "Unhealthy",
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Endpoint = endpoint,
                ErrorMessage = "Request timed out"
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(serviceName, ex);

            _logger.LogWarning(ex, "Service {Service} health check failed", serviceName);

            return new ServiceHealth
            {
                Name = serviceName,
                Status = "Unhealthy",
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Endpoint = endpoint,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(serviceName, ex);

            _logger.LogError(ex, "Unexpected error checking service {Service} health", serviceName);

            return new ServiceHealth
            {
                Name = serviceName,
                Status = "Unknown",
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Endpoint = endpoint,
                ErrorMessage = $"Unexpected error: {ex.Message}"
            };
        }
    }
}

/// <summary>
/// Result of a platform health check.
/// </summary>
public sealed record HealthCheckResult
{
    /// <summary>
    /// Overall platform status: Healthy, Degraded, or Unhealthy.
    /// </summary>
    public required string OverallStatus { get; init; }

    /// <summary>
    /// Human-readable message about the health status.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Health status of individual services.
    /// </summary>
    public required IReadOnlyList<ServiceHealth> Services { get; init; }

    /// <summary>
    /// When the health check was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Total time to complete all health checks in milliseconds.
    /// </summary>
    public int TotalCheckTimeMs { get; init; }

    /// <summary>
    /// Summary statistics.
    /// </summary>
    public HealthSummary? Summary { get; init; }
}

/// <summary>
/// Health status of an individual service.
/// </summary>
public sealed record ServiceHealth
{
    /// <summary>
    /// Name of the service.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Current status: Healthy, Degraded, Unhealthy, or Unknown.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public required int ResponseTimeMs { get; init; }

    /// <summary>
    /// The endpoint that was checked.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Error message if service is unhealthy.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Summary of health check results.
/// </summary>
public sealed record HealthSummary
{
    /// <summary>
    /// Total number of services checked.
    /// </summary>
    public required int TotalServices { get; init; }

    /// <summary>
    /// Number of healthy services.
    /// </summary>
    public required int HealthyServices { get; init; }

    /// <summary>
    /// Number of degraded services.
    /// </summary>
    public required int DegradedServices { get; init; }

    /// <summary>
    /// Number of unhealthy services.
    /// </summary>
    public required int UnhealthyServices { get; init; }
}
