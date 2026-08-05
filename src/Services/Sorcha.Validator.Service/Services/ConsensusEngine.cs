// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Grpc.V1;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Peer;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;
using System.Diagnostics;
using Sorcha.Register.Models;
// The gRPC contract also declares a VoteDecision (Sorcha.Validator.Grpc.V1). Bare
// VoteDecision means the canonical ledger enum; the proto one stays Grpc.V1.VoteDecision.
using VoteDecision = Sorcha.Register.Models.VoteDecision;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Manages distributed consensus for proposed dockets
/// </summary>
public class ConsensusEngine : IConsensusEngine
{
    private readonly IPeerServiceClient _peerClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly IRegisterServiceClient _registerClient;
    private readonly Sorcha.Validator.Core.Validators.ITransactionValidator _transactionValidator;
    private readonly IValidationEngine _validationEngine;
    private readonly IValidatorRegistry _validatorRegistry;
    private readonly ConsensusConfiguration _consensusConfig;
    private readonly ValidatorConfiguration _validatorConfig;
    private readonly ILogger<ConsensusEngine> _logger;
    private readonly bool _isDevelopment;

    public ConsensusEngine(
        IPeerServiceClient peerClient,
        IWalletServiceClient walletClient,
        IRegisterServiceClient registerClient,
        Sorcha.Validator.Core.Validators.ITransactionValidator transactionValidator,
        IValidationEngine validationEngine,
        IValidatorRegistry validatorRegistry,
        IOptions<ConsensusConfiguration> consensusConfig,
        IOptions<ValidatorConfiguration> validatorConfig,
        IHostEnvironment hostEnvironment,
        ILogger<ConsensusEngine> logger)
    {
        _peerClient = peerClient ?? throw new ArgumentNullException(nameof(peerClient));
        _walletClient = walletClient ?? throw new ArgumentNullException(nameof(walletClient));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _transactionValidator = transactionValidator ?? throw new ArgumentNullException(nameof(transactionValidator));
        _validationEngine = validationEngine ?? throw new ArgumentNullException(nameof(validationEngine));
        _validatorRegistry = validatorRegistry ?? throw new ArgumentNullException(nameof(validatorRegistry));
        _consensusConfig = consensusConfig?.Value ?? throw new ArgumentNullException(nameof(consensusConfig));
        _validatorConfig = validatorConfig?.Value ?? throw new ArgumentNullException(nameof(validatorConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isDevelopment = hostEnvironment?.EnvironmentName == Environments.Development;

        if (_consensusConfig.SingleValidatorAutoApprove && !_isDevelopment)
        {
            _logger.LogWarning(
                "SingleValidatorAutoApprove is enabled but environment is {Environment} — " +
                "this setting will be IGNORED outside Development. Multi-validator consensus is required",
                hostEnvironment?.EnvironmentName);
        }
    }

    /// <inheritdoc/>
    public async Task<ConsensusResult> AchieveConsensusAsync(
        Docket docket,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting consensus for docket {DocketNumber} on register {RegisterId}",
            docket.DocketNumber, docket.RegisterId);

        try
        {
            // Step 1: Publish docket to peer network
            var docketData = DocketSerializer.SerializeToBytes(docket);
            await _peerClient.PublishProposedDocketAsync(docket.RegisterId, docket.DocketId, docketData, cancellationToken);
            _logger.LogDebug("Published docket {DocketNumber} to peer network", docket.DocketNumber);

            // Step 2: Query for active validators
            var validators = await _peerClient.QueryValidatorsAsync(docket.RegisterId, cancellationToken);
            _logger.LogInformation(
                "Found {ValidatorCount} active validators for register {RegisterId}",
                validators.Count, docket.RegisterId);

            if (validators.Count == 0)
            {
                if (_consensusConfig.SingleValidatorAutoApprove && _isDevelopment)
                {
                    stopwatch.Stop();
                    _logger.LogDebug(
                        "No peer validators found for register {RegisterId} — auto-approving in single-validator mode (Development only)",
                        docket.RegisterId);

                    return new ConsensusResult
                    {
                        Achieved = true,
                        Docket = docket,
                        Votes = Array.Empty<Models.ConsensusVote>(),
                        TotalValidators = 1,
                        Duration = stopwatch.Elapsed,
                        CompletedAt = DateTimeOffset.UtcNow
                    };
                }

                _logger.LogWarning("No validators found for register {RegisterId}", docket.RegisterId);
                return new ConsensusResult
                {
                    Achieved = false,
                    Docket = docket,
                    Votes = Array.Empty<Models.ConsensusVote>(),
                    TotalValidators = 0,
                    FailureReason = "No validators found for register",
                    Duration = stopwatch.Elapsed,
                    CompletedAt = DateTimeOffset.UtcNow
                };
            }

            // Step 3: Collect votes from validators using parallel gRPC calls
            var voteCollectionTasks = validators
                .Where(v => v.ValidatorId != _validatorConfig.ValidatorId) // Don't vote for our own docket
                .Select(v => CollectVoteFromValidatorAsync(v, docket, cancellationToken))
                .ToList();

            // Wait for all votes with timeout
            using var timeoutCts = new CancellationTokenSource((int)_consensusConfig.VoteTimeout.TotalMilliseconds);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            List<Models.ConsensusVote?> collectedVotes;
            try
            {
                var voteResults = await Task.WhenAll(voteCollectionTasks);
                collectedVotes = voteResults.Where(v => v != null).ToList()!;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Consensus timeout after {Timeout}s for docket {DocketNumber}",
                    _consensusConfig.VoteTimeout.TotalSeconds, docket.DocketNumber);

                // Collect any votes that completed before timeout
                collectedVotes = voteCollectionTasks
                    .Where(t => t.IsCompletedSuccessfully)
                    .Select(t => t.GetAwaiter().GetResult())
                    .Where(v => v != null)
                    .ToList()!;
            }

            stopwatch.Stop();

            // Step 4: Validate votes and check for consensus
            var validVotes = await ValidateVotesAsync(collectedVotes!, docket, cancellationToken);

            var approvalCount = validVotes.Count(v => v.Decision == VoteDecision.Approve);
            var totalValidators = validators.Count;
            var approvalPercentage = (double)approvalCount / totalValidators;

            var consensusAchieved = approvalPercentage > _consensusConfig.ApprovalThreshold;

            _logger.LogInformation(
                "Consensus {Result} for docket {DocketNumber}: {ApprovalCount}/{TotalValidators} ({ApprovalPercentage:P2}) validators approved (threshold: {Threshold:P2})",
                consensusAchieved ? "ACHIEVED" : "FAILED",
                docket.DocketNumber,
                approvalCount,
                totalValidators,
                approvalPercentage,
                _consensusConfig.ApprovalThreshold);

            // Step 5: Report any invalid proposer behavior
            var rejectedVotes = validVotes.Where(v => v.Decision == VoteDecision.Reject).ToList();
            if (rejectedVotes.Count > totalValidators / 2)
            {
                // Majority rejected - report proposer as potentially malicious
                await _peerClient.ReportValidatorBehaviorAsync(
                    docket.ProposerValidatorId,
                    "ProposedInvalidDocket",
                    $"Docket {docket.DocketNumber} rejected by {rejectedVotes.Count}/{totalValidators} validators",
                    cancellationToken);

                _logger.LogWarning(
                    "Reported proposer {ProposerId} for invalid docket {DocketNumber}",
                    docket.ProposerValidatorId, docket.DocketNumber);
            }

            return new ConsensusResult
            {
                Achieved = consensusAchieved,
                Docket = docket,
                Votes = validVotes,
                TotalValidators = totalValidators,
                FailureReason = consensusAchieved ? null : "Insufficient validator approvals",
                Duration = stopwatch.Elapsed,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error during consensus for docket {DocketNumber}", docket.DocketNumber);

            return new ConsensusResult
            {
                Achieved = false,
                Docket = docket,
                Votes = Array.Empty<Models.ConsensusVote>(),
                TotalValidators = 0,
                FailureReason = $"Consensus error: {ex.Message}",
                Duration = stopwatch.Elapsed,
                CompletedAt = DateTimeOffset.UtcNow
            };
        }
    }

    /// <inheritdoc/>
    public async Task<Models.ConsensusVote> ValidateAndVoteAsync(
        Docket docket,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Validating docket {DocketNumber} from proposer {ProposerId}",
            docket.DocketNumber, docket.ProposerValidatorId);

        var voteId = Guid.NewGuid().ToString();
        var votedAt = DateTimeOffset.UtcNow;

        try
        {
            // Validate docket structure
            if (string.IsNullOrEmpty(docket.DocketHash))
            {
                return await CreateRejectionVoteAsync(
                    voteId, docket, "Missing docket hash", votedAt, cancellationToken);
            }

            // Validate chain linkage
            if (docket.DocketNumber > 0 && string.IsNullOrEmpty(docket.PreviousHash))
            {
                return await CreateRejectionVoteAsync(
                    voteId, docket, "Missing previous hash for non-genesis docket", votedAt, cancellationToken);
            }

            // Validate proposer signature
            var signatureValid = await _walletClient.VerifySignatureAsync(
                Base64Url.EncodeToString(docket.ProposerSignature.PublicKey),
                docket.DocketHash,
                Base64Url.EncodeToString(docket.ProposerSignature.SignatureValue),
                docket.ProposerSignature.Algorithm,
                cancellationToken);

            if (!signatureValid)
            {
                return await CreateRejectionVoteAsync(
                    voteId, docket, "Invalid proposer signature", votedAt, cancellationToken);
            }

            // Validate previous hash exists in chain
            if (docket.DocketNumber > 0)
            {
                var previousDocket = await _registerClient.ReadDocketAsync(
                    docket.RegisterId,
                    docket.DocketNumber - 1,
                    cancellationToken);

                if (previousDocket == null)
                {
                    return await CreateRejectionVoteAsync(
                        voteId, docket, "Previous docket not found in chain", votedAt, cancellationToken);
                }

                if (previousDocket.DocketHash != docket.PreviousHash)
                {
                    return await CreateRejectionVoteAsync(
                        voteId, docket, "Previous hash mismatch", votedAt, cancellationToken);
                }
            }

            // Validate all transactions using full validation engine
            foreach (var transaction in docket.Transactions)
            {
                try
                {
                    var validationResult = await _validationEngine.ValidateTransactionAsync(transaction, cancellationToken);

                    if (!validationResult.IsValid)
                    {
                        var errors = string.Join(", ", validationResult.Errors.Select(e => e.Message));
                        return await CreateRejectionVoteAsync(
                            voteId, docket, $"Transaction {transaction.TransactionId} validation failed: {errors}",
                            votedAt, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    // Fall back to structural validation if full validation engine is unavailable
                    _logger.LogWarning(ex,
                        "Full validation engine failed for transaction {TransactionId}, falling back to structural validation",
                        transaction.TransactionId);

                    var signatures = transaction.Signatures.Select(s =>
                        new Sorcha.Validator.Core.Validators.TransactionSignature(
                            Base64Url.EncodeToString(s.PublicKey),
                            Base64Url.EncodeToString(s.SignatureValue),
                            s.Algorithm)).ToList();

                    var structuralResult = _transactionValidator.ValidateTransactionStructure(
                        transaction.TransactionId,
                        transaction.RegisterId,
                        transaction.BlueprintId,
                        transaction.Payload,
                        transaction.PayloadHash,
                        signatures,
                        transaction.CreatedAt);

                    if (!structuralResult.IsValid)
                    {
                        var errors = string.Join(", ", structuralResult.Errors.Select(e => e.Message));
                        return await CreateRejectionVoteAsync(
                            voteId, docket, $"Transaction {transaction.TransactionId} validation failed: {errors}",
                            votedAt, cancellationToken);
                    }
                }
            }

            // All validations passed - approve
            _logger.LogInformation("Approving docket {DocketNumber}", docket.DocketNumber);
            return await CreateApprovalVoteAsync(voteId, docket, votedAt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating docket {DocketNumber}", docket.DocketNumber);
            return await CreateRejectionVoteAsync(
                voteId, docket, $"Validation error: {ex.Message}", votedAt, cancellationToken);
        }
    }

    /// <summary>
    /// Collects a vote from a single validator
    /// </summary>
    private async Task<Models.ConsensusVote?> CollectVoteFromValidatorAsync(
        Sorcha.ServiceClients.Peer.ValidatorInfo validator,
        Docket docket,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug(
                "Requesting vote from validator {ValidatorId} at {Endpoint}",
                validator.ValidatorId, validator.GrpcEndpoint);

            // Create gRPC channel to peer validator
            using var channel = GrpcChannel.ForAddress(validator.GrpcEndpoint);
            var client = new ValidatorService.ValidatorServiceClient(channel);

            // Create vote request
            var request = new VoteRequest
            {
                DocketId = docket.DocketId,
                RegisterId = docket.RegisterId,
                DocketNumber = docket.DocketNumber,
                DocketHash = docket.DocketHash,
                PreviousHash = docket.PreviousHash ?? string.Empty,
                CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(docket.CreatedAt),
                ProposerValidatorId = docket.ProposerValidatorId,
                MerkleRoot = docket.MerkleRoot,
                ProposerSignature = new Sorcha.Validator.Grpc.V1.Signature
                {
                    PublicKey = Base64Url.EncodeToString(docket.ProposerSignature.PublicKey),
                    SignatureValue = Base64Url.EncodeToString(docket.ProposerSignature.SignatureValue),
                    Algorithm = docket.ProposerSignature.Algorithm
                }
            };

            // Add transactions
            foreach (var tx in docket.Transactions)
            {
                request.Transactions.Add(MapTransactionToProto(tx));
            }

            // Call RequestVote RPC with retry logic
            VoteResponse? response = null;
            for (int attempt = 0; attempt < _consensusConfig.MaxRetries; attempt++)
            {
                try
                {
                    response = await client.RequestVoteAsync(request, cancellationToken: cancellationToken);
                    break;
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable && attempt < _consensusConfig.MaxRetries - 1)
                {
                    _logger.LogWarning(
                        "Validator {ValidatorId} unavailable, retrying ({Attempt}/{MaxRetries})",
                        validator.ValidatorId, attempt + 1, _consensusConfig.MaxRetries);

                    await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)), cancellationToken);
                }
            }

            if (response == null)
            {
                _logger.LogWarning("Failed to get vote from validator {ValidatorId}", validator.ValidatorId);
                return null;
            }

            // Map response to ConsensusVote
            var vote = new Models.ConsensusVote
            {
                VoteId = response.VoteId,
                DocketId = docket.DocketId,
                ValidatorId = response.ValidatorId,
                Decision = response.Decision switch
                {
                    Sorcha.Validator.Grpc.V1.VoteDecision.Approve => VoteDecision.Approve,
                    Sorcha.Validator.Grpc.V1.VoteDecision.Reject => VoteDecision.Reject,
                    _ => VoteDecision.Reject
                },
                RejectionReason = response.Decision == Sorcha.Validator.Grpc.V1.VoteDecision.Reject ? response.RejectionReason : null,
                VotedAt = response.VotedAt.ToDateTimeOffset(),
                ValidatorSignature = new Models.Signature
                {
                    PublicKey = Base64Url.DecodeFromChars(response.ValidatorSignature.PublicKey),
                    SignatureValue = Base64Url.DecodeFromChars(response.ValidatorSignature.SignatureValue),
                    Algorithm = response.ValidatorSignature.Algorithm,
                    SignedAt = response.VotedAt.ToDateTimeOffset()
                },
                DocketHash = docket.DocketHash
            };

            _logger.LogDebug(
                "Received {Decision} vote from validator {ValidatorId}",
                vote.Decision, validator.ValidatorId);

            return vote;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting vote from validator {ValidatorId}", validator.ValidatorId);
            return null;
        }
    }

    /// <summary>
    /// Validates collected votes (signature verification, double-vote detection)
    /// </summary>
    private async Task<List<Models.ConsensusVote>> ValidateVotesAsync(
        List<Models.ConsensusVote> votes,
        Docket docket,
        CancellationToken cancellationToken)
    {
        var validVotes = new List<Models.ConsensusVote>();
        var seenValidators = new HashSet<string>();

        foreach (var vote in votes)
        {
            // Check for double-vote
            if (seenValidators.Contains(vote.ValidatorId))
            {
                _logger.LogWarning(
                    "SECURITY: Detected double-vote from validator {ValidatorId} on docket {DocketNumber}",
                    vote.ValidatorId, docket.DocketNumber);

                await _peerClient.ReportValidatorBehaviorAsync(
                    vote.ValidatorId,
                    "DoubleVote",
                    $"Validator cast multiple votes on docket {docket.DocketNumber}",
                    cancellationToken);

                continue;
            }

            // SEC-AUDIT 4.1: Verify voter is a registered, Active validator
            var registeredValidator = await _validatorRegistry.GetValidatorAsync(
                docket.RegisterId, vote.ValidatorId, cancellationToken);

            if (registeredValidator == null)
            {
                _logger.LogWarning(
                    "SECURITY: Vote rejected from unregistered validator {ValidatorId} on docket {DocketNumber}",
                    vote.ValidatorId, docket.DocketNumber);
                continue;
            }

            if (registeredValidator.Status != Interfaces.ValidatorStatus.Active)
            {
                _logger.LogWarning(
                    "SECURITY: Vote rejected from non-Active validator {ValidatorId} (status: {Status}) on docket {DocketNumber}",
                    vote.ValidatorId, registeredValidator.Status, docket.DocketNumber);
                continue;
            }

            // SEC-AUDIT 4.1: Verify vote's public key matches the registered validator's public key
            var votePublicKeyHex = Convert.ToHexStringLower(vote.ValidatorSignature.PublicKey);
            if (!string.Equals(votePublicKeyHex, registeredValidator.PublicKey, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(Convert.ToBase64String(vote.ValidatorSignature.PublicKey), registeredValidator.PublicKey, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "SECURITY: Vote rejected — public key mismatch for validator {ValidatorId} on docket {DocketNumber}. " +
                    "Vote key does not match registered key. Possible impersonation attempt.",
                    vote.ValidatorId, docket.DocketNumber);

                await _peerClient.ReportValidatorBehaviorAsync(
                    vote.ValidatorId,
                    "KeyMismatch",
                    $"Vote public key does not match registered key on docket {docket.DocketNumber}",
                    cancellationToken);

                continue;
            }

            // Verify vote cryptographic signature
            var signatureValid = await _walletClient.VerifySignatureAsync(
                Base64Url.EncodeToString(vote.ValidatorSignature.PublicKey),
                vote.DocketHash,
                Base64Url.EncodeToString(vote.ValidatorSignature.SignatureValue),
                vote.ValidatorSignature.Algorithm,
                cancellationToken);

            if (!signatureValid)
            {
                _logger.LogWarning(
                    "SECURITY: Invalid cryptographic signature on vote from validator {ValidatorId} on docket {DocketNumber}",
                    vote.ValidatorId, docket.DocketNumber);
                continue;
            }

            validVotes.Add(vote);
            seenValidators.Add(vote.ValidatorId);
        }

        return validVotes;
    }

    /// <summary>
    /// Creates an approval vote
    /// </summary>
    private async Task<Models.ConsensusVote> CreateApprovalVoteAsync(
        string voteId,
        Docket docket,
        DateTimeOffset votedAt,
        CancellationToken cancellationToken)
    {
        var systemWalletId = await _walletClient.CreateOrRetrieveSystemWalletAsync(
            _validatorConfig.ValidatorId,
            cancellationToken);

        var signResult = await _walletClient.SignDataAsync(
            systemWalletId,
            docket.DocketHash,
            cancellationToken);

        return new Models.ConsensusVote
        {
            VoteId = voteId,
            DocketId = docket.DocketId,
            ValidatorId = _validatorConfig.ValidatorId,
            Decision = VoteDecision.Approve,
            VotedAt = votedAt,
            ValidatorSignature = new Models.Signature
            {
                PublicKey = signResult.PublicKey,
                SignatureValue = signResult.Signature,
                Algorithm = signResult.Algorithm,
                SignedAt = votedAt
            },
            DocketHash = docket.DocketHash
        };
    }

    /// <summary>
    /// Creates a rejection vote
    /// </summary>
    private async Task<Models.ConsensusVote> CreateRejectionVoteAsync(
        string voteId,
        Docket docket,
        string reason,
        DateTimeOffset votedAt,
        CancellationToken cancellationToken)
    {
        var systemWalletId = await _walletClient.CreateOrRetrieveSystemWalletAsync(
            _validatorConfig.ValidatorId,
            cancellationToken);

        var signResult = await _walletClient.SignDataAsync(
            systemWalletId,
            docket.DocketHash,
            cancellationToken);

        return new Models.ConsensusVote
        {
            VoteId = voteId,
            DocketId = docket.DocketId,
            ValidatorId = _validatorConfig.ValidatorId,
            Decision = VoteDecision.Reject,
            RejectionReason = reason,
            VotedAt = votedAt,
            ValidatorSignature = new Models.Signature
            {
                PublicKey = signResult.PublicKey,
                SignatureValue = signResult.Signature,
                Algorithm = signResult.Algorithm,
                SignedAt = votedAt
            },
            DocketHash = docket.DocketHash
        };
    }

    /// <summary>
    /// Maps domain Transaction to protobuf Transaction
    /// </summary>
    private static Sorcha.Validator.Grpc.V1.Transaction MapTransactionToProto(Models.Transaction transaction)
    {
        var protoTx = new Sorcha.Validator.Grpc.V1.Transaction
        {
            TransactionId = transaction.TransactionId,
            RegisterId = transaction.RegisterId,
            BlueprintId = transaction.BlueprintId,
            ActionId = transaction.ActionId ?? string.Empty,
            PayloadJson = transaction.PayloadJson ?? string.Empty,
            CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(transaction.CreatedAt),
            PayloadHash = transaction.PayloadHash,
            Priority = transaction.Priority switch
            {
                Models.TransactionPriority.High => Sorcha.Validator.Grpc.V1.TransactionPriority.High,
                Models.TransactionPriority.Normal => Sorcha.Validator.Grpc.V1.TransactionPriority.Normal,
                Models.TransactionPriority.Low => Sorcha.Validator.Grpc.V1.TransactionPriority.Low,
                _ => Sorcha.Validator.Grpc.V1.TransactionPriority.Unspecified
            }
        };

        if (transaction.ExpiresAt.HasValue)
        {
            protoTx.ExpiresAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(transaction.ExpiresAt.Value);
        }

        foreach (var sig in transaction.Signatures)
        {
            protoTx.Signatures.Add(new Sorcha.Validator.Grpc.V1.Signature
            {
                PublicKey = Base64Url.EncodeToString(sig.PublicKey),
                SignatureValue = Base64Url.EncodeToString(sig.SignatureValue),
                Algorithm = sig.Algorithm
            });
        }

        foreach (var kvp in transaction.Metadata)
        {
            protoTx.Metadata.Add(kvp.Key, kvp.Value);
        }

        return protoTx;
    }
}
