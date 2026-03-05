// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

public record AggregatedMetricsViewModel
{
    [JsonPropertyName("timestamp")] public DateTimeOffset Timestamp { get; init; }
    [JsonPropertyName("validation")] public ValidationSummaryViewModel Validation { get; init; } = new();
    [JsonPropertyName("consensus")] public ConsensusSummaryViewModel Consensus { get; init; } = new();
    [JsonPropertyName("pools")] public PoolSummaryViewModel Pools { get; init; } = new();
    [JsonPropertyName("caches")] public CacheSummaryViewModel Caches { get; init; } = new();
}

public record ValidationSummaryViewModel
{
    [JsonPropertyName("totalValidated")] public long TotalValidated { get; init; }
    [JsonPropertyName("totalSuccessful")] public long TotalSuccessful { get; init; }
    [JsonPropertyName("totalFailed")] public long TotalFailed { get; init; }
    [JsonPropertyName("successRate")] public double SuccessRate { get; init; }
    [JsonPropertyName("averageValidationTimeMs")] public double AverageValidationTimeMs { get; init; }
    [JsonPropertyName("inProgress")] public int InProgress { get; init; }
    [JsonPropertyName("errorsByCategory")] public Dictionary<string, long> ErrorsByCategory { get; init; } = new();
}

public record ConsensusSummaryViewModel
{
    [JsonPropertyName("docketsProposed")] public long DocketsProposed { get; init; }
    [JsonPropertyName("docketsDistributed")] public long DocketsDistributed { get; init; }
    [JsonPropertyName("registerSubmissions")] public long RegisterSubmissions { get; init; }
    [JsonPropertyName("failedSubmissions")] public long FailedSubmissions { get; init; }
    [JsonPropertyName("consensusFailures")] public long ConsensusFailures { get; init; }
    [JsonPropertyName("successfulRecoveries")] public long SuccessfulRecoveries { get; init; }
    [JsonPropertyName("docketsAbandoned")] public long DocketsAbandoned { get; init; }
    [JsonPropertyName("pendingDockets")] public int PendingDockets { get; init; }
}

public record PoolSummaryViewModel
{
    [JsonPropertyName("queueSizes")] public Dictionary<string, int> QueueSizes { get; init; } = new();
    [JsonPropertyName("oldestTransaction")] public DateTimeOffset? OldestTransaction { get; init; }
    [JsonPropertyName("newestTransaction")] public DateTimeOffset? NewestTransaction { get; init; }
    [JsonPropertyName("totalEnqueued")] public long TotalEnqueued { get; init; }
    [JsonPropertyName("totalDequeued")] public long TotalDequeued { get; init; }
    [JsonPropertyName("totalExpired")] public long TotalExpired { get; init; }
}

public record CacheSummaryViewModel
{
    [JsonPropertyName("blueprintCacheHits")] public long BlueprintCacheHits { get; init; }
    [JsonPropertyName("blueprintCacheMisses")] public long BlueprintCacheMisses { get; init; }
    [JsonPropertyName("hitRatio")] public double HitRatio { get; init; }
    [JsonPropertyName("localEntryCount")] public int LocalEntryCount { get; init; }
    [JsonPropertyName("distributedEntryCount")] public int DistributedEntryCount { get; init; }
}
