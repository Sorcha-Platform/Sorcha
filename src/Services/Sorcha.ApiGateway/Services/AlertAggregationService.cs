// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.ApiGateway.Models;

namespace Sorcha.ApiGateway.Services;

/// <summary>
/// Aggregates metrics from backend services and evaluates alert thresholds.
/// </summary>
public class AlertAggregationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AlertAggregationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly AlertThresholdConfig _thresholds;

    public AlertAggregationService(
        IHttpClientFactory httpClientFactory,
        ILogger<AlertAggregationService> logger,
        IConfiguration configuration,
        IOptions<AlertThresholdConfig> thresholds)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
        _thresholds = thresholds.Value;
    }

    public async Task<AlertsResponse> GetAlertsAsync(CancellationToken cancellationToken = default)
    {
        var alerts = new List<ServiceAlert>();

        var validatorTask = FetchValidatorMetricsAsync(cancellationToken);
        var peerTask = FetchPeerHealthAsync(cancellationToken);

        var results = await Task.WhenAll(validatorTask, peerTask);

        alerts.AddRange(results[0]);
        alerts.AddRange(results[1]);

        // Sort by severity descending (Critical first)
        alerts.Sort((a, b) => b.Severity.CompareTo(a.Severity));

        return new AlertsResponse
        {
            Alerts = alerts,
            InfoCount = alerts.Count(a => a.Severity == AlertSeverity.Info),
            WarningCount = alerts.Count(a => a.Severity == AlertSeverity.Warning),
            ErrorCount = alerts.Count(a => a.Severity == AlertSeverity.Error),
            CriticalCount = alerts.Count(a => a.Severity == AlertSeverity.Critical),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private async Task<List<ServiceAlert>> FetchValidatorMetricsAsync(CancellationToken cancellationToken)
    {
        var alerts = new List<ServiceAlert>();

        try
        {
            var baseUrl = _configuration["Services:Validator:Url"] ?? "http://validator-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{baseUrl}/api/metrics/", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                alerts.Add(CreateServiceUnreachableAlert("validator", "Validator Service metrics endpoint returned " + response.StatusCode));
                return alerts;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // TotalFailed
            if (TryGetDouble(root, "totalFailed", out var totalFailed))
            {
                EvaluateThreshold(alerts, "validator", "TotalFailed", totalFailed,
                    _thresholds.ValidatorFailedWarning, _thresholds.ValidatorFailedCritical,
                    "Validator has {0} failed validations");
            }

            // SuccessRate (alert when BELOW threshold, inverted logic)
            if (TryGetDouble(root, "successRate", out var successRate))
            {
                EvaluateThresholdInverted(alerts, "validator", "SuccessRate", successRate,
                    _thresholds.ValidatorSuccessRateWarning, _thresholds.ValidatorSuccessRateCritical,
                    "Validator success rate is {0:F1}%");
            }

            // ConsensusFailures
            if (TryGetDouble(root, "consensusFailures", out var consensusFailures))
            {
                EvaluateThreshold(alerts, "validator", "ConsensusFailures", consensusFailures,
                    _thresholds.ConsensusFailuresWarning, _thresholds.ConsensusFailuresCritical,
                    "Validator has {0} consensus failures");
            }

            // DocketsAbandoned
            if (TryGetDouble(root, "docketsAbandoned", out var docketsAbandoned))
            {
                EvaluateThreshold(alerts, "validator", "DocketsAbandoned", docketsAbandoned,
                    _thresholds.DocketsAbandonedWarning, _thresholds.DocketsAbandonedCritical,
                    "Validator has {0} abandoned dockets");
            }

            // TotalExceptions
            if (TryGetDouble(root, "totalExceptions", out var totalExceptions))
            {
                EvaluateThreshold(alerts, "validator", "TotalExceptions", totalExceptions,
                    _thresholds.ValidatorExceptionsWarning, _thresholds.ValidatorExceptionsCritical,
                    "Validator has {0} total exceptions");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch validator metrics for alert evaluation");
            alerts.Add(CreateServiceUnreachableAlert("validator", "Validator Service is unreachable: " + ex.Message));
        }

        return alerts;
    }

    private async Task<List<ServiceAlert>> FetchPeerHealthAsync(CancellationToken cancellationToken)
    {
        var alerts = new List<ServiceAlert>();

        try
        {
            var baseUrl = _configuration["Services:Peer:Url"] ?? "http://peer-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{baseUrl}/api/peers/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                alerts.Add(CreateServiceUnreachableAlert("peer", "Peer Service health endpoint returned " + response.StatusCode));
                return alerts;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // HealthPercentage (alert when BELOW threshold, inverted logic)
            if (TryGetDouble(root, "healthPercentage", out var healthPct))
            {
                EvaluateThresholdInverted(alerts, "peer", "HealthPercentage", healthPct,
                    _thresholds.PeerHealthPercentageWarning, _thresholds.PeerHealthPercentageCritical,
                    "Peer network health is {0:F1}%");
            }

            // AverageLatency
            if (TryGetDouble(root, "averageLatencyMs", out var avgLatency))
            {
                EvaluateThreshold(alerts, "peer", "AverageLatency", avgLatency,
                    _thresholds.PeerAverageLatencyWarning, _thresholds.PeerAverageLatencyCritical,
                    "Peer average latency is {0:F0}ms");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch peer health for alert evaluation");
            alerts.Add(CreateServiceUnreachableAlert("peer", "Peer Service is unreachable: " + ex.Message));
        }

        return alerts;
    }

    internal static void EvaluateThreshold(
        List<ServiceAlert> alerts, string source, string metricName, double value,
        double warningThreshold, double criticalThreshold, string messageFormat)
    {
        if (value >= criticalThreshold)
        {
            alerts.Add(new ServiceAlert
            {
                Id = $"{source}-{metricName}-critical",
                Severity = AlertSeverity.Critical,
                Source = source,
                Message = string.Format(messageFormat, value),
                MetricName = metricName,
                CurrentValue = value,
                Threshold = criticalThreshold
            });
        }
        else if (value >= warningThreshold)
        {
            alerts.Add(new ServiceAlert
            {
                Id = $"{source}-{metricName}-warning",
                Severity = AlertSeverity.Warning,
                Source = source,
                Message = string.Format(messageFormat, value),
                MetricName = metricName,
                CurrentValue = value,
                Threshold = warningThreshold
            });
        }
    }

    internal static void EvaluateThresholdInverted(
        List<ServiceAlert> alerts, string source, string metricName, double value,
        double warningThreshold, double criticalThreshold, string messageFormat)
    {
        if (value <= criticalThreshold)
        {
            alerts.Add(new ServiceAlert
            {
                Id = $"{source}-{metricName}-critical",
                Severity = AlertSeverity.Critical,
                Source = source,
                Message = string.Format(messageFormat, value),
                MetricName = metricName,
                CurrentValue = value,
                Threshold = criticalThreshold
            });
        }
        else if (value <= warningThreshold)
        {
            alerts.Add(new ServiceAlert
            {
                Id = $"{source}-{metricName}-warning",
                Severity = AlertSeverity.Warning,
                Source = source,
                Message = string.Format(messageFormat, value),
                MetricName = metricName,
                CurrentValue = value,
                Threshold = warningThreshold
            });
        }
    }

    private static ServiceAlert CreateServiceUnreachableAlert(string source, string message)
    {
        return new ServiceAlert
        {
            Id = $"{source}-unreachable",
            Severity = AlertSeverity.Error,
            Source = source,
            Message = message,
            MetricName = "ServiceReachability"
        };
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;

        // Try camelCase first, then PascalCase
        if (element.TryGetProperty(propertyName, out var prop) ||
            element.TryGetProperty(char.ToUpperInvariant(propertyName[0]) + propertyName[1..], out prop))
        {
            if (prop.ValueKind == JsonValueKind.Number)
            {
                value = prop.GetDouble();
                return true;
            }
        }

        return false;
    }
}
