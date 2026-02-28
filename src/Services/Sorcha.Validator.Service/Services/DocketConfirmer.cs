// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Sorcha.Cryptography.Utilities;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Validates and signs dockets received from other validators (confirmer role).
/// Implements full validation pipeline including Merkle root verification,
/// initiator signature verification, and transaction validation.
/// </summary>
public class DocketConfirmer : IDocketConfirmer
{
    private readonly DocketConfirmerConfiguration _config;
    private readonly IValidationEngine _validationEngine;
    private readonly IWalletIntegrationService _walletService;
    private readonly IValidatorRegistry _validatorRegistry;
    private readonly ILeaderElectionService _leaderElection;
    private readonly IBadActorDetector _badActorDetector;
    private readonly MerkleTree _merkleTree;
    private readonly DocketHasher _docketHasher;
    private readonly ILogger<DocketConfirmer> _logger;

    // Statistics
    private long _totalConfirmations;
    private long _successfulConfirmations;
    private long _rejectedConfirmations;
    private readonly ConcurrentDictionary<DocketRejectionReason, long> _rejectionsByReason = new();

    public DocketConfirmer(
        IOptions<DocketConfirmerConfiguration> config,
        IValidationEngine validationEngine,
        IWalletIntegrationService walletService,
        IValidatorRegistry validatorRegistry,
        ILeaderElectionService leaderElection,
        IBadActorDetector badActorDetector,
        MerkleTree merkleTree,
        DocketHasher docketHasher,
        ILogger<DocketConfirmer> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _validationEngine = validationEngine ?? throw new ArgumentNullException(nameof(validationEngine));
        _walletService = walletService ?? throw new ArgumentNullException(nameof(walletService));
        _validatorRegistry = validatorRegistry ?? throw new ArgumentNullException(nameof(validatorRegistry));
        _leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
        _badActorDetector = badActorDetector ?? throw new ArgumentNullException(nameof(badActorDetector));
        _merkleTree = merkleTree ?? throw new ArgumentNullException(nameof(merkleTree));
        _docketHasher = docketHasher ?? throw new ArgumentNullException(nameof(docketHasher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<DocketConfirmationResult> ConfirmDocketAsync(
        Docket docket,
        Signature initiatorSignature,
        long term,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);
        ArgumentNullException.ThrowIfNull(initiatorSignature);

        Interlocked.Increment(ref _totalConfirmations);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Confirming docket {DocketId} from validator {ProposerId} for register {RegisterId} (term: {Term})",
            docket.DocketId, docket.ProposerValidatorId, docket.RegisterId, term);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_config.ValidationTimeout);

            // 1. Validate docket structure
            var structureResult = ValidateDocketStructure(docket);
            if (structureResult != null)
            {
                return RecordRejection(structureResult, docket, stopwatch.Elapsed);
            }

            // 2. Validate term
            var termResult = ValidateTerm(term, docket);
            if (termResult != null)
            {
                return RecordRejection(termResult, docket, stopwatch.Elapsed);
            }

            // 3. Verify initiator is authorized
            var authResult = await VerifyInitiatorAuthorizationAsync(
                docket.RegisterId, docket.ProposerValidatorId, term, timeoutCts.Token);
            if (authResult != null)
            {
                return RecordRejection(authResult, docket, stopwatch.Elapsed);
            }

            // 4. Verify initiator signature
            if (_config.VerifyInitiatorSignature)
            {
                var sigResult = await VerifyInitiatorSignatureAsync(
                    docket, initiatorSignature, timeoutCts.Token);
                if (sigResult != null)
                {
                    return RecordRejection(sigResult, docket, stopwatch.Elapsed);
                }
            }

            // 5. Verify docket hash
            if (_config.VerifyDocketHash)
            {
                var hashResult = VerifyDocketHash(docket);
                if (hashResult != null)
                {
                    return RecordRejection(hashResult, docket, stopwatch.Elapsed);
                }
            }

            // 6. Verify Merkle root
            if (_config.VerifyMerkleRoot)
            {
                var merkleResult = VerifyMerkleRoot(docket);
                if (merkleResult != null)
                {
                    return RecordRejection(merkleResult, docket, stopwatch.Elapsed);
                }
            }

            // 7. Validate all transactions
            var txValidationResult = await ValidateAllTransactionsAsync(docket, timeoutCts.Token);
            if (txValidationResult != null)
            {
                return RecordRejection(txValidationResult, docket, stopwatch.Elapsed);
            }

            // All validations passed - sign the docket
            var signature = await SignDocketAsync(docket, timeoutCts.Token);

            Interlocked.Increment(ref _successfulConfirmations);

            _logger.LogInformation(
                "Confirmed docket {DocketId} with {TransactionCount} transactions in {Duration}ms",
                docket.DocketId, docket.TransactionCount, stopwatch.ElapsedMilliseconds);

            return DocketConfirmationResult.CreateConfirmed(
                signature,
                stopwatch.Elapsed,
                docket.TransactionCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Docket confirmation cancelled for {DocketId}", docket.DocketId);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Docket confirmation timed out for {DocketId}", docket.DocketId);
            return RecordRejection(
                CreateRejection(DocketRejectionReason.Timeout, "Validation timed out"),
                docket,
                stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming docket {DocketId}", docket.DocketId);
            return RecordRejection(
                CreateRejection(DocketRejectionReason.InternalError, ex.Message),
                docket,
                stopwatch.Elapsed);
        }
    }

    /// <inheritdoc/>
    public async Task<TransactionValidationResult> ValidateTransactionAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await _validationEngine.ValidateTransactionAsync(transaction, ct);

            return new TransactionValidationResult
            {
                IsValid = result.IsValid,
                TransactionId = transaction.TransactionId,
                Errors = result.Errors.Select(e => e.Message).ToList(),
                ValidationDuration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating transaction {TransactionId}", transaction.TransactionId);
            return new TransactionValidationResult
            {
                IsValid = false,
                TransactionId = transaction.TransactionId,
                Errors = [$"Validation error: {ex.Message}"],
                ValidationDuration = stopwatch.Elapsed
            };
        }
    }


    private DocketConfirmationResult? ValidateDocketStructure(Docket docket)
    {
        if (string.IsNullOrWhiteSpace(docket.DocketId))
        {
            return CreateRejection(DocketRejectionReason.InvalidDocketStructure, "Docket ID is required");
        }

        if (string.IsNullOrWhiteSpace(docket.RegisterId))
        {
            return CreateRejection(DocketRejectionReason.InvalidDocketStructure, "Register ID is required");
        }

        if (string.IsNullOrWhiteSpace(docket.ProposerValidatorId))
        {
            return CreateRejection(DocketRejectionReason.InvalidDocketStructure, "Proposer validator ID is required");
        }

        if (string.IsNullOrWhiteSpace(docket.DocketHash))
        {
            return CreateRejection(DocketRejectionReason.InvalidDocketStructure, "Docket hash is required");
        }

        if (string.IsNullOrWhiteSpace(docket.MerkleRoot))
        {
            return CreateRejection(DocketRejectionReason.InvalidDocketStructure, "Merkle root is required");
        }

        if (docket.DocketNumber < 0)
        {
            return CreateRejection(DocketRejectionReason.InvalidSequenceNumber, "Docket number must be non-negative");
        }

        if (docket.DocketNumber > 0 && string.IsNullOrWhiteSpace(docket.PreviousHash))
        {
            return CreateRejection(DocketRejectionReason.InvalidDocketStructure,
                "Non-genesis docket must have previous hash");
        }

        // Validate timestamp
        var now = DateTimeOffset.UtcNow;
        if (docket.CreatedAt > now.Add(_config.MaxClockSkew))
        {
            return CreateRejection(DocketRejectionReason.InvalidDocketStructure,
                "Docket timestamp is in the future");
        }

        if (docket.CreatedAt < now.Subtract(_config.MaxDocketAge))
        {
            return CreateRejection(DocketRejectionReason.InvalidDocketStructure,
                "Docket is too old");
        }

        return null; // Valid structure
    }

    private DocketConfirmationResult? ValidateTerm(long term, Docket docket)
    {
        var currentTerm = _leaderElection.CurrentTerm;

        // Accept current term or one term behind (in case of network delays)
        if (term < currentTerm - 1)
        {
            _logger.LogWarning(
                "Rejecting docket {DocketId} with stale term {Term} (current: {CurrentTerm})",
                docket.DocketId, term, currentTerm);
            return CreateRejection(DocketRejectionReason.InvalidTerm,
                $"Term {term} is too old (current: {currentTerm})");
        }

        // Future terms are suspicious
        if (term > currentTerm + 1)
        {
            _logger.LogWarning(
                "Rejecting docket {DocketId} with future term {Term} (current: {CurrentTerm})",
                docket.DocketId, term, currentTerm);
            return CreateRejection(DocketRejectionReason.InvalidTerm,
                $"Term {term} is in the future (current: {currentTerm})");
        }

        return null;
    }

    private async Task<DocketConfirmationResult?> VerifyInitiatorAuthorizationAsync(
        string registerId,
        string initiatorId,
        long term,
        CancellationToken ct)
    {
        // Check if initiator is a registered validator
        var isRegistered = await _validatorRegistry.IsRegisteredAsync(registerId, initiatorId, ct);
        if (!isRegistered)
        {
            _badActorDetector.LogLeaderImpersonation(
                registerId,
                initiatorId,
                _leaderElection.CurrentLeaderId ?? "unknown",
                term);

            return CreateRejection(DocketRejectionReason.UnauthorizedInitiator,
                $"Validator {initiatorId} is not registered for register {registerId}");
        }

        // Check if initiator was the leader for the given term
        var expectedLeader = _leaderElection.GetLeaderForTerm(term);
        if (expectedLeader != null && !string.Equals(expectedLeader, initiatorId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Docket proposed by {ProposerId} but expected leader is {ExpectedLeader} for term {Term}",
                initiatorId, expectedLeader, term);

            _badActorDetector.LogLeaderImpersonation(registerId, initiatorId, expectedLeader, term);

            return CreateRejection(DocketRejectionReason.UnauthorizedInitiator,
                $"Validator {initiatorId} was not the leader for term {term}");
        }

        return null;
    }

    private async Task<DocketConfirmationResult?> VerifyInitiatorSignatureAsync(
        Docket docket,
        Signature initiatorSignature,
        CancellationToken ct)
    {
        try
        {
            // Get the docket hash that should have been signed
            var docketHashBytes = Encoding.UTF8.GetBytes(docket.DocketHash);

            // Parse algorithm
            if (!Enum.TryParse<WalletAlgorithm>(initiatorSignature.Algorithm, true, out var algorithm))
            {
                return CreateRejection(DocketRejectionReason.InvalidInitiatorSignature,
                    $"Unknown signature algorithm: {initiatorSignature.Algorithm}");
            }

            // Verify signature
            var isValid = await _walletService.VerifySignatureAsync(
                initiatorSignature.SignatureValue,
                docketHashBytes,
                initiatorSignature.PublicKey,
                algorithm,
                ct);

            if (!isValid)
            {
                _logger.LogWarning(
                    "Invalid initiator signature on docket {DocketId} from {ProposerId}",
                    docket.DocketId, docket.ProposerValidatorId);

                _badActorDetector.LogDocketRejection(
                    docket.RegisterId,
                    docket.ProposerValidatorId,
                    docket.DocketId,
                    DocketRejectionReason.InvalidInitiatorSignature,
                    "Signature verification failed");

                return CreateRejection(DocketRejectionReason.InvalidInitiatorSignature,
                    "Initiator signature verification failed");
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying initiator signature for docket {DocketId}", docket.DocketId);
            return CreateRejection(DocketRejectionReason.InvalidInitiatorSignature,
                $"Signature verification error: {ex.Message}");
        }
    }

    private DocketConfirmationResult? VerifyDocketHash(Docket docket)
    {
        var computedHash = _docketHasher.ComputeDocketHash(
            docket.RegisterId,
            docket.DocketNumber,
            docket.PreviousHash,
            docket.MerkleRoot,
            docket.CreatedAt);

        if (!string.Equals(computedHash, docket.DocketHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Docket hash mismatch for {DocketId}. Expected: {Expected}, Got: {Actual}",
                docket.DocketId, computedHash, docket.DocketHash);

            _badActorDetector.LogDocketRejection(
                docket.RegisterId,
                docket.ProposerValidatorId,
                docket.DocketId,
                DocketRejectionReason.InvalidDocketHash,
                $"Expected: {computedHash}, Got: {docket.DocketHash}");

            return CreateRejection(DocketRejectionReason.InvalidDocketHash,
                "Docket hash does not match computed value");
        }

        return null;
    }

    private DocketConfirmationResult? VerifyMerkleRoot(Docket docket)
    {
        // Compute Merkle root from transactions
        string computedMerkleRoot;

        if (docket.Transactions == null || docket.Transactions.Count == 0)
        {
            computedMerkleRoot = _merkleTree.ComputeMerkleRoot(new List<string>());
        }
        else
        {
            var txHashes = docket.Transactions.Select(tx =>
                _docketHasher.ComputeTransactionHash(tx.TransactionId, tx.PayloadHash, tx.CreatedAt)
            ).ToList();

            computedMerkleRoot = _merkleTree.ComputeMerkleRoot(txHashes);
        }

        if (!string.Equals(computedMerkleRoot, docket.MerkleRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Merkle root mismatch for docket {DocketId}. Expected: {Expected}, Got: {Actual}",
                docket.DocketId, computedMerkleRoot, docket.MerkleRoot);

            _badActorDetector.LogDocketRejection(
                docket.RegisterId,
                docket.ProposerValidatorId,
                docket.DocketId,
                DocketRejectionReason.InvalidMerkleRoot,
                $"Expected: {computedMerkleRoot}, Got: {docket.MerkleRoot}");

            return CreateRejection(DocketRejectionReason.InvalidMerkleRoot,
                "Merkle root does not match computed value");
        }

        return null;
    }

    private async Task<DocketConfirmationResult?> ValidateAllTransactionsAsync(
        Docket docket,
        CancellationToken ct)
    {
        if (docket.Transactions == null || docket.Transactions.Count == 0)
        {
            _logger.LogDebug("Docket {DocketId} has no transactions to validate", docket.DocketId);
            return null;
        }

        _logger.LogDebug(
            "Validating {Count} transactions in docket {DocketId}",
            docket.Transactions.Count, docket.DocketId);

        IReadOnlyList<ValidationEngineResult> results;

        if (_config.EnableParallelValidation)
        {
            // Parallel validation with semaphore for concurrency control
            using var semaphore = new SemaphoreSlim(_config.MaxConcurrentValidations);
            var tasks = docket.Transactions.Select(async tx =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    return await _validationEngine.ValidateTransactionAsync(tx, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            results = await Task.WhenAll(tasks);
        }
        else
        {
            // Sequential validation
            var resultList = new List<ValidationEngineResult>();
            foreach (var tx in docket.Transactions)
            {
                var result = await _validationEngine.ValidateTransactionAsync(tx, ct);
                resultList.Add(result);

                // Fail fast on invalid transaction
                if (!result.IsValid)
                {
                    break;
                }
            }
            results = resultList;
        }

        // Check for any failures
        var failures = results.Where(r => !r.IsValid).ToList();
        if (failures.Count > 0)
        {
            var firstFailure = failures[0];
            var errorMessages = firstFailure.Errors.Select(e => e.Message).ToList();
            var errorDetails = string.Join("; ", errorMessages);

            _logger.LogWarning(
                "Transaction validation failed for docket {DocketId}. Failed transactions: {FailedCount}/{TotalCount}. First error: {Error}",
                docket.DocketId, failures.Count, docket.Transactions.Count, errorDetails);

            // Log bad actor incident for each failed transaction
            foreach (var failure in failures)
            {
                var tx = docket.Transactions.FirstOrDefault(t => t.TransactionId == failure.TransactionId);
                if (tx != null)
                {
                    var firstError = failure.Errors.FirstOrDefault();
                    _badActorDetector.LogTransactionValidationFailure(
                        docket.RegisterId,
                        tx.Signatures?.FirstOrDefault()?.SignedBy ?? "unknown",
                        tx.TransactionId,
                        firstError?.Category.ToString() ?? "Unknown",
                        firstError?.Message);
                }
            }

            // Also log docket rejection
            _badActorDetector.LogDocketRejection(
                docket.RegisterId,
                docket.ProposerValidatorId,
                docket.DocketId,
                DocketRejectionReason.InvalidTransaction,
                $"{failures.Count} transaction(s) failed validation");

            return CreateRejection(
                DocketRejectionReason.InvalidTransaction,
                $"Transaction {firstFailure.TransactionId} failed validation: {errorDetails}");
        }

        return null;
    }

    private async Task<Signature> SignDocketAsync(Docket docket, CancellationToken ct)
    {
        var docketHashBytes = Encoding.UTF8.GetBytes(docket.DocketHash);
        return await _walletService.SignDocketAsync(docketHashBytes, ct);
    }



    private static DocketConfirmationResult CreateRejection(
        DocketRejectionReason reason,
        string? details)
    {
        return DocketConfirmationResult.CreateRejected(reason, details, TimeSpan.Zero);
    }

    private DocketConfirmationResult RecordRejection(
        DocketConfirmationResult result,
        Docket docket,
        TimeSpan duration)
    {
        Interlocked.Increment(ref _rejectedConfirmations);

        if (result.RejectionReason.HasValue)
        {
            _rejectionsByReason.AddOrUpdate(result.RejectionReason.Value, 1, (_, count) => count + 1);
        }

        if (_config.LogDetailedRejections)
        {
            _logger.LogWarning(
                "Rejected docket {DocketId} from {ProposerId}: {Reason} - {Details}",
                docket.DocketId,
                docket.ProposerValidatorId,
                result.RejectionReason,
                result.RejectionDetails);
        }

        // Return with actual duration
        return DocketConfirmationResult.CreateRejected(
            result.RejectionReason ?? DocketRejectionReason.InternalError,
            result.RejectionDetails,
            duration);
    }


    /// <summary>
    /// Get confirmer statistics
    /// </summary>
    public DocketConfirmerStats GetStats()
    {
        return new DocketConfirmerStats
        {
            TotalConfirmations = Interlocked.Read(ref _totalConfirmations),
            SuccessfulConfirmations = Interlocked.Read(ref _successfulConfirmations),
            RejectedConfirmations = Interlocked.Read(ref _rejectedConfirmations),
            RejectionsByReason = new Dictionary<DocketRejectionReason, long>(_rejectionsByReason)
        };
    }
}

/// <summary>
/// Statistics for the docket confirmer
/// </summary>
public record DocketConfirmerStats
{
    /// <summary>Total confirmation attempts</summary>
    public long TotalConfirmations { get; init; }

    /// <summary>Successful confirmations</summary>
    public long SuccessfulConfirmations { get; init; }

    /// <summary>Rejected confirmations</summary>
    public long RejectedConfirmations { get; init; }

    /// <summary>Rejections by reason</summary>
    public IReadOnlyDictionary<DocketRejectionReason, long> RejectionsByReason { get; init; }
        = new Dictionary<DocketRejectionReason, long>();

    /// <summary>Success rate (0-1)</summary>
    public double SuccessRate => TotalConfirmations > 0
        ? (double)SuccessfulConfirmations / TotalConfirmations
        : 0;
}
