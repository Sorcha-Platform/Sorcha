// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for fetching validator/mempool status for admin display.
/// </summary>
public interface IValidatorAdminService
{
    /// <summary>
    /// Gets overall mempool status across all registers.
    /// </summary>
    Task<ValidatorStatusViewModel> GetMempoolStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets mempool statistics for a specific register.
    /// </summary>
    Task<RegisterMempoolStat> GetRegisterMempoolAsync(string registerId, CancellationToken cancellationToken = default);

    // Consent Queue
    Task<ConsentQueueViewModel> GetConsentQueueAsync(CancellationToken ct = default);
    Task<List<PendingValidatorViewModel>> GetPendingValidatorsAsync(string registerId, CancellationToken ct = default);
    Task<bool> ApproveValidatorAsync(string registerId, string validatorId, CancellationToken ct = default);
    Task<bool> RejectValidatorAsync(string registerId, string validatorId, string? reason = null, CancellationToken ct = default);
    Task<List<ApprovedValidatorInfo>> RefreshApprovedValidatorsAsync(string registerId, CancellationToken ct = default);

    // Metrics
    Task<AggregatedMetricsViewModel> GetAggregatedMetricsAsync(CancellationToken ct = default);
    Task<ValidationSummaryViewModel> GetValidationMetricsAsync(CancellationToken ct = default);
    Task<ConsensusSummaryViewModel> GetConsensusMetricsAsync(CancellationToken ct = default);
    Task<PoolSummaryViewModel> GetPoolMetricsAsync(CancellationToken ct = default);
    Task<CacheSummaryViewModel> GetCacheMetricsAsync(CancellationToken ct = default);

    // Threshold
    Task<List<ThresholdConfigViewModel>> GetThresholdStatusAsync(CancellationToken ct = default);
    Task<ThresholdConfigViewModel> SetupThresholdAsync(ThresholdSetupRequest request, CancellationToken ct = default);

    // Config
    Task<ValidatorConfigViewModel> GetConfigAsync(CancellationToken ct = default);
}
