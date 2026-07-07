// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Background service that continuously polls transactions from the unverified pool,
/// validates them, and moves valid transactions to the verified queue.
/// </summary>
public class ValidationEngineService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITransactionPoolPoller _poolPoller;
    private readonly IVerifiedTransactionQueue _verifiedQueue;
    private readonly IRegisterMonitoringRegistry _monitoringRegistry;
    private readonly ValidationEngineConfiguration _config;
    private readonly ValidatorMempoolMetrics _metrics;
    private readonly ILogger<ValidationEngineService> _logger;

    // Track active registers being validated, keyed by when processing STARTED (issue #814).
    // The value is the start time so a flag that was never released (a processing await that hung
    // and outlived its timeout, e.g. a call that ignores cancellation on a stale connection) can be
    // reclaimed instead of skipping the register forever. A HashSet with a finally-only release was
    // the original wedge: one hung await stranded the flag and every later batch skipped the register.
    private readonly Dictionary<string, DateTimeOffset> _activeRegisters = new(StringComparer.Ordinal);
    private readonly object _registersLock = new();

    // A register whose processing flag has been held longer than this is treated as STUCK and
    // reclaimed. Comfortably longer than the per-register ValidationTimeout so a genuinely-in-flight
    // register (bounded by that timeout) is never falsely reclaimed.
    private TimeSpan StuckReclaimAfter => TimeSpan.FromTicks(Math.Max(_config.ValidationTimeout.Ticks * 3, TimeSpan.FromSeconds(90).Ticks));

    // Testable clock seam (avoids taking a new IClock dependency across every call site).
    internal Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;

    public ValidationEngineService(
        IServiceScopeFactory scopeFactory,
        ITransactionPoolPoller poolPoller,
        IVerifiedTransactionQueue verifiedQueue,
        IRegisterMonitoringRegistry monitoringRegistry,
        IOptions<ValidationEngineConfiguration> config,
        ValidatorMempoolMetrics metrics,
        ILogger<ValidationEngineService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _poolPoller = poolPoller ?? throw new ArgumentNullException(nameof(poolPoller));
        _verifiedQueue = verifiedQueue ?? throw new ArgumentNullException(nameof(verifiedQueue));
        _monitoringRegistry = monitoringRegistry ?? throw new ArgumentNullException(nameof(monitoringRegistry));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Validation Engine Service starting with batch size {BatchSize}, interval {Interval}",
            _config.BatchSize, _config.ValidationInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessValidationBatchAsync(stoppingToken);
                await Task.Delay(_config.ValidationInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in validation engine service loop");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        _logger.LogInformation("Validation Engine Service stopping");
    }

    private async Task ProcessValidationBatchAsync(CancellationToken ct)
    {
        // Discover active registers from the monitoring registry
        var monitoredRegisters = _monitoringRegistry.GetAll().ToList();

        if (monitoredRegisters.Count == 0)
        {
            _logger.LogTrace("No monitored registers — skipping validation batch");
            return;
        }

        _logger.LogTrace("Processing validation batch for {Count} monitored registers", monitoredRegisters.Count);

        foreach (var registerId in monitoredRegisters)
        {
            try
            {
                await ProcessRegisterAsync(registerId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing register {RegisterId} during validation batch", registerId);
            }
        }
    }

    /// <summary>
    /// Process pending transactions for a specific register
    /// </summary>
    /// <param name="registerId">Register ID to process</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of transactions validated</returns>
    public async Task<int> ProcessRegisterAsync(string registerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        // Admission guard (issue #814): skip a register only while it is GENUINELY in flight. A flag
        // held past StuckReclaimAfter means a prior cycle never released it (a hung await that
        // outlived its timeout / ignored cancellation) — reclaim it so validation resumes rather than
        // skipping this register forever. The original HashSet + finally-only release was the wedge:
        // one hung await stranded the flag and every later batch skipped the register permanently.
        lock (_registersLock)
        {
            if (_activeRegisters.TryGetValue(registerId, out var startedAt))
            {
                var heldFor = Now() - startedAt;
                if (heldFor < StuckReclaimAfter)
                {
                    _logger.LogDebug("Already processing register {RegisterId}, skipping", registerId);
                    return 0;
                }

                _logger.LogWarning(
                    "Register {RegisterId} validation slot held {HeldSeconds:F0}s (> {ThresholdSeconds:F0}s) — reclaiming a stuck slot so validation can resume (issue #814)",
                    registerId, heldFor.TotalSeconds, StuckReclaimAfter.TotalSeconds);
                _metrics.RecordValidationSlotReclaimed(registerId);
            }
            _activeRegisters[registerId] = Now();
        }

        // Bound the whole cycle: after a long idle a stale Redis/gRPC connection must make the awaits
        // THROW (caught below → loop continues, finally releases the flag), never hang forever and
        // wedge the validation loop. This is the core #814 fix.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_config.ValidationTimeout);
        var opCt = timeoutCts.Token;

        try
        {
            // Poll transactions from unverified pool
            var transactions = await _poolPoller.PollTransactionsAsync(
                registerId, _config.BatchSize, opCt);

            if (transactions.Count == 0)
            {
                return 0;
            }

            _logger.LogDebug(
                "Processing {Count} transactions for register {RegisterId}",
                transactions.Count, registerId);

            // Validate the batch (create scope for scoped IValidationEngine)
            using var scope = _scopeFactory.CreateScope();
            var validationEngine = scope.ServiceProvider.GetRequiredService<IValidationEngine>();
            var results = await validationEngine.ValidateBatchAsync(transactions, opCt);

            var validCount = 0;
            var invalidCount = 0;
            var transactionsToReturn = new List<Models.Transaction>();

            foreach (var result in results)
            {
                var tx = transactions.First(t => t.TransactionId == result.TransactionId);

                if (result.IsValid)
                {
                    // Enqueue to verified queue for docket building
                    var priority = GetTransactionPriority(tx);
                    if (_verifiedQueue.Enqueue(registerId, tx, priority))
                    {
                        validCount++;
                        _logger.LogDebug(
                            "Transaction {TransactionId} validated and queued",
                            tx.TransactionId);
                    }
                    else
                    {
                        // Queue full - return to pool for retry
                        transactionsToReturn.Add(tx);
                        _logger.LogWarning(
                            "Verified queue full, returning transaction {TransactionId} to pool",
                            tx.TransactionId);
                    }
                }
                else
                {
                    invalidCount++;

                    // Log validation failures
                    var errorSummary = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Message}"));
                    _logger.LogWarning(
                        "Transaction {TransactionId} failed validation: {Errors}",
                        tx.TransactionId, errorSummary);

                    // Don't return invalid transactions to pool - they're rejected
                    // In production, might want to store rejection reason
                }
            }

            // Return any transactions that couldn't be queued
            if (transactionsToReturn.Count > 0)
            {
                await _poolPoller.ReturnTransactionsAsync(registerId, transactionsToReturn, opCt);
            }

            _logger.LogInformation(
                "Register {RegisterId}: validated {Valid} transactions, rejected {Invalid}",
                registerId, validCount, invalidCount);

            return validCount;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // The processing cycle exceeded ValidationTimeout (a stale connection after idle, most
            // likely). Abort THIS cycle and return — the finally releases the slot and the next batch
            // retries. Pending transactions stay in the unverified pool. Without this, the await would
            // hang forever and wedge the entire validation loop (issue #814).
            _logger.LogError(
                "Register {RegisterId} validation cycle exceeded {TimeoutSeconds:F0}s and was aborted to keep the validation loop alive (issue #814); pending transactions remain in the pool for the next cycle",
                registerId, _config.ValidationTimeout.TotalSeconds);
            _metrics.RecordValidationCycleTimeout(registerId);
            return 0;
        }
        finally
        {
            lock (_registersLock)
            {
                _activeRegisters.Remove(registerId);
            }
        }
    }

    /// <summary>
    /// Validate a single transaction on demand (not from pool)
    /// </summary>
    public async Task<ValidationEngineResult> ValidateOnDemandAsync(
        Models.Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var scope = _scopeFactory.CreateScope();
        var validationEngine = scope.ServiceProvider.GetRequiredService<IValidationEngine>();
        var result = await validationEngine.ValidateTransactionAsync(transaction, ct);

        if (result.IsValid)
        {
            // Optionally enqueue to verified queue
            var priority = GetTransactionPriority(transaction);
            _verifiedQueue.Enqueue(transaction.RegisterId, transaction, priority);
        }

        return result;
    }

    private static int GetTransactionPriority(Models.Transaction transaction)
    {
        // Priority based on transaction priority enum
        return transaction.Priority switch
        {
            Models.TransactionPriority.Low => -10,
            Models.TransactionPriority.Normal => 0,
            Models.TransactionPriority.High => 10,
            _ => 0
        };
    }
}
