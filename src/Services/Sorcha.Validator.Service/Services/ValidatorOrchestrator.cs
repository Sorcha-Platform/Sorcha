// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Peer;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Orchestrates the complete validation pipeline for registers
/// </summary>
public class ValidatorOrchestrator : IValidatorOrchestrator
{
    private readonly IVerifiedTransactionQueue _verifiedQueue;
    private readonly IDocketBuilder _docketBuilder;
    private readonly IConsensusEngine _consensusEngine;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IPeerServiceClient _peerClient;
    private readonly ILogger<ValidatorOrchestrator> _logger;

    // Track active validators per register
    private readonly ConcurrentDictionary<string, ValidatorState> _activeValidators = new();

    public ValidatorOrchestrator(
        IVerifiedTransactionQueue verifiedQueue,
        IDocketBuilder docketBuilder,
        IConsensusEngine consensusEngine,
        IRegisterServiceClient registerClient,
        IPeerServiceClient peerClient,
        ILogger<ValidatorOrchestrator> logger)
    {
        _verifiedQueue = verifiedQueue ?? throw new ArgumentNullException(nameof(verifiedQueue));
        _docketBuilder = docketBuilder ?? throw new ArgumentNullException(nameof(docketBuilder));
        _consensusEngine = consensusEngine ?? throw new ArgumentNullException(nameof(consensusEngine));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _peerClient = peerClient ?? throw new ArgumentNullException(nameof(peerClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<bool> StartValidatorAsync(string registerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting validator for register {RegisterId}", registerId);

        try
        {
            var state = _activeValidators.GetOrAdd(registerId, _ => new ValidatorState
            {
                RegisterId = registerId,
                IsActive = true,
                StartedAt = DateTimeOffset.UtcNow
            });

            if (state.IsActive)
            {
                _logger.LogWarning("Validator for register {RegisterId} is already active", registerId);
                return true;
            }

            state.IsActive = true;
            state.StartedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation("Validator for register {RegisterId} started successfully", registerId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start validator for register {RegisterId}", registerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> StopValidatorAsync(
        string registerId,
        bool persistMemPool = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Stopping validator for register {RegisterId} (persistMemPool: {PersistMemPool})",
            registerId, persistMemPool);

        try
        {
            if (!_activeValidators.TryGetValue(registerId, out var state))
            {
                _logger.LogWarning("No active validator found for register {RegisterId}", registerId);
                return false;
            }

            state.IsActive = false;

            if (persistMemPool)
            {
                // MemPoolManager is already Redis-backed, so transactions persist across restarts.
                // Log the current pool state for operational awareness.
                var poolCount = _verifiedQueue.GetCount(registerId);
                _logger.LogInformation("Memory pool for register {RegisterId} has {Count} transactions (Redis-persisted)",
                    registerId, poolCount);
            }

            _logger.LogInformation("Validator for register {RegisterId} stopped successfully", registerId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop validator for register {RegisterId}", registerId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<ValidatorStatus?> GetValidatorStatusAsync(string registerId)
    {
        if (!_activeValidators.TryGetValue(registerId, out var state))
        {
            return null;
        }

        var transactionCount = _verifiedQueue.GetCount(registerId);

        return new ValidatorStatus
        {
            RegisterId = registerId,
            IsActive = state.IsActive,
            TransactionsInMemPool = transactionCount,
            DocketsProposed = state.DocketsProposed,
            DocketsConfirmed = state.DocketsConfirmed,
            DocketsRejected = state.DocketsRejected,
            StartedAt = state.StartedAt,
            LastDocketBuildAt = state.LastDocketBuildAt
        };
    }

    /// <inheritdoc/>
    public async Task<PipelineResult?> ProcessValidationPipelineAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("Processing validation pipeline for register {RegisterId}", registerId);

        try
        {
            // Get validator state
            if (!_activeValidators.TryGetValue(registerId, out var state))
            {
                _logger.LogWarning("No active validator found for register {RegisterId}", registerId);
                return null;
            }

            if (!state.IsActive)
            {
                _logger.LogDebug("Validator for register {RegisterId} is not active", registerId);
                return null;
            }

            // Stage 1: Check if docket build should trigger
            var lastBuildTime = state.LastDocketBuildAt ?? DateTimeOffset.MinValue;
            var shouldBuild = await _docketBuilder.ShouldBuildDocketAsync(
                registerId,
                lastBuildTime,
                cancellationToken);

            if (!shouldBuild)
            {
                _logger.LogDebug("Docket build triggers not met for register {RegisterId}", registerId);
                return null;
            }

            // Stage 2: Build docket from memory pool
            _logger.LogInformation("Building docket for register {RegisterId}", registerId);
            var docket = await _docketBuilder.BuildDocketAsync(registerId, false, cancellationToken);

            if (docket == null)
            {
                _logger.LogWarning("Failed to build docket for register {RegisterId}", registerId);
                return null;
            }

            state.DocketsProposed++;
            state.LastDocketBuildAt = DateTimeOffset.UtcNow;

            // Stage 3: Achieve consensus
            _logger.LogInformation(
                "Achieving consensus for docket {DocketNumber} on register {RegisterId}",
                docket.DocketNumber, registerId);

            var consensusResult = await _consensusEngine.AchieveConsensusAsync(docket, cancellationToken);

            if (!consensusResult.Achieved)
            {
                _logger.LogWarning(
                    "Consensus failed for docket {DocketNumber}: {Reason}",
                    docket.DocketNumber, consensusResult.FailureReason);

                state.DocketsRejected++;

                // Return transactions to memory pool
                await ReturnTransactionsToMemPoolAsync(docket, cancellationToken);

                stopwatch.Stop();
                return new PipelineResult
                {
                    Docket = docket,
                    ConsensusAchieved = false,
                    WrittenToRegister = false,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = consensusResult.FailureReason
                };
            }

            state.DocketsConfirmed++;

            // Update docket with consensus information
            docket.Status = DocketStatus.Confirmed;
            docket.ConsensusAchievedAt = DateTimeOffset.UtcNow;
            docket.Votes.Clear();
            docket.Votes.AddRange(consensusResult.Votes);

            // Stage 4: Write confirmed docket to Register Service
            _logger.LogInformation(
                "Writing confirmed docket {DocketNumber} to Register Service",
                docket.DocketNumber);

            var docketModel = DocketSerializer.ToRegisterModel(docket);
            var written = await _registerClient.WriteDocketAsync(docketModel, cancellationToken);

            if (!written)
            {
                _logger.LogError(
                    "Failed to write docket {DocketNumber} to Register Service",
                    docket.DocketNumber);

                stopwatch.Stop();
                return new PipelineResult
                {
                    Docket = docket,
                    ConsensusAchieved = true,
                    WrittenToRegister = false,
                    Duration = stopwatch.Elapsed,
                    ErrorMessage = "Failed to write to Register Service"
                };
            }

            // Stage 5: Remove processed transactions from memory pool
            _logger.LogDebug("Removing {Count} processed transactions from memory pool", docket.Transactions.Count);
            await RemoveTransactionsFromMemPoolAsync(docket, cancellationToken);

            // Stage 6: Broadcast confirmed docket to peer network
            _logger.LogInformation("Broadcasting confirmed docket {DocketNumber} to peer network", docket.DocketNumber);
            var confirmedDocketData = DocketSerializer.SerializeToBytes(docket);
            await _peerClient.BroadcastConfirmedDocketAsync(
                registerId,
                docket.DocketId,
                confirmedDocketData,
                cancellationToken);

            stopwatch.Stop();

            _logger.LogInformation(
                "Validation pipeline completed for docket {DocketNumber} in {Duration}ms",
                docket.DocketNumber, stopwatch.ElapsedMilliseconds);

            return new PipelineResult
            {
                Docket = docket,
                ConsensusAchieved = true,
                WrittenToRegister = true,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing validation pipeline for register {RegisterId}", registerId);

            return new PipelineResult
            {
                Docket = new Docket
                {
                    DocketId = "error",
                    RegisterId = registerId,
                    DocketNumber = -1,
                    DocketHash = "error",
                    CreatedAt = DateTimeOffset.UtcNow,
                    Transactions = new List<Transaction>(),
                    Status = DocketStatus.Rejected,
                    ProposerValidatorId = "unknown",
                    ProposerSignature = new Signature
                    {
                        PublicKey = System.Text.Encoding.UTF8.GetBytes("error"),
                        SignatureValue = System.Text.Encoding.UTF8.GetBytes("error"),
                        Algorithm = "error",
                        SignedAt = DateTimeOffset.UtcNow
                    },
                    MerkleRoot = "error"
                },
                ConsensusAchieved = false,
                WrittenToRegister = false,
                Duration = stopwatch.Elapsed,
                ErrorMessage = $"Pipeline error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Returns transactions to memory pool when consensus fails
    /// </summary>
    private async Task ReturnTransactionsToMemPoolAsync(Docket docket, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var transaction in docket.Transactions)
            {
                transaction.RetryCount++;
                // Transactions remain in memory pool - just increment retry count
            }

            _logger.LogInformation(
                "Returned {Count} transactions to memory pool for docket {DocketNumber}",
                docket.Transactions.Count, docket.DocketNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error returning transactions to memory pool");
        }
    }

    /// <summary>
    /// Removes confirmed transactions from verified queue
    /// </summary>
    private Task RemoveTransactionsFromMemPoolAsync(Docket docket, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var transaction in docket.Transactions)
            {
                _verifiedQueue.Remove(
                    docket.RegisterId,
                    transaction.TransactionId);
            }

            _logger.LogDebug(
                "Removed {Count} transactions from verified queue for docket {DocketNumber}",
                docket.Transactions.Count, docket.DocketNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing transactions from verified queue");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Internal state tracking for a validator instance
    /// </summary>
    private class ValidatorState
    {
        public required string RegisterId { get; init; }
        public bool IsActive { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public DateTimeOffset? LastDocketBuildAt { get; set; }
        public long DocketsProposed { get; set; }
        public long DocketsConfirmed { get; set; }
        public long DocketsRejected { get; set; }
    }
}
