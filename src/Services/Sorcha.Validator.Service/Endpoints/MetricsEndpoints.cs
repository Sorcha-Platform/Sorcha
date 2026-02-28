// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Endpoints;

/// <summary>
/// API endpoints for metrics, health, and operational statistics (VAL-9.45)
/// </summary>
public static class MetricsEndpoints
{
    /// <summary>
    /// Maps metrics endpoints to the application
    /// </summary>
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/metrics")
            .WithTags("Metrics")
            .RequireAuthorization("RequireAuthenticated");

        // Aggregated metrics endpoint
        group.MapGet("/", GetAggregatedMetrics)
            .WithName("GetAggregatedMetrics")
            .WithSummary("Get aggregated validator metrics")
            .WithDescription("Returns comprehensive metrics from all validator subsystems including validation engine, consensus, memory pools, and caches")
            .Produces<AggregatedMetrics>(StatusCodes.Status200OK);

        // Validation engine metrics
        group.MapGet("/validation", GetValidationMetrics)
            .WithName("GetValidationMetrics")
            .WithSummary("Get validation engine metrics")
            .WithDescription("Returns validation engine statistics including transaction counts, success rates, and error breakdowns")
            .Produces<ValidationMetricsResponse>(StatusCodes.Status200OK);

        // Consensus metrics
        group.MapGet("/consensus", GetConsensusMetrics)
            .WithName("GetConsensusMetrics")
            .WithSummary("Get consensus metrics")
            .WithDescription("Returns consensus-related statistics including docket distribution, failure handling, and pending dockets")
            .Produces<ConsensusMetricsResponse>(StatusCodes.Status200OK);

        // Memory pool metrics (global)
        group.MapGet("/pools", GetPoolMetrics)
            .WithName("GetPoolMetrics")
            .WithSummary("Get memory pool metrics")
            .WithDescription("Returns global memory pool statistics across all registers")
            .Produces<PoolMetricsResponse>(StatusCodes.Status200OK);

        // Cache metrics
        group.MapGet("/caches", GetCacheMetrics)
            .WithName("GetCacheMetrics")
            .WithSummary("Get cache metrics")
            .WithDescription("Returns cache statistics for blueprints and genesis configurations")
            .Produces<CacheMetricsResponse>(StatusCodes.Status200OK);

        // Configuration endpoint (read-only)
        group.MapGet("/config", GetCurrentConfiguration)
            .WithName("GetCurrentConfiguration")
            .WithSummary("Get current configuration")
            .WithDescription("Returns the current validator configuration settings (sensitive values redacted)")
            .Produces<ConfigurationResponse>(StatusCodes.Status200OK);

        return app;
    }

    /// <summary>
    /// Get aggregated metrics from all subsystems.
    /// Services not registered in the DI container (e.g. consensus services in MVD)
    /// are resolved optionally and return default/empty stats.
    /// </summary>
    private static async Task<IResult> GetAggregatedMetrics(
        [FromServices] IValidationEngine validationEngine,
        [FromServices] IVerifiedTransactionQueue verifiedQueue,
        [FromServices] IBlueprintCache blueprintCache,
        [FromServices] IPendingDocketStore pendingDocketStore,
        [FromServices] IExceptionResponseHandler exceptionHandler,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var validationStats = validationEngine.GetStats();
        var queueStats = verifiedQueue.GetStats();
        var cacheStats = await blueprintCache.GetStatsAsync(cancellationToken);
        var pendingStats = pendingDocketStore.GetStats();
        var exceptionStats = exceptionHandler.GetStats();

        // Optional: consensus services are not wired in single-validator MVD
        var docketDistributor = httpContext.RequestServices.GetService<IDocketDistributor>();
        var failureHandler = httpContext.RequestServices.GetService<IConsensusFailureHandler>();
        var distributorStats = docketDistributor?.GetStats();
        var failureStats = failureHandler?.GetStats();

        var metrics = new AggregatedMetrics
        {
            Timestamp = DateTimeOffset.UtcNow,
            Validation = new ValidationSummary
            {
                TotalValidated = validationStats.TotalValidated,
                TotalSuccessful = validationStats.TotalSuccessful,
                TotalFailed = validationStats.TotalFailed,
                SuccessRate = validationStats.TotalValidated > 0
                    ? (double)validationStats.TotalSuccessful / validationStats.TotalValidated * 100
                    : 0,
                AverageValidationTimeMs = validationStats.AverageValidationDuration.TotalMilliseconds,
                InProgress = validationStats.InProgress,
                ErrorsByCategory = validationStats.ErrorsByCategory
                    .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value)
            },
            Consensus = new ConsensusSummary
            {
                DocketsDistributed = distributorStats?.TotalConfirmedBroadcasts ?? 0,
                DocketsProposed = distributorStats?.TotalProposedBroadcasts ?? 0,
                RegisterSubmissions = distributorStats?.TotalRegisterSubmissions ?? 0,
                FailedSubmissions = distributorStats?.FailedRegisterSubmissions ?? 0,
                ConsensusFailures = failureStats?.TotalFailures ?? 0,
                SuccessfulRecoveries = failureStats?.SuccessfulRecoveries ?? 0,
                DocketsAbandoned = failureStats?.DocketsAbandoned ?? 0,
                PendingDockets = pendingStats.TotalPending
            },
            Pools = new PoolSummary
            {
                VerifiedQueueSize = queueStats.TotalTransactions,
                ActiveRegisters = queueStats.ActiveRegisters,
                OldestTransaction = queueStats.OldestTransaction,
                NewestTransaction = queueStats.NewestTransaction,
                TotalEnqueued = queueStats.TotalEnqueued,
                TotalDequeued = queueStats.TotalDequeued,
                TotalExpired = queueStats.TotalExpired
            },
            Caches = new CacheSummary
            {
                BlueprintCacheHits = cacheStats.TotalHits,
                BlueprintCacheMisses = cacheStats.TotalMisses,
                BlueprintHitRatio = cacheStats.HitRatio,
                LocalCacheEntries = cacheStats.LocalCacheEntries,
                RedisCacheEntries = cacheStats.RedisCacheEntries,
                AverageLatencyMs = cacheStats.AverageLatencyMs
            },
            Exceptions = new ExceptionSummary
            {
                TotalCreated = exceptionStats.TotalCreated,
                TotalDelivered = exceptionStats.TotalDelivered,
                ByCode = exceptionStats.ByCode.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value)
            }
        };

        return Results.Ok(metrics);
    }

    /// <summary>
    /// Get validation engine specific metrics
    /// </summary>
    private static IResult GetValidationMetrics([FromServices] IValidationEngine validationEngine)
    {
        var stats = validationEngine.GetStats();

        var response = new ValidationMetricsResponse
        {
            Timestamp = DateTimeOffset.UtcNow,
            TotalValidated = stats.TotalValidated,
            TotalSuccessful = stats.TotalSuccessful,
            TotalFailed = stats.TotalFailed,
            SuccessRate = stats.SuccessRate * 100, // Convert to percentage
            AverageValidationTimeMs = stats.AverageValidationDuration.TotalMilliseconds,
            InProgress = stats.InProgress,
            ErrorsByCategory = stats.ErrorsByCategory
                .ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value)
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Get consensus-related metrics
    /// </summary>
    private static IResult GetConsensusMetrics(
        [FromServices] IPendingDocketStore pendingDocketStore,
        HttpContext httpContext)
    {
        var docketDistributor = httpContext.RequestServices.GetService<IDocketDistributor>();
        var failureHandler = httpContext.RequestServices.GetService<IConsensusFailureHandler>();
        var distributorStats = docketDistributor?.GetStats();
        var failureStats = failureHandler?.GetStats();
        var pendingStats = pendingDocketStore.GetStats();

        var response = new ConsensusMetricsResponse
        {
            Timestamp = DateTimeOffset.UtcNow,
            Distribution = new DistributionMetrics
            {
                TotalProposedBroadcasts = distributorStats?.TotalProposedBroadcasts ?? 0,
                TotalConfirmedBroadcasts = distributorStats?.TotalConfirmedBroadcasts ?? 0,
                TotalRegisterSubmissions = distributorStats?.TotalRegisterSubmissions ?? 0,
                FailedRegisterSubmissions = distributorStats?.FailedRegisterSubmissions ?? 0,
                AverageBroadcastTimeMs = distributorStats?.AverageBroadcastTimeMs ?? 0,
                LastBroadcastAt = distributorStats?.LastBroadcastAt
            },
            Failures = new FailureMetrics
            {
                TotalFailures = failureStats?.TotalFailures ?? 0,
                SuccessfulRecoveries = failureStats?.SuccessfulRecoveries ?? 0,
                DocketsAbandoned = failureStats?.DocketsAbandoned ?? 0,
                TotalRetryAttempts = failureStats?.TotalRetryAttempts ?? 0,
                TransactionsReturnedToPool = failureStats?.TransactionsReturnedToPool ?? 0,
                RecoveryRate = failureStats?.RecoveryRate ?? 0
            },
            Pending = new PendingMetrics
            {
                TotalPending = pendingStats.TotalPending,
                TotalAdded = pendingStats.TotalAdded,
                TotalRemoved = pendingStats.TotalRemoved,
                AverageTimeInStoreMs = pendingStats.AverageTimeInStoreMs,
                ByStatus = pendingStats.ByStatus.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => kvp.Value)
            }
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Get memory pool metrics
    /// </summary>
    private static IResult GetPoolMetrics([FromServices] IVerifiedTransactionQueue verifiedQueue)
    {
        var stats = verifiedQueue.GetStats();

        var response = new PoolMetricsResponse
        {
            Timestamp = DateTimeOffset.UtcNow,
            VerifiedQueue = new VerifiedQueueMetrics
            {
                TotalTransactions = stats.TotalTransactions,
                ActiveRegisters = stats.ActiveRegisters,
                AverageTransactionsPerRegister = stats.AverageTransactionsPerRegister,
                OldestTransaction = stats.OldestTransaction,
                NewestTransaction = stats.NewestTransaction,
                TotalEnqueued = stats.TotalEnqueued,
                TotalDequeued = stats.TotalDequeued,
                TotalExpired = stats.TotalExpired
            }
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Get cache metrics
    /// </summary>
    private static async Task<IResult> GetCacheMetrics(
        [FromServices] IBlueprintCache blueprintCache,
        CancellationToken cancellationToken)
    {
        var cacheStats = await blueprintCache.GetStatsAsync(cancellationToken);

        var response = new CacheMetricsResponse
        {
            Timestamp = DateTimeOffset.UtcNow,
            BlueprintCache = new BlueprintCacheMetrics
            {
                TotalHits = cacheStats.TotalHits,
                TotalMisses = cacheStats.TotalMisses,
                HitRatio = cacheStats.HitRatio,
                LocalCacheEntries = cacheStats.LocalCacheEntries,
                LocalCacheHits = cacheStats.LocalCacheHits,
                RedisCacheEntries = cacheStats.RedisCacheEntries,
                RedisCacheHits = cacheStats.RedisCacheHits,
                AverageLatencyMs = cacheStats.AverageLatencyMs
            }
        };

        return Results.Ok(response);
    }

    /// <summary>
    /// Get current configuration (redacted sensitive values)
    /// </summary>
    private static IResult GetCurrentConfiguration(
        [FromServices] IOptions<ValidatorConfiguration> validatorConfig,
        [FromServices] IOptions<ConsensusConfiguration> consensusConfig,
        [FromServices] IOptions<MemPoolConfiguration> memPoolConfig,
        [FromServices] IOptions<DocketBuildConfiguration> docketBuildConfig,
        [FromServices] IOptions<ValidationEngineConfiguration> validationEngineConfig)
    {
        var response = new ConfigurationResponse
        {
            Timestamp = DateTimeOffset.UtcNow,
            Validator = new ValidatorConfigSummary
            {
                ValidatorId = validatorConfig.Value.ValidatorId,
                MaxReorgDepth = validatorConfig.Value.MaxReorgDepth,
                HasGrpcEndpoint = !string.IsNullOrEmpty(validatorConfig.Value.GrpcEndpoint)
            },
            Consensus = new ConsensusConfigSummary
            {
                ApprovalThreshold = consensusConfig.Value.ApprovalThreshold,
                VoteTimeoutSeconds = (int)consensusConfig.Value.VoteTimeout.TotalSeconds,
                MaxRetries = consensusConfig.Value.MaxRetries,
                RequireQuorum = consensusConfig.Value.RequireQuorum
            },
            MemPool = new MemPoolConfigSummary
            {
                MaxSize = memPoolConfig.Value.MaxSize,
                DefaultTTLMinutes = (int)memPoolConfig.Value.DefaultTTL.TotalMinutes,
                HighPriorityQuota = memPoolConfig.Value.HighPriorityQuota,
                CleanupIntervalMinutes = (int)memPoolConfig.Value.CleanupInterval.TotalMinutes
            },
            DocketBuild = new DocketBuildConfigSummary
            {
                TimeThresholdSeconds = (int)docketBuildConfig.Value.TimeThreshold.TotalSeconds,
                SizeThreshold = docketBuildConfig.Value.SizeThreshold,
                MaxTransactionsPerDocket = docketBuildConfig.Value.MaxTransactionsPerDocket,
                AllowEmptyDockets = docketBuildConfig.Value.AllowEmptyDockets
            },
            ValidationEngine = new ValidationEngineConfigSummary
            {
                BatchSize = validationEngineConfig.Value.BatchSize,
                EnableParallelValidation = validationEngineConfig.Value.EnableParallelValidation,
                MaxConcurrentValidationsPerRegister = validationEngineConfig.Value.MaxConcurrentValidationsPerRegister,
                ValidationTimeoutSeconds = (int)validationEngineConfig.Value.ValidationTimeout.TotalSeconds,
                EnableSchemaValidation = validationEngineConfig.Value.EnableSchemaValidation,
                EnableSignatureVerification = validationEngineConfig.Value.EnableSignatureVerification,
                EnableChainValidation = validationEngineConfig.Value.EnableChainValidation,
                EnableMetrics = validationEngineConfig.Value.EnableMetrics
            }
        };

        return Results.Ok(response);
    }
}


/// <summary>
/// Aggregated metrics from all validator subsystems
/// </summary>
public record AggregatedMetrics
{
    public DateTimeOffset Timestamp { get; init; }
    public required ValidationSummary Validation { get; init; }
    public required ConsensusSummary Consensus { get; init; }
    public required PoolSummary Pools { get; init; }
    public required CacheSummary Caches { get; init; }
    public required ExceptionSummary Exceptions { get; init; }
}

public record ValidationSummary
{
    public long TotalValidated { get; init; }
    public long TotalSuccessful { get; init; }
    public long TotalFailed { get; init; }
    public double SuccessRate { get; init; }
    public double AverageValidationTimeMs { get; init; }
    public int InProgress { get; init; }
    public IReadOnlyDictionary<string, long> ErrorsByCategory { get; init; } = new Dictionary<string, long>();
}

public record ConsensusSummary
{
    public long DocketsProposed { get; init; }
    public long DocketsDistributed { get; init; }
    public long RegisterSubmissions { get; init; }
    public long FailedSubmissions { get; init; }
    public long ConsensusFailures { get; init; }
    public long SuccessfulRecoveries { get; init; }
    public long DocketsAbandoned { get; init; }
    public int PendingDockets { get; init; }
}

public record PoolSummary
{
    public int VerifiedQueueSize { get; init; }
    public int ActiveRegisters { get; init; }
    public DateTimeOffset? OldestTransaction { get; init; }
    public DateTimeOffset? NewestTransaction { get; init; }
    public long TotalEnqueued { get; init; }
    public long TotalDequeued { get; init; }
    public long TotalExpired { get; init; }
}

public record CacheSummary
{
    public long BlueprintCacheHits { get; init; }
    public long BlueprintCacheMisses { get; init; }
    public double BlueprintHitRatio { get; init; }
    public int LocalCacheEntries { get; init; }
    public long RedisCacheEntries { get; init; }
    public double AverageLatencyMs { get; init; }
}

public record ExceptionSummary
{
    public long TotalCreated { get; init; }
    public long TotalDelivered { get; init; }
    public IReadOnlyDictionary<string, long> ByCode { get; init; } = new Dictionary<string, long>();
}

/// <summary>
/// Validation engine metrics response
/// </summary>
public record ValidationMetricsResponse
{
    public DateTimeOffset Timestamp { get; init; }
    public long TotalValidated { get; init; }
    public long TotalSuccessful { get; init; }
    public long TotalFailed { get; init; }
    public double SuccessRate { get; init; }
    public double AverageValidationTimeMs { get; init; }
    public int InProgress { get; init; }
    public IReadOnlyDictionary<string, long> ErrorsByCategory { get; init; } = new Dictionary<string, long>();
}

/// <summary>
/// Consensus metrics response
/// </summary>
public record ConsensusMetricsResponse
{
    public DateTimeOffset Timestamp { get; init; }
    public required DistributionMetrics Distribution { get; init; }
    public required FailureMetrics Failures { get; init; }
    public required PendingMetrics Pending { get; init; }
}

public record DistributionMetrics
{
    public long TotalProposedBroadcasts { get; init; }
    public long TotalConfirmedBroadcasts { get; init; }
    public long TotalRegisterSubmissions { get; init; }
    public long FailedRegisterSubmissions { get; init; }
    public double AverageBroadcastTimeMs { get; init; }
    public DateTimeOffset? LastBroadcastAt { get; init; }
}

public record FailureMetrics
{
    public long TotalFailures { get; init; }
    public long SuccessfulRecoveries { get; init; }
    public long DocketsAbandoned { get; init; }
    public long TotalRetryAttempts { get; init; }
    public long TransactionsReturnedToPool { get; init; }
    public double RecoveryRate { get; init; }
}

public record PendingMetrics
{
    public int TotalPending { get; init; }
    public long TotalAdded { get; init; }
    public long TotalRemoved { get; init; }
    public double AverageTimeInStoreMs { get; init; }
    public IReadOnlyDictionary<string, int> ByStatus { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Pool metrics response
/// </summary>
public record PoolMetricsResponse
{
    public DateTimeOffset Timestamp { get; init; }
    public required VerifiedQueueMetrics VerifiedQueue { get; init; }
}

public record VerifiedQueueMetrics
{
    public int TotalTransactions { get; init; }
    public int ActiveRegisters { get; init; }
    public double AverageTransactionsPerRegister { get; init; }
    public DateTimeOffset? OldestTransaction { get; init; }
    public DateTimeOffset? NewestTransaction { get; init; }
    public long TotalEnqueued { get; init; }
    public long TotalDequeued { get; init; }
    public long TotalExpired { get; init; }
}

/// <summary>
/// Cache metrics response
/// </summary>
public record CacheMetricsResponse
{
    public DateTimeOffset Timestamp { get; init; }
    public required BlueprintCacheMetrics BlueprintCache { get; init; }
}

public record BlueprintCacheMetrics
{
    public long TotalHits { get; init; }
    public long TotalMisses { get; init; }
    public double HitRatio { get; init; }
    public int LocalCacheEntries { get; init; }
    public long LocalCacheHits { get; init; }
    public long RedisCacheEntries { get; init; }
    public long RedisCacheHits { get; init; }
    public double AverageLatencyMs { get; init; }
}

/// <summary>
/// Configuration response (sensitive values redacted)
/// </summary>
public record ConfigurationResponse
{
    public DateTimeOffset Timestamp { get; init; }
    public required ValidatorConfigSummary Validator { get; init; }
    public required ConsensusConfigSummary Consensus { get; init; }
    public required MemPoolConfigSummary MemPool { get; init; }
    public required DocketBuildConfigSummary DocketBuild { get; init; }
    public required ValidationEngineConfigSummary ValidationEngine { get; init; }
}

public record ValidatorConfigSummary
{
    public string ValidatorId { get; init; } = string.Empty;
    public int MaxReorgDepth { get; init; }
    public bool HasGrpcEndpoint { get; init; }
}

public record ConsensusConfigSummary
{
    public double ApprovalThreshold { get; init; }
    public int VoteTimeoutSeconds { get; init; }
    public int MaxRetries { get; init; }
    public bool RequireQuorum { get; init; }
}

public record MemPoolConfigSummary
{
    public int MaxSize { get; init; }
    public int DefaultTTLMinutes { get; init; }
    public double HighPriorityQuota { get; init; }
    public int CleanupIntervalMinutes { get; init; }
}

public record DocketBuildConfigSummary
{
    public int TimeThresholdSeconds { get; init; }
    public int SizeThreshold { get; init; }
    public int MaxTransactionsPerDocket { get; init; }
    public bool AllowEmptyDockets { get; init; }
}

public record ValidationEngineConfigSummary
{
    public int BatchSize { get; init; }
    public bool EnableParallelValidation { get; init; }
    public int MaxConcurrentValidationsPerRegister { get; init; }
    public int ValidationTimeoutSeconds { get; init; }
    public bool EnableSchemaValidation { get; init; }
    public bool EnableSignatureVerification { get; init; }
    public bool EnableChainValidation { get; init; }
    public bool EnableMetrics { get; init; }
}

