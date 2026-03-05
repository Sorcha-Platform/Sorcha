// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// Aggregated high-level metrics for the validator service.
/// </summary>
public record AggregatedMetricsViewModel
{
    [JsonPropertyName("validationSuccessRate")] public double ValidationSuccessRate { get; init; }
    [JsonPropertyName("docketsProposed")] public long DocketsProposed { get; init; }
    [JsonPropertyName("queueDepth")] public int QueueDepth { get; init; }
    [JsonPropertyName("cacheHitRatio")] public double CacheHitRatio { get; init; }
    [JsonPropertyName("uptimeSeconds")] public long UptimeSeconds { get; init; }
}

/// <summary>
/// Summary of validation activity metrics.
/// </summary>
public record ValidationSummaryViewModel
{
    [JsonPropertyName("totalValidated")] public long TotalValidated { get; init; }
    [JsonPropertyName("totalFailed")] public long TotalFailed { get; init; }
    [JsonPropertyName("averageLatencyMs")] public double AverageLatencyMs { get; init; }
    [JsonPropertyName("peakLatencyMs")] public double PeakLatencyMs { get; init; }
    [JsonPropertyName("validationsPerMinute")] public double ValidationsPerMinute { get; init; }
}

/// <summary>
/// Summary of consensus round metrics.
/// </summary>
public record ConsensusSummaryViewModel
{
    [JsonPropertyName("roundsCompleted")] public long RoundsCompleted { get; init; }
    [JsonPropertyName("roundsFailed")] public long RoundsFailed { get; init; }
    [JsonPropertyName("averageRoundTimeMs")] public double AverageRoundTimeMs { get; init; }
    [JsonPropertyName("docketsProposed")] public long DocketsProposed { get; init; }
    [JsonPropertyName("docketsAccepted")] public long DocketsAccepted { get; init; }
}

/// <summary>
/// Summary of transaction pool metrics.
/// </summary>
public record PoolSummaryViewModel
{
    [JsonPropertyName("activePoolCount")] public int ActivePoolCount { get; init; }
    [JsonPropertyName("totalPoolTransactions")] public long TotalPoolTransactions { get; init; }
    [JsonPropertyName("averagePoolSize")] public double AveragePoolSize { get; init; }
    [JsonPropertyName("peakPoolSize")] public int PeakPoolSize { get; init; }
}

/// <summary>
/// Summary of cache performance metrics.
/// </summary>
public record CacheSummaryViewModel
{
    [JsonPropertyName("totalHits")] public long TotalHits { get; init; }
    [JsonPropertyName("totalMisses")] public long TotalMisses { get; init; }
    [JsonPropertyName("hitRatio")] public double HitRatio { get; init; }
    [JsonPropertyName("evictions")] public long Evictions { get; init; }
    [JsonPropertyName("currentSize")] public long CurrentSize { get; init; }
}
