// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ApiGateway.Models;

/// <summary>
/// Severity level for a service alert.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlertSeverity
{
    Info,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Represents an active alert generated from service metric evaluation.
/// </summary>
public record ServiceAlert
{
    /// <summary>Unique identifier for the resource.</summary>
    public required string Id { get; init; }
    /// <summary>The severity.</summary>
    public required AlertSeverity Severity { get; init; }
    /// <summary>The source.</summary>
    public required string Source { get; init; }
    /// <summary>Human-readable message.</summary>
    public required string Message { get; init; }
    /// <summary>The metric name.</summary>
    public string? MetricName { get; init; }
    /// <summary>Numeric value for current value.</summary>
    public double? CurrentValue { get; init; }
    /// <summary>Numeric value for threshold.</summary>
    public double? Threshold { get; init; }
    /// <summary>Timestamp associated with this record (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Response containing all active alerts and summary counts.
/// </summary>
public record AlertsResponse
{
    /// <summary>Collection of alerts associated with this resource.</summary>
    public IReadOnlyList<ServiceAlert> Alerts { get; init; } = [];
    /// <summary>Numeric value for info count.</summary>
    public int InfoCount { get; init; }
    /// <summary>Numeric value for warning count.</summary>
    public int WarningCount { get; init; }
    /// <summary>Numeric value for error count.</summary>
    public int ErrorCount { get; init; }
    /// <summary>Numeric value for critical count.</summary>
    public int CriticalCount { get; init; }
    public int TotalCount => Alerts.Count;
    /// <summary>Timestamp associated with this record (UTC).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Configuration for alert threshold values. Bindable from appsettings.json.
/// </summary>
public class AlertThresholdConfig
{
    /// <summary>Numeric value for validator failed warning.</summary>
    public double ValidatorFailedWarning { get; set; } = 10;
    /// <summary>Numeric value for validator failed critical.</summary>
    public double ValidatorFailedCritical { get; set; } = 50;
    /// <summary>Numeric value for validator success rate warning.</summary>
    public double ValidatorSuccessRateWarning { get; set; } = 95;
    /// <summary>Numeric value for validator success rate critical.</summary>
    public double ValidatorSuccessRateCritical { get; set; } = 80;
    /// <summary>Numeric value for consensus failures warning.</summary>
    public double ConsensusFailuresWarning { get; set; } = 5;
    /// <summary>Numeric value for consensus failures critical.</summary>
    public double ConsensusFailuresCritical { get; set; } = 20;
    /// <summary>Numeric value for dockets abandoned warning.</summary>
    public double DocketsAbandonedWarning { get; set; } = 3;
    /// <summary>Numeric value for dockets abandoned critical.</summary>
    public double DocketsAbandonedCritical { get; set; } = 10;
    /// <summary>Numeric value for validator exceptions warning.</summary>
    public double ValidatorExceptionsWarning { get; set; } = 5;
    /// <summary>Numeric value for validator exceptions critical.</summary>
    public double ValidatorExceptionsCritical { get; set; } = 25;
    /// <summary>Numeric value for peer health percentage warning.</summary>
    public double PeerHealthPercentageWarning { get; set; } = 70;
    /// <summary>Numeric value for peer health percentage critical.</summary>
    public double PeerHealthPercentageCritical { get; set; } = 40;
    /// <summary>Numeric value for peer average latency warning.</summary>
    public double PeerAverageLatencyWarning { get; set; } = 500;
    /// <summary>Numeric value for peer average latency critical.</summary>
    public double PeerAverageLatencyCritical { get; set; } = 2000;
}
