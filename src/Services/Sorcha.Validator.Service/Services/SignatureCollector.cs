// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using Grpc.Net.Client;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Collects signatures from confirming validators for proposed dockets.
/// Implements parallel signature collection with timeout and threshold checking.
/// </summary>
public class SignatureCollector : ISignatureCollector
{
    private readonly ValidatorConfiguration _validatorConfig;
    private readonly ConsensusConfiguration _consensusConfig;
    private readonly ILeaderElectionService _leaderElection;
    private readonly ILogger<SignatureCollector> _logger;

    // Storage for incoming signatures by docket (registerId:docketId -> signatures)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConsensusVote>> _incomingSignatures = new();

    // Storage for local votes by docket (registerId:docketId -> vote)
    private readonly ConcurrentDictionary<string, ConsensusVote> _localVotes = new();

    // Statistics
    private long _totalCollections;
    private long _successfulCollections;
    private long _failedCollections;
    private long _incomingSignaturesReceived;

    public SignatureCollector(
        IOptions<ValidatorConfiguration> validatorConfig,
        IOptions<ConsensusConfiguration> consensusConfig,
        ILeaderElectionService leaderElection,
        ILogger<SignatureCollector> logger)
    {
        _validatorConfig = validatorConfig?.Value ?? throw new ArgumentNullException(nameof(validatorConfig));
        _consensusConfig = consensusConfig?.Value ?? throw new ArgumentNullException(nameof(consensusConfig));
        _leaderElection = leaderElection ?? throw new ArgumentNullException(nameof(leaderElection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<SignatureCollectionResult> CollectSignaturesAsync(
        Docket docket,
        ConsensusConfig config,
        IReadOnlyList<ValidatorInfo> validators,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(docket);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(validators);

        Interlocked.Increment(ref _totalCollections);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Collecting signatures for docket {DocketId} from {ValidatorCount} validators (threshold: {Min}-{Max})",
            docket.DocketId, validators.Count, config.SignatureThresholdMin, config.SignatureThresholdMax);

        var signatures = new ConcurrentBag<CollectedSignature>();
        var rejectionDetails = new ConcurrentDictionary<string, string>();
        var nonResponders = new ConcurrentBag<string>();
        var responses = 0;
        var approvals = 0;
        var rejections = 0;

        // Add initiator's signature first
        if (docket.ProposerSignature != null)
        {
            signatures.Add(new CollectedSignature
            {
                ValidatorId = docket.ProposerValidatorId,
                Signature = docket.ProposerSignature,
                SignedAt = docket.CreatedAt,
                IsInitiator = true
            });
            Interlocked.Increment(ref approvals);
        }

        // Filter out self from validators to request from
        var othersToRequest = validators
            .Where(v => v.ValidatorId != _validatorConfig.ValidatorId)
            .ToList();

        if (othersToRequest.Count == 0)
        {
            _logger.LogDebug("No other validators to request signatures from");
            return CreateResult(
                signatures.ToList(),
                config,
                validators.Count,
                1, // Just self
                1, // Just self approved
                0,
                false,
                stopwatch.Elapsed,
                [],
                new Dictionary<string, string>());
        }

        // Create timeout cancellation
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(config.DocketTimeout);

        try
        {
            // Request signatures in parallel
            var tasks = othersToRequest.Select(async validator =>
            {
                try
                {
                    var response = await RequestSignatureAsync(
                        validator,
                        docket,
                        _leaderElection.CurrentTerm,
                        timeoutCts.Token);

                    if (response == null)
                    {
                        nonResponders.Add(validator.ValidatorId);
                        return;
                    }

                    Interlocked.Increment(ref responses);

                    if (response.Approved && response.Signature != null)
                    {
                        signatures.Add(new CollectedSignature
                        {
                            ValidatorId = response.ValidatorId,
                            Signature = response.Signature,
                            SignedAt = DateTimeOffset.UtcNow,
                            IsInitiator = false
                        });
                        Interlocked.Increment(ref approvals);
                    }
                    else
                    {
                        Interlocked.Increment(ref rejections);
                        if (response.RejectionDetails != null)
                        {
                            rejectionDetails[validator.ValidatorId] = response.RejectionDetails;
                        }
                    }

                    // Early exit if we have max signatures
                    if (approvals >= config.MaxSignaturesPerDocket)
                    {
                        _logger.LogDebug("Max signatures reached, stopping collection early");
                        await timeoutCts.CancelAsync();
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    nonResponders.Add(validator.ValidatorId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Error requesting signature from validator {ValidatorId}",
                        validator.ValidatorId);
                    nonResponders.Add(validator.ValidatorId);
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // Timeout occurred, but we may still have enough signatures
            _logger.LogWarning(
                "Signature collection timed out for docket {DocketId} after {Timeout}",
                docket.DocketId, config.DocketTimeout);
        }

        stopwatch.Stop();

        var result = CreateResult(
            signatures.ToList(),
            config,
            validators.Count,
            responses + 1, // +1 for self
            approvals,
            rejections,
            timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested,
            stopwatch.Elapsed,
            nonResponders.ToList(),
            new Dictionary<string, string>(rejectionDetails));

        if (result.ThresholdMet)
        {
            Interlocked.Increment(ref _successfulCollections);
            _logger.LogInformation(
                "Signature collection succeeded for docket {DocketId}: {Approvals}/{Total} ({Percentage:F1}%)",
                docket.DocketId, result.Approvals, result.TotalValidators, result.ApprovalPercentage);
        }
        else
        {
            Interlocked.Increment(ref _failedCollections);
            _logger.LogWarning(
                "Signature collection failed for docket {DocketId}: {Approvals}/{Total} (need {Min})",
                docket.DocketId, result.Approvals, result.TotalValidators, config.SignatureThresholdMin);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<ValidatorSignatureResponse?> RequestSignatureAsync(
        ValidatorInfo validator,
        Docket docket,
        long term,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(docket);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug(
                "Requesting signature from validator {ValidatorId} for docket {DocketId}",
                validator.ValidatorId, docket.DocketId);

            // Create gRPC channel to peer validator and request vote
            using var channel = GrpcChannel.ForAddress(validator.GrpcEndpoint);
            var client = new Sorcha.Validator.Grpc.V1.ValidatorService.ValidatorServiceClient(channel);

            var request = new Sorcha.Validator.Grpc.V1.VoteRequest
            {
                DocketId = docket.DocketId,
                RegisterId = docket.RegisterId,
                DocketNumber = docket.DocketNumber,
                DocketHash = docket.DocketHash,
                PreviousHash = docket.PreviousHash ?? string.Empty,
                CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(docket.CreatedAt),
                ProposerValidatorId = docket.ProposerValidatorId,
                MerkleRoot = docket.MerkleRoot
            };

            if (docket.ProposerSignature != null)
            {
                request.ProposerSignature = new Sorcha.Validator.Grpc.V1.Signature
                {
                    PublicKey = Base64Url.EncodeToString(docket.ProposerSignature.PublicKey),
                    SignatureValue = Base64Url.EncodeToString(docket.ProposerSignature.SignatureValue),
                    Algorithm = docket.ProposerSignature.Algorithm
                };
            }

            var response = await client.RequestVoteAsync(request, cancellationToken: ct);

            stopwatch.Stop();

            var approved = response.Decision == Sorcha.Validator.Grpc.V1.VoteDecision.Approve;

            return new ValidatorSignatureResponse
            {
                ValidatorId = response.ValidatorId,
                Approved = approved,
                Signature = approved && response.ValidatorSignature != null
                    ? new RegisterSignature
                    {
                        PublicKey = Base64Url.DecodeFromChars(response.ValidatorSignature.PublicKey),
                        SignatureValue = Base64Url.DecodeFromChars(response.ValidatorSignature.SignatureValue),
                        Algorithm = response.ValidatorSignature.Algorithm,
                        SignedAt = response.VotedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                        SignedBy = response.ValidatorId
                    }
                    : null,
                RejectionDetails = !approved ? response.RejectionReason : null,
                Latency = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug(
                "Signature request to {ValidatorId} was cancelled",
                validator.ValidatorId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to request signature from validator {ValidatorId}",
                validator.ValidatorId);
            return null;
        }
    }

    /// <inheritdoc/>
    public Task<bool> AddSignatureAsync(
        string registerId,
        string docketId,
        ConsensusVote vote,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);
        ArgumentNullException.ThrowIfNull(vote);

        var key = $"{registerId}:{docketId}";

        _logger.LogDebug(
            "Adding incoming signature from {ValidatorId} for docket {DocketId}",
            vote.ValidatorId, docketId);

        // Get or create the signatures dictionary for this docket
        var docketSignatures = _incomingSignatures.GetOrAdd(key, _ => new ConcurrentDictionary<string, ConsensusVote>());

        // Try to add the signature (only one per validator)
        var added = docketSignatures.TryAdd(vote.ValidatorId, vote);

        if (added)
        {
            Interlocked.Increment(ref _incomingSignaturesReceived);
            _logger.LogInformation(
                "Received signature from {ValidatorId} for docket {DocketId} ({Decision})",
                vote.ValidatorId, docketId, vote.Decision);
        }
        else
        {
            _logger.LogDebug(
                "Duplicate signature from {ValidatorId} for docket {DocketId}, ignoring",
                vote.ValidatorId, docketId);
        }

        return Task.FromResult(added);
    }

    /// <inheritdoc/>
    public Task<ConsensusVote?> GetLocalVoteAsync(
        string registerId,
        string docketId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);

        var key = $"{registerId}:{docketId}";

        _localVotes.TryGetValue(key, out var vote);

        return Task.FromResult(vote);
    }

    /// <summary>
    /// Store a local vote for later retrieval during signature exchange.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketId">Docket ID</param>
    /// <param name="vote">The local vote</param>
    public void StoreLocalVote(string registerId, string docketId, ConsensusVote vote)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(docketId);
        ArgumentNullException.ThrowIfNull(vote);

        var key = $"{registerId}:{docketId}";
        _localVotes[key] = vote;

        _logger.LogDebug(
            "Stored local vote for docket {DocketId} ({Decision})",
            docketId, vote.Decision);
    }

    /// <summary>
    /// Get all collected signatures for a docket.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketId">Docket ID</param>
    /// <returns>List of collected signatures</returns>
    public IReadOnlyList<ConsensusVote> GetCollectedSignatures(string registerId, string docketId)
    {
        var key = $"{registerId}:{docketId}";

        if (_incomingSignatures.TryGetValue(key, out var signatures))
        {
            return signatures.Values.ToList();
        }

        return [];
    }

    /// <summary>
    /// Clear signatures for a docket (after consensus is complete).
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docketId">Docket ID</param>
    public void ClearDocketSignatures(string registerId, string docketId)
    {
        var key = $"{registerId}:{docketId}";
        _incomingSignatures.TryRemove(key, out _);
        _localVotes.TryRemove(key, out _);

        _logger.LogDebug("Cleared signatures for docket {DocketId}", docketId);
    }

    private static SignatureCollectionResult CreateResult(
        IReadOnlyList<CollectedSignature> signatures,
        ConsensusConfig config,
        int totalValidators,
        int responsesReceived,
        int approvals,
        int rejections,
        bool timedOut,
        TimeSpan duration,
        IReadOnlyList<string> nonResponders,
        IReadOnlyDictionary<string, string> rejectionDetails)
    {
        // Check if we met the minimum threshold
        var thresholdMet = approvals >= config.SignatureThresholdMin;

        return new SignatureCollectionResult
        {
            Signatures = signatures,
            ThresholdMet = thresholdMet,
            TimedOut = timedOut,
            TotalValidators = totalValidators,
            ResponsesReceived = responsesReceived,
            Approvals = approvals,
            Rejections = rejections,
            Duration = duration,
            NonResponders = nonResponders,
            RejectionDetails = rejectionDetails
        };
    }

    /// <summary>
    /// Get statistics about signature collection
    /// </summary>
    public SignatureCollectorStats GetStats()
    {
        return new SignatureCollectorStats
        {
            TotalCollections = Interlocked.Read(ref _totalCollections),
            SuccessfulCollections = Interlocked.Read(ref _successfulCollections),
            FailedCollections = Interlocked.Read(ref _failedCollections)
        };
    }
}

/// <summary>
/// Statistics for signature collection
/// </summary>
public record SignatureCollectorStats
{
    /// <summary>Total collection attempts</summary>
    public long TotalCollections { get; init; }

    /// <summary>Successful collections (threshold met)</summary>
    public long SuccessfulCollections { get; init; }

    /// <summary>Failed collections (threshold not met)</summary>
    public long FailedCollections { get; init; }

    /// <summary>Success rate (0-1)</summary>
    public double SuccessRate => TotalCollections > 0
        ? (double)SuccessfulCollections / TotalCollections
        : 0;
}
