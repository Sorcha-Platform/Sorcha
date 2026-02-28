// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Handles consensus failures by implementing retry logic with exponential backoff.
/// Abandons dockets after max retries and returns transactions to the memory pool.
/// </summary>
public class ConsensusFailureHandler : IConsensusFailureHandler
{
    private readonly ConsensusConfiguration _consensusConfig;
    private readonly ISignatureCollector _signatureCollector;
    private readonly IValidatorRegistry _validatorRegistry;
    private readonly IGenesisConfigService _genesisConfigService;
    private readonly IMemPoolManager _memPoolManager;
    private readonly IPendingDocketStore _pendingDocketStore;
    private readonly ILogger<ConsensusFailureHandler> _logger;

    // Statistics
    private long _totalFailures;
    private long _successfulRecoveries;
    private long _docketsAbandoned;
    private long _totalRetryAttempts;
    private long _transactionsReturnedToPool;

    public ConsensusFailureHandler(
        IOptions<ConsensusConfiguration> consensusConfig,
        ISignatureCollector signatureCollector,
        IValidatorRegistry validatorRegistry,
        IGenesisConfigService genesisConfigService,
        IMemPoolManager memPoolManager,
        IPendingDocketStore pendingDocketStore,
        ILogger<ConsensusFailureHandler> logger)
    {
        _consensusConfig = consensusConfig?.Value ?? throw new ArgumentNullException(nameof(consensusConfig));
        _signatureCollector = signatureCollector ?? throw new ArgumentNullException(nameof(signatureCollector));
        _validatorRegistry = validatorRegistry ?? throw new ArgumentNullException(nameof(validatorRegistry));
        _genesisConfigService = genesisConfigService ?? throw new ArgumentNullException(nameof(genesisConfigService));
        _memPoolManager = memPoolManager ?? throw new ArgumentNullException(nameof(memPoolManager));
        _pendingDocketStore = pendingDocketStore ?? throw new ArgumentNullException(nameof(pendingDocketStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ConsensusRecoveryResult> HandleFailureAsync(
        Docket docket,
        SignatureCollectionResult result,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);
        ArgumentNullException.ThrowIfNull(result);

        Interlocked.Increment(ref _totalFailures);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogWarning(
            "Handling consensus failure for docket {DocketId}: {Approvals}/{Total} signatures " +
            "(needed: {Min}, timed out: {TimedOut})",
            docket.DocketId, result.Approvals, result.TotalValidators,
            _consensusConfig.ApprovalThreshold * result.TotalValidators, result.TimedOut);

        // Check if we already have enough signatures
        if (result.ThresholdMet)
        {
            _logger.LogInformation(
                "Threshold already met for docket {DocketId}, no recovery needed",
                docket.DocketId);

            return new ConsensusRecoveryResult
            {
                Action = ConsensusRecoveryAction.NoActionNeeded,
                Succeeded = true,
                RetryAttempts = 0,
                Reason = "Threshold already met",
                RecoveryDuration = stopwatch.Elapsed
            };
        }

        // Get current retry count from docket metadata
        var retryCount = GetRetryCount(docket);

        // Check if we've exceeded max retries
        if (retryCount >= _consensusConfig.MaxRetries)
        {
            _logger.LogError(
                "Max retries ({MaxRetries}) exceeded for docket {DocketId}, abandoning",
                _consensusConfig.MaxRetries, docket.DocketId);

            await AbandonDocketAsync(docket, $"Max retries ({_consensusConfig.MaxRetries}) exceeded", ct);

            var returnedCount = await ReturnTransactionsToPoolAsync(docket, ct);

            Interlocked.Increment(ref _docketsAbandoned);

            return new ConsensusRecoveryResult
            {
                Action = ConsensusRecoveryAction.Abandon,
                Succeeded = false,
                RetryAttempts = retryCount,
                Reason = $"Max retries exceeded after {retryCount} attempts",
                TransactionsReturnedToPool = returnedCount,
                RecoveryDuration = stopwatch.Elapsed
            };
        }

        // Attempt retry with exponential backoff
        var backoffDelay = CalculateBackoff(retryCount);
        _logger.LogInformation(
            "Attempting retry {RetryNumber}/{MaxRetries} for docket {DocketId} after {Delay}ms backoff",
            retryCount + 1, _consensusConfig.MaxRetries, docket.DocketId, backoffDelay.TotalMilliseconds);

        await Task.Delay(backoffDelay, ct);

        Interlocked.Increment(ref _totalRetryAttempts);

        // Get fresh validators list and consensus config
        var validators = await _validatorRegistry.GetActiveValidatorsAsync(docket.RegisterId, ct);
        var consensusConfig = await _genesisConfigService.GetConsensusConfigAsync(docket.RegisterId, ct);

        // Retry signature collection
        var retryResult = await _signatureCollector.CollectSignaturesAsync(
            docket, consensusConfig, validators, ct);

        if (retryResult.ThresholdMet)
        {
            Interlocked.Increment(ref _successfulRecoveries);

            _logger.LogInformation(
                "Recovery succeeded for docket {DocketId} on retry {RetryNumber}",
                docket.DocketId, retryCount + 1);

            // Update docket status (Docket is a class, not a record)
            docket.Status = DocketStatus.Proposed;
            // In a real implementation, we'd merge signatures here

            return new ConsensusRecoveryResult
            {
                Action = ConsensusRecoveryAction.Retry,
                Succeeded = true,
                RetryAttempts = retryCount + 1,
                Reason = $"Threshold met on retry {retryCount + 1}",
                UpdatedDocket = docket,
                RecoveryDuration = stopwatch.Elapsed
            };
        }

        // Still failed, return retry action for potential further retries
        _logger.LogWarning(
            "Retry {RetryNumber} failed for docket {DocketId}: {Approvals}/{Total} signatures",
            retryCount + 1, docket.DocketId, retryResult.Approvals, retryResult.TotalValidators);

        return new ConsensusRecoveryResult
        {
            Action = ConsensusRecoveryAction.Retry,
            Succeeded = false,
            RetryAttempts = retryCount + 1,
            Reason = $"Retry {retryCount + 1} failed: {retryResult.Approvals}/{retryResult.TotalValidators} signatures",
            RecoveryDuration = stopwatch.Elapsed
        };
    }

    /// <inheritdoc/>
    public async Task AbandonDocketAsync(
        Docket docket,
        string reason,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        _logger.LogError(
            "Abandoning docket {DocketId} for register {RegisterId}: {Reason}",
            docket.DocketId, docket.RegisterId, reason);

        // Mark docket as rejected and persist status
        docket.Status = DocketStatus.Rejected;

        try
        {
            await _pendingDocketStore.UpdateStatusAsync(docket.DocketId, DocketStatus.Rejected, ct);
            _logger.LogInformation("Persisted rejected status for docket {DocketId}: {Reason}", docket.DocketId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist rejected status for docket {DocketId} — status updated in memory only", docket.DocketId);
        }
    }

    /// <inheritdoc/>
    public async Task<int> ReturnTransactionsToPoolAsync(
        Docket docket,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);

        if (docket.Transactions.Count == 0)
        {
            _logger.LogDebug("No transactions to return for docket {DocketId}", docket.DocketId);
            return 0;
        }

        _logger.LogInformation(
            "Returning {Count} transactions from abandoned docket {DocketId} to memory pool",
            docket.Transactions.Count, docket.DocketId);

        try
        {
            // Use bulk return method for efficiency
            await _memPoolManager.ReturnTransactionsAsync(
                docket.RegisterId,
                docket.Transactions.ToList(),
                ct);

            var returnedCount = docket.Transactions.Count;
            Interlocked.Add(ref _transactionsReturnedToPool, returnedCount);

            _logger.LogInformation(
                "Successfully returned {Count} transactions to pool",
                returnedCount);

            return returnedCount;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to return transactions from docket {DocketId} to pool",
                docket.DocketId);
            return 0;
        }
    }

    /// <inheritdoc/>
    public ConsensusFailureStats GetStats()
    {
        return new ConsensusFailureStats
        {
            TotalFailures = Interlocked.Read(ref _totalFailures),
            SuccessfulRecoveries = Interlocked.Read(ref _successfulRecoveries),
            DocketsAbandoned = Interlocked.Read(ref _docketsAbandoned),
            TotalRetryAttempts = Interlocked.Read(ref _totalRetryAttempts),
            TransactionsReturnedToPool = Interlocked.Read(ref _transactionsReturnedToPool)
        };
    }


    private static int GetRetryCount(Docket docket)
    {
        if (docket.Metadata?.TryGetValue("retryCount", out var retryStr) == true &&
            int.TryParse(retryStr, out var retryCount))
        {
            return retryCount;
        }
        return 0;
    }

    private TimeSpan CalculateBackoff(int retryCount)
    {
        // Exponential backoff with jitter
        var baseDelay = Math.Pow(2, retryCount) * 100; // 100ms, 200ms, 400ms, 800ms...
        var jitter = Random.Shared.Next(0, 50);
        var maxDelay = _consensusConfig.VoteTimeout.TotalMilliseconds / 2;

        var delay = Math.Min(baseDelay + jitter, maxDelay);
        return TimeSpan.FromMilliseconds(delay);
    }

}
