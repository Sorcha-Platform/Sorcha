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
/// Administrator tool for checking peer network status.
/// </summary>
[McpServerToolType]
public sealed class PeerStatusTool
{
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PeerStatusTool> _logger;
    private readonly string _peerServiceEndpoint;

    public PeerStatusTool(
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PeerStatusTool> logger)
    {
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _peerServiceEndpoint = SorchaServiceAddresses.TryResolve(configuration, SorchaService.Peer) ?? "http://localhost:5002";
    }

    /// <summary>
    /// Queries the peer network status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Peer network status including connected peers, statistics, and health.</returns>
    [McpServerTool(Name = "sorcha_peer_status")]
    [Description("Returns the state of the peer-replication network: connected peer count, per-peer connection quality, gossip statistics, and an aggregate network health verdict. Call this when a register is failing to replicate, a participant reports stale data, or after sorcha_health_check has flagged the Peer service as Degraded; prefer this over sorcha_validator_status when the concern is cross-node state propagation rather than consensus or docket sealing, and prefer it over sorcha_register_stats when you need to know whether peers can talk to each other at all rather than how much data each register holds.")]
    public async Task<PeerStatusResult> GetPeerStatusAsync(CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_peer_status"))
        {
            return new PeerStatusResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Peer"))
        {
            return new PeerStatusResult
            {
                Status = "Unavailable",
                Message = "Peer service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Querying peer network status");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            // Fetch stats and health in parallel
            var statsTask = FetchPeerStatsAsync(client, cancellationToken);
            var healthTask = FetchPeerHealthAsync(client, cancellationToken);

            await Task.WhenAll(statsTask, healthTask);
            stopwatch.Stop();

            var stats = await statsTask;
            var health = await healthTask;

            // Record success
            _availabilityTracker.RecordSuccess("Peer");

            // Determine overall status
            string status;
            string message;

            if (stats == null && health == null)
            {
                status = "Unknown";
                message = "Unable to retrieve peer network status.";
            }
            else if (health?.HealthyPeers == 0)
            {
                status = "Degraded";
                message = "No healthy peers connected. Network may be isolated.";
            }
            else if (health != null && health.HealthPercentage < 50)
            {
                status = "Degraded";
                message = $"Network is degraded. Only {health.HealthPercentage:F1}% of peers are healthy.";
            }
            else
            {
                status = "Healthy";
                message = $"Peer network is healthy with {health?.HealthyPeers ?? 0} connected peers.";
            }

            _logger.LogInformation(
                "Peer status query completed in {ElapsedMs}ms. Status: {Status}",
                stopwatch.ElapsedMilliseconds, status);

            return new PeerStatusResult
            {
                Status = status,
                Message = message,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                NetworkStatistics = stats,
                HealthStatus = health
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Peer");

            _logger.LogWarning("Peer status query timed out");

            return new PeerStatusResult
            {
                Status = "Timeout",
                Message = "Request to peer service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Peer", ex);

            _logger.LogWarning(ex, "Failed to query peer status");

            return new PeerStatusResult
            {
                Status = "Error",
                Message = $"Failed to connect to peer service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Peer", ex);

            _logger.LogError(ex, "Unexpected error querying peer status");

            return new PeerStatusResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while querying peer status.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<PeerNetworkStatistics?> FetchPeerStatsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_peerServiceEndpoint.TrimEnd('/')}/api/peers/stats";
            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch peer stats: HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var stats = JsonSerializer.Deserialize<PeerServiceStatsResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (stats == null) return null;

            return new PeerNetworkStatistics
            {
                TotalPeers = stats.PeerStats?.TotalPeers ?? 0,
                HealthyPeers = stats.PeerStats?.HealthyPeers ?? 0,
                UnhealthyPeers = stats.PeerStats?.UnhealthyPeers ?? 0,
                BootstrapNodes = stats.PeerStats?.BootstrapNodes ?? 0,
                AverageLatencyMs = stats.PeerStats?.AverageLatencyMs ?? 0,
                TotalFailures = stats.PeerStats?.TotalFailures ?? 0,
                QueueSize = stats.QueueStats?.QueueSize ?? 0,
                QualityMetrics = stats.QualityStats != null ? new ConnectionQualityMetrics
                {
                    TotalTrackedPeers = stats.QualityStats.TotalTrackedPeers,
                    ExcellentPeers = stats.QualityStats.ExcellentPeers,
                    GoodPeers = stats.QualityStats.GoodPeers,
                    FairPeers = stats.QualityStats.FairPeers,
                    PoorPeers = stats.QualityStats.PoorPeers,
                    AverageQualityScore = stats.QualityStats.AverageQualityScore
                } : null,
                CircuitBreakers = stats.CircuitBreakerStats != null ? new CircuitBreakerStatus
                {
                    TotalCircuits = stats.CircuitBreakerStats.TotalCircuitBreakers,
                    OpenCircuits = stats.CircuitBreakerStats.OpenCircuits,
                    HalfOpenCircuits = stats.CircuitBreakerStats.HalfOpenCircuits,
                    ClosedCircuits = stats.CircuitBreakerStats.ClosedCircuits
                } : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching peer stats");
            return null;
        }
    }

    private async Task<PeerHealthStatus?> FetchPeerHealthAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_peerServiceEndpoint.TrimEnd('/')}/api/peers/health";
            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch peer health: HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var health = JsonSerializer.Deserialize<PeerHealthResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (health == null) return null;

            return new PeerHealthStatus
            {
                TotalPeers = health.TotalPeers,
                HealthyPeers = health.HealthyPeers,
                UnhealthyPeers = health.UnhealthyPeers,
                HealthPercentage = health.HealthPercentage
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching peer health");
            return null;
        }
    }

    // Internal response models for deserialization
    private sealed class PeerServiceStatsResponse
    {
        public DateTimeOffset Timestamp { get; set; }
        public PeerStatsData? PeerStats { get; set; }
        public QualityStatsData? QualityStats { get; set; }
        public QueueStatsData? QueueStats { get; set; }
        public CircuitBreakerStatsData? CircuitBreakerStats { get; set; }
    }

    private sealed class PeerStatsData
    {
        public int TotalPeers { get; set; }
        public int HealthyPeers { get; set; }
        public int UnhealthyPeers { get; set; }
        public int BootstrapNodes { get; set; }
        public double AverageLatencyMs { get; set; }
        public int TotalFailures { get; set; }
    }

    private sealed class QualityStatsData
    {
        public int TotalTrackedPeers { get; set; }
        public int ExcellentPeers { get; set; }
        public int GoodPeers { get; set; }
        public int FairPeers { get; set; }
        public int PoorPeers { get; set; }
        public double AverageQualityScore { get; set; }
    }

    private sealed class QueueStatsData
    {
        public int QueueSize { get; set; }
        public bool IsEmpty { get; set; }
    }

    private sealed class CircuitBreakerStatsData
    {
        public int TotalCircuitBreakers { get; set; }
        public int OpenCircuits { get; set; }
        public int HalfOpenCircuits { get; set; }
        public int ClosedCircuits { get; set; }
    }

    private sealed class PeerHealthResponse
    {
        public int TotalPeers { get; set; }
        public int HealthyPeers { get; set; }
        public int UnhealthyPeers { get; set; }
        public double HealthPercentage { get; set; }
    }
}

/// <summary>
/// Result of a peer network status query.
/// </summary>
public sealed record PeerStatusResult
{
    /// <summary>
    /// Overall peer network status: Healthy, Degraded, Unavailable, Timeout, Error, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the peer network status.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the status check was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// Network statistics (peer counts, latency, failures).
    /// </summary>
    public PeerNetworkStatistics? NetworkStatistics { get; init; }

    /// <summary>
    /// Health status of peer connections.
    /// </summary>
    public PeerHealthStatus? HealthStatus { get; init; }
}

/// <summary>
/// Peer network statistics.
/// </summary>
public sealed record PeerNetworkStatistics
{
    /// <summary>
    /// Total number of known peers.
    /// </summary>
    public int TotalPeers { get; init; }

    /// <summary>
    /// Number of healthy peers.
    /// </summary>
    public int HealthyPeers { get; init; }

    /// <summary>
    /// Number of unhealthy peers.
    /// </summary>
    public int UnhealthyPeers { get; init; }

    /// <summary>
    /// Number of bootstrap nodes.
    /// </summary>
    public int BootstrapNodes { get; init; }

    /// <summary>
    /// Average latency to peers in milliseconds.
    /// </summary>
    public double AverageLatencyMs { get; init; }

    /// <summary>
    /// Total connection failures across all peers.
    /// </summary>
    public int TotalFailures { get; init; }

    /// <summary>
    /// Number of transactions in the queue.
    /// </summary>
    public int QueueSize { get; init; }

    /// <summary>
    /// Connection quality metrics.
    /// </summary>
    public ConnectionQualityMetrics? QualityMetrics { get; init; }

    /// <summary>
    /// Circuit breaker status.
    /// </summary>
    public CircuitBreakerStatus? CircuitBreakers { get; init; }
}

/// <summary>
/// Connection quality metrics for peers.
/// </summary>
public sealed record ConnectionQualityMetrics
{
    /// <summary>
    /// Total peers with quality tracking.
    /// </summary>
    public int TotalTrackedPeers { get; init; }

    /// <summary>
    /// Peers with excellent quality (score >= 80).
    /// </summary>
    public int ExcellentPeers { get; init; }

    /// <summary>
    /// Peers with good quality (score 60-79).
    /// </summary>
    public int GoodPeers { get; init; }

    /// <summary>
    /// Peers with fair quality (score 40-59).
    /// </summary>
    public int FairPeers { get; init; }

    /// <summary>
    /// Peers with poor quality (score < 40).
    /// </summary>
    public int PoorPeers { get; init; }

    /// <summary>
    /// Average quality score across all peers.
    /// </summary>
    public double AverageQualityScore { get; init; }
}

/// <summary>
/// Circuit breaker status for peer connections.
/// </summary>
public sealed record CircuitBreakerStatus
{
    /// <summary>
    /// Total number of circuit breakers.
    /// </summary>
    public int TotalCircuits { get; init; }

    /// <summary>
    /// Number of open (failing) circuits.
    /// </summary>
    public int OpenCircuits { get; init; }

    /// <summary>
    /// Number of half-open (testing) circuits.
    /// </summary>
    public int HalfOpenCircuits { get; init; }

    /// <summary>
    /// Number of closed (healthy) circuits.
    /// </summary>
    public int ClosedCircuits { get; init; }
}

/// <summary>
/// Health status of peer connections.
/// </summary>
public sealed record PeerHealthStatus
{
    /// <summary>
    /// Total number of peers.
    /// </summary>
    public int TotalPeers { get; init; }

    /// <summary>
    /// Number of healthy peers.
    /// </summary>
    public int HealthyPeers { get; init; }

    /// <summary>
    /// Number of unhealthy peers.
    /// </summary>
    public int UnhealthyPeers { get; init; }

    /// <summary>
    /// Percentage of healthy peers (0-100).
    /// </summary>
    public double HealthPercentage { get; init; }
}
