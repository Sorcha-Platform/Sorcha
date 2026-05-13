// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of health check polling service.
/// </summary>
public class HealthCheckService : IHealthCheckService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HealthCheckService> _logger;
    private readonly HealthCheckOptions _options;
    private readonly List<ServiceHealthStatus> _serviceStatuses = [];
    private readonly object _lock = new();
    private Timer? _pollingTimer;
    private bool _isRunning;
    private bool _isDisposed;

    public event EventHandler<HealthStatusChangedEventArgs>? HealthStatusChanged;

    public IReadOnlyList<ServiceHealthStatus> ServiceStatuses
    {
        get
        {
            lock (_lock)
            {
                return _serviceStatuses.ToList().AsReadOnly();
            }
        }
    }

    public HealthCheckService(
        HttpClient httpClient,
        IOptions<HealthCheckOptions> options,
        ILogger<HealthCheckService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        InitializeServiceStatuses();
    }

    private void InitializeServiceStatuses()
    {
        lock (_lock)
        {
            _serviceStatuses.Clear();
            foreach (var config in _options.Services)
            {
                _serviceStatuses.Add(new ServiceHealthStatus
                {
                    ServiceName = config.ServiceName,
                    ServiceKey = config.ServiceKey,
                    Endpoint = config.HealthEndpoint,
                    Status = HealthStatus.Unknown
                });
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            return;

        _isRunning = true;
        _logger.LogInformation("Starting health check polling with {Interval}ms interval",
            _options.PollingIntervalMs);

        // Perform initial check immediately
        await RefreshAsync(cancellationToken);

        // Start periodic polling
        _pollingTimer = new Timer(
            async _ => await PollingCallbackAsync(),
            null,
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs),
            TimeSpan.FromMilliseconds(_options.PollingIntervalMs));
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = false;
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        _logger.LogInformation("Health check polling stopped");
        return Task.CompletedTask;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _options.Services.Select(config =>
            CheckServiceHealthAsync(config, cancellationToken));

        await Task.WhenAll(tasks);
    }

    public ServiceHealthStatus? GetServiceStatus(string serviceKey)
    {
        lock (_lock)
        {
            return _serviceStatuses.FirstOrDefault(s => s.ServiceKey == serviceKey);
        }
    }

    private async Task PollingCallbackAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check polling");
        }
    }

    private async Task CheckServiceHealthAsync(
        ServiceEndpointConfig config,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        HealthStatus newStatus;
        string? errorMessage = null;
        string? version = null;
        string? uptime = null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.TimeoutMs);

            var response = await _httpClient.GetAsync(config.HealthEndpoint, cts.Token);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cts.Token);
                var healthResponse = ParseHealthResponse(content);
                newStatus = healthResponse.status;
                version = healthResponse.version;
                uptime = healthResponse.uptime;
            }
            else if ((int)response.StatusCode == 503)
            {
                newStatus = HealthStatus.Unhealthy;
                errorMessage = $"Service unavailable (HTTP 503)";
            }
            else
            {
                newStatus = HealthStatus.Unknown;
                errorMessage = $"Unexpected status code: {(int)response.StatusCode}";
            }
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            newStatus = HealthStatus.Unknown;
            errorMessage = "Request timed out";
            _logger.LogWarning("Health check timed out for {ServiceKey}", config.ServiceKey);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            newStatus = HealthStatus.Unknown;
            errorMessage = ex.Message;
            _logger.LogWarning("Health check failed for {ServiceKey}: {Error}",
                config.ServiceKey, ex.Message);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            newStatus = HealthStatus.Unknown;
            errorMessage = ex.Message;
            _logger.LogError(ex, "Unexpected error during health check for {ServiceKey}",
                config.ServiceKey);
        }

        UpdateServiceStatus(config.ServiceKey, newStatus, stopwatch.Elapsed, errorMessage, version, uptime);
    }

    private (HealthStatus status, string? version, string? uptime) ParseHealthResponse(string content)
    {
        // Try JSON format first (custom health endpoints return JSON with status/version/uptime)
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            var statusText = root.TryGetProperty("status", out var statusProp)
                ? statusProp.GetString()?.ToLowerInvariant()
                : null;

            var version = root.TryGetProperty("version", out var versionProp)
                ? versionProp.GetString()
                : null;

            var uptime = root.TryGetProperty("uptime", out var uptimeProp)
                ? uptimeProp.GetString()
                : null;

            var status = statusText switch
            {
                "healthy" => HealthStatus.Healthy,
                "degraded" => HealthStatus.Degraded,
                "unhealthy" => HealthStatus.Unhealthy,
                _ => HealthStatus.Unknown
            };

            return (status, version, uptime);
        }
        catch (JsonException)
        {
            // Fall back to plain text format (standard .NET health check endpoints
            // return "Healthy", "Degraded", or "Unhealthy" as plain text)
            var trimmed = content.Trim().ToLowerInvariant();
            var plainStatus = trimmed switch
            {
                "healthy" => HealthStatus.Healthy,
                "degraded" => HealthStatus.Degraded,
                "unhealthy" => HealthStatus.Unhealthy,
                _ => HealthStatus.Unknown
            };

            return (plainStatus, null, null);
        }
    }

    private void UpdateServiceStatus(
        string serviceKey,
        HealthStatus newStatus,
        TimeSpan duration,
        string? errorMessage,
        string? version,
        string? uptime)
    {
        HealthStatus oldStatus;

        lock (_lock)
        {
            var service = _serviceStatuses.FirstOrDefault(s => s.ServiceKey == serviceKey);
            if (service == null)
                return;

            oldStatus = service.Status;
            service.Status = newStatus;
            service.LastCheckTime = DateTimeOffset.UtcNow;
            service.LastCheckDuration = duration;
            service.ErrorMessage = errorMessage;
            service.Version = version;
            service.Uptime = uptime;
        }

        if (oldStatus != newStatus)
        {
            _logger.LogInformation("Service {ServiceKey} status changed from {OldStatus} to {NewStatus}",
                serviceKey, oldStatus, newStatus);

            HealthStatusChanged?.Invoke(this, new HealthStatusChangedEventArgs
            {
                ServiceKey = serviceKey,
                OldStatus = oldStatus,
                NewStatus = newStatus
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        await StopAsync();

        GC.SuppressFinalize(this);
    }
}
