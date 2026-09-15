// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
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
/// <remarks>
/// <para>
/// <b>Per-service status comes from the API Gateway's aggregated <c>/api/health</c>, never from
/// probing each service client's address (#1635).</b> On a node the MCP server reaches every backend
/// THROUGH the gateway: the <c>mcp-server-http</c> compose block points every
/// <c>ServiceClients__*__Address</c> at <c>http://api-gateway:8080</c>, so the forwarded caller token
/// is authorised by the platform. Probing <c>{address}/health</c> per service therefore hit the
/// gateway's own <c>/health</c> five times, reporting five independent "Healthy" rows for services it
/// never reached. Meanwhile Peer and the gateway itself had no address key, fell back to
/// <c>localhost</c>, and read as down.
/// </para>
/// <para>
/// The gateway is the one component that reaches every backend at its real internal address, so it
/// is the only honest source. A backend the aggregate does not vouch for is <c>Unknown</c>, never
/// <c>Healthy</c>.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class HealthCheckTool
{
    private const string ToolName = "sorcha_health_check";
    private const string GatewayName = "ApiGateway";

    private static readonly TimeSpan GatewayProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The gateway probes every backend in parallel with its own 5s timeout; allow for that.</summary>
    private static readonly TimeSpan AggregateTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Each backend's key in the gateway aggregate, and the name this tool and
    /// <see cref="IServiceAvailabilityTracker"/> use for it. Array order is report order.
    /// </summary>
    private static readonly (string AggregateKey, string Name)[] BackendServices =
    [
        ("blueprint", "Blueprint"),
        ("register", "Register"),
        ("wallet", "Wallet"),
        ("tenant", "Tenant"),
        ("validator", "Validator"),
        ("peer", "Peer")
    ];

    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HealthCheckTool> _logger;
    private readonly string _gatewayAddress;

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

        // Resolved through SorchaServiceAddresses like every other client; the local-dev default
        // stays here (CLAUDE.md pattern 17).
        _gatewayAddress = (SorchaServiceAddresses.TryResolve(configuration, SorchaService.ApiGateway)
                           ?? "http://localhost:80").TrimEnd('/');
    }

    /// <summary>
    /// Checks the health status of all Sorcha microservices.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health status for all services.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Returns a per-service health snapshot (Blueprint, Register, Wallet, Tenant, Validator, Peer, API Gateway) with any error messages, plus an aggregate Healthy/Degraded/Unhealthy verdict. Backend status is read from the API Gateway's aggregated health check, which probes each service at its own internal address; a service reported Unknown was NOT confirmed healthy (for example because the aggregate could not be read), so do not treat Unknown as up. Call this first when a user reports unexpected failures or before any other admin tool to confirm the platform is reachable; prefer this over sorcha_metrics when you need a binary up/down signal rather than throughput or latency trends, and call before sorcha_peer_status or sorcha_validator_status to localise an outage to a specific subsystem.")]
    public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new HealthCheckResult
            {
                OverallStatus = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                Services = [],
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Starting health check via the API gateway at {Gateway}", _gatewayAddress);
        var stopwatch = Stopwatch.StartNew();

        var gatewayTask = CheckGatewayAsync(cancellationToken);
        var aggregateTask = ReadAggregateAsync(cancellationToken);
        await Task.WhenAll(gatewayTask, aggregateTask);
        stopwatch.Stop();

        var aggregate = await aggregateTask;
        var services = BackendServices
            .Select(backend => ToServiceHealth(backend.AggregateKey, backend.Name, aggregate))
            .ToList();
        services.Add(await gatewayTask);

        // Calculate overall status. Unknown counts against the verdict: it is not confirmation.
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
            message = "All services are unhealthy or unconfirmed. Platform is not operational.";
        }
        else if (unhealthyCount > 0)
        {
            overallStatus = "Degraded";
            var unhealthy = services.Where(s => s.Status == "Unhealthy").Select(s => s.Name).ToList();
            var unknown = services.Where(s => s.Status == "Unknown").Select(s => s.Name).ToList();
            message = "Platform is degraded."
                + (unhealthy.Count > 0 ? $" Unhealthy services: {string.Join(", ", unhealthy)}." : string.Empty)
                + (unknown.Count > 0 ? $" Not confirmed (status unknown): {string.Join(", ", unknown)}." : string.Empty);
        }
        else
        {
            overallStatus = "Healthy";
            message = "All services are healthy.";
        }

        if (aggregate.Failure is not null)
        {
            message += $" Per-service health could not be read from the API gateway ({aggregate.Failure}), "
                       + "so no backend service is confirmed healthy.";
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

    /// <summary>The gateway's own liveness, from its plain-text <c>/health</c>.</summary>
    private async Task<ServiceHealth> CheckGatewayAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var healthUrl = $"{_gatewayAddress}/health";

        // A per-request timeout rather than HttpClient.Timeout: this runs in parallel with the
        // aggregate read, and a client's Timeout cannot be changed once any request has started.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GatewayProbeTimeout);

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(healthUrl, timeout.Token);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(timeout.Token);
                var status = content.Trim().ToLowerInvariant() == "healthy" ? "Healthy" : "Degraded";
                _availabilityTracker.RecordSuccess(GatewayName);

                return new ServiceHealth
                {
                    Name = GatewayName,
                    Status = status,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    Endpoint = _gatewayAddress
                };
            }

            _availabilityTracker.RecordFailure(GatewayName);
            _logger.LogWarning("API gateway health check failed: HTTP {StatusCode}", response.StatusCode);

            return new ServiceHealth
            {
                Name = GatewayName,
                Status = "Unhealthy",
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Endpoint = _gatewayAddress,
                ErrorMessage = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(GatewayName);
            _logger.LogWarning("API gateway health check timed out");

            return new ServiceHealth
            {
                Name = GatewayName,
                Status = "Unhealthy",
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Endpoint = _gatewayAddress,
                ErrorMessage = "Request timed out"
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(GatewayName, ex);
            _logger.LogWarning(ex, "API gateway health check failed");

            return new ServiceHealth
            {
                Name = GatewayName,
                Status = "Unhealthy",
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Endpoint = _gatewayAddress,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Reads the gateway's aggregated <c>/api/health</c>. Both 200 (healthy or degraded) and 503
    /// (unhealthy) carry the per-service body, so the status code alone decides nothing.
    /// </summary>
    private async Task<AggregateRead> ReadAggregateAsync(CancellationToken cancellationToken)
    {
        var url = $"{_gatewayAddress}/api/health";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AggregateTimeout);

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.GetAsync(url, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);

            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("services", out var servicesElement)
                || servicesElement.ValueKind != JsonValueKind.Object)
            {
                return new AggregateRead(null, $"HTTP {(int)response.StatusCode} from {url} carried no per-service detail");
            }

            var entries = new Dictionary<string, BackendEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in servicesElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                entries[property.Name] = new BackendEntry(
                    ReadString(property.Value, "status") ?? "unknown",
                    ReadString(property.Value, "endpoint"),
                    ReadString(property.Value, "error"));
            }

            return new AggregateRead(entries, null);
        }
        catch (JsonException)
        {
            _logger.LogWarning("API gateway aggregated health at {Url} did not return JSON", url);
            return new AggregateRead(null, $"{url} did not return JSON");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("API gateway aggregated health at {Url} timed out", url);
            return new AggregateRead(null, $"{url} timed out");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "API gateway aggregated health at {Url} could not be read", url);
            return new AggregateRead(null, ex.Message);
        }
    }

    private ServiceHealth ToServiceHealth(string aggregateKey, string name, AggregateRead aggregate)
    {
        if (aggregate.Services is null)
        {
            // Not evidence against the service, so nothing is recorded with the availability tracker.
            return new ServiceHealth
            {
                Name = name,
                Status = "Unknown",
                ResponseTimeMs = 0,
                ErrorMessage = $"Not checked: per-service health could not be read from the API gateway ({aggregate.Failure})."
            };
        }

        if (!aggregate.Services.TryGetValue(aggregateKey, out var entry))
        {
            return new ServiceHealth
            {
                Name = name,
                Status = "Unknown",
                ResponseTimeMs = 0,
                ErrorMessage = "The API gateway's aggregated health check does not report this service."
            };
        }

        var status = entry.Status.Trim().ToLowerInvariant() switch
        {
            "healthy" => "Healthy",
            "degraded" => "Degraded",
            "unhealthy" => "Unhealthy",
            _ => "Unknown"
        };

        if (status is "Healthy" or "Degraded")
        {
            _availabilityTracker.RecordSuccess(name);
        }
        else if (status == "Unhealthy")
        {
            _availabilityTracker.RecordFailure(name);
        }

        return new ServiceHealth
        {
            Name = name,
            Status = status,
            ResponseTimeMs = 0,
            Endpoint = entry.Endpoint,
            ErrorMessage = entry.Error
        };
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The aggregate's per-service entries, or why they could not be read.</summary>
    private sealed record AggregateRead(IReadOnlyDictionary<string, BackendEntry>? Services, string? Failure);

    /// <summary>One backend as the gateway reported it.</summary>
    private sealed record BackendEntry(string Status, string? Endpoint, string? Error);
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
    /// Current status: Healthy, Degraded, Unhealthy, or Unknown. Unknown means the status was NOT
    /// confirmed; it never means healthy.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Response time in milliseconds. Measured for the API Gateway; 0 for backend services, whose
    /// status comes from the gateway's aggregated check, which does not report per-service timing.
    /// </summary>
    public required int ResponseTimeMs { get; init; }

    /// <summary>
    /// The endpoint that was checked: for a backend, the internal address the gateway probed.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Error message if service is unhealthy or its status is unknown.
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
    /// Number of services that are unhealthy or whose status is unknown.
    /// </summary>
    public required int UnhealthyServices { get; init; }
}
