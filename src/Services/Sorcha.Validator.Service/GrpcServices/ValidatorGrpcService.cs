// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Grpc.V1;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using Sorcha.Validator.Service.Models;
using Sorcha.Register.Models;
// The gRPC contract also declares a ConsensusVote (Sorcha.Validator.Grpc.V1). Bare
// ConsensusVote means the canonical ledger type; the proto one stays Grpc.V1.ConsensusVote.
using ConsensusVote = Sorcha.Register.Models.ConsensusVote;
// The gRPC contract has its own VoteDecision (Sorcha.Validator.Grpc.V1). Bare VoteDecision
// means the canonical ledger enum; the proto one is always written Grpc.V1.VoteDecision.
using VoteDecision = Sorcha.Register.Models.VoteDecision;

namespace Sorcha.Validator.Service.GrpcServices;

/// <summary>
/// gRPC service implementation for validator-to-validator communication
/// </summary>
/// <remarks>
/// This service implements the ValidatorService protocol defined in validator.proto.
/// It handles:
/// <list type="bullet">
///   <item>RequestVote: Validates proposed dockets and returns signed votes</item>
///   <item>ValidateDocket: Validates confirmed dockets from peers before persistence</item>
///   <item>GetHealthStatus: Reports validator health and status</item>
///   <item>ReceiveTransaction: Receives transactions from peer network</item>
///   <item>ExchangeSignature: Exchanges signatures during consensus</item>
///   <item>ReceiveConfirmedDocket: Receives confirmed dockets for persistence</item>
/// </list>
/// </remarks>
public class ValidatorGrpcService : Sorcha.Validator.Grpc.V1.ValidatorService.ValidatorServiceBase
{
    private readonly IConsensusEngine _consensusEngine;
    private readonly ITransactionReceiver? _transactionReceiver;
    private readonly IDocketDistributor? _docketDistributor;
    private readonly IDocketConfirmer? _docketConfirmer;
    private readonly ISignatureCollector? _signatureCollector;
    private readonly IRegisterMonitoringRegistry? _monitoringRegistry;
    private readonly ValidatorConfiguration _validatorConfig;
    private readonly ILogger<ValidatorGrpcService> _logger;

    public ValidatorGrpcService(
        IConsensusEngine consensusEngine,
        IOptions<ValidatorConfiguration> validatorConfig,
        ILogger<ValidatorGrpcService> logger,
        ITransactionReceiver? transactionReceiver = null,
        IDocketDistributor? docketDistributor = null,
        IDocketConfirmer? docketConfirmer = null,
        ISignatureCollector? signatureCollector = null,
        IRegisterMonitoringRegistry? monitoringRegistry = null)
    {
        _consensusEngine = consensusEngine ?? throw new ArgumentNullException(nameof(consensusEngine));
        _validatorConfig = validatorConfig?.Value ?? throw new ArgumentNullException(nameof(validatorConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _transactionReceiver = transactionReceiver;
        _docketDistributor = docketDistributor;
        _docketConfirmer = docketConfirmer;
        _signatureCollector = signatureCollector;
        _monitoringRegistry = monitoringRegistry;
    }

    /// <summary>
    /// RequestVote RPC: Validates a proposed docket and returns a signed vote
    /// </summary>
    /// <remarks>
    /// This method is called by a peer validator proposing a new docket for consensus.
    /// It performs independent validation and returns an approve/reject vote.
    ///
    /// <para><b>User Story:</b> US3 - Distributed Consensus Achievement (P1)</para>
    /// <para><b>Acceptance Criteria:</b> AS3 - Receive and sign votes</para>
    /// </remarks>
    public override async Task<VoteResponse> RequestVote(VoteRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "Received vote request for docket {DocketNumber} on register {RegisterId} from {ProposerId}",
            request.DocketNumber, request.RegisterId, request.ProposerValidatorId);

        try
        {
            // Convert gRPC request to domain model
            var docket = MapProtoToDocket(request);

            // Validate and vote
            var vote = await _consensusEngine.ValidateAndVoteAsync(docket, context.CancellationToken);

            // Convert domain vote to gRPC response
            var response = new VoteResponse
            {
                VoteId = vote.VoteId,
                Decision = vote.Decision == VoteDecision.Approve
                    ? Grpc.V1.VoteDecision.Approve
                    : Grpc.V1.VoteDecision.Reject,
                RejectionReason = vote.RejectionReason ?? string.Empty,
                ValidatorId = vote.ValidatorId,
                VotedAt = Timestamp.FromDateTimeOffset(vote.VotedAt),
                ValidatorSignature = new Grpc.V1.Signature
                {
                    PublicKey = Base64Url.EncodeToString(vote.ValidatorSignature.PublicKey),
                    SignatureValue = Base64Url.EncodeToString(vote.ValidatorSignature.SignatureValue),
                    Algorithm = vote.ValidatorSignature.Algorithm
                }
            };

            _logger.LogInformation(
                "Returning {Decision} vote for docket {DocketNumber}",
                response.Decision, request.DocketNumber);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing vote request for docket {DocketNumber}", request.DocketNumber);
            throw new RpcException(new Status(StatusCode.Internal, $"Vote processing failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// ValidateDocket RPC: Validates a confirmed docket received from peers
    /// </summary>
    /// <remarks>
    /// This method is called when a peer broadcasts a confirmed docket.
    /// The validator must validate it before writing to local Register Service.
    ///
    /// <para><b>User Story:</b> US5 - Peer Docket Validation (P2)</para>
    /// <para><b>Acceptance Criteria:</b> AS1 - Validate transaction signatures, blueprint compliance, hash linkage</para>
    /// </remarks>
    public override async Task<DocketValidationResponse> ValidateDocket(
        DocketValidationRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "Received docket validation request for docket {DocketNumber} on register {RegisterId}",
            request.DocketNumber, request.RegisterId);

        try
        {
            // Convert gRPC request to domain model
            var docket = MapProtoToDocket(request);

            // Validate the docket independently
            var vote = await _consensusEngine.ValidateAndVoteAsync(docket, context.CancellationToken);

            // Determine if docket should be persisted
            var shouldPersist = vote.Decision == VoteDecision.Approve;

            var response = new DocketValidationResponse
            {
                IsValid = shouldPersist,
                ShouldPersist = shouldPersist,
                IsFork = false // Fork detection not implemented yet (US5)
            };

            if (!shouldPersist && vote.RejectionReason != null)
            {
                response.ValidationErrors.Add(vote.RejectionReason);
            }

            _logger.LogInformation(
                "Docket {DocketNumber} validation result: {Result}",
                request.DocketNumber, shouldPersist ? "Valid" : "Invalid");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating docket {DocketNumber}", request.DocketNumber);
            throw new RpcException(new Status(StatusCode.Internal, $"Docket validation failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// GetHealthStatus RPC: Returns validator health and status information
    /// </summary>
    public override Task<HealthStatusResponse> GetHealthStatus(Empty request, ServerCallContext context)
    {
        _logger.LogDebug("Health status check received");

        var response = new HealthStatusResponse
        {
            Status = Grpc.V1.HealthStatus.Healthy,
            ValidatorId = _validatorConfig.ValidatorId,
            ActiveRegisters = _monitoringRegistry?.GetAll().Count() ?? 0,
            LastHeartbeat = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        return Task.FromResult(response);
    }

    /// <summary>
    /// ReceiveTransaction RPC: Receives a transaction notification from the peer network
    /// </summary>
    /// <remarks>
    /// This method is called by the Peer Service when a transaction is gossiped.
    /// The validator validates and adds the transaction to its memory pool.
    ///
    /// <para><b>User Story:</b> US4 - Transaction Reception (P2)</para>
    /// <para><b>Acceptance Criteria:</b> AS1 - Receive transactions from peer network</para>
    /// </remarks>
    public override async Task<ReceiveTransactionResponse> ReceiveTransaction(
        ReceiveTransactionRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "Received transaction {TransactionHash} from peer {PeerId}",
            request.TransactionHash, request.SenderPeerId);

        if (_transactionReceiver == null)
        {
            _logger.LogWarning("TransactionReceiver not configured, rejecting transaction");
            return new ReceiveTransactionResponse
            {
                Accepted = false,
                ValidationErrors = { "Transaction receiver not configured" }
            };
        }

        try
        {
            var result = await _transactionReceiver.ReceiveTransactionAsync(
                request.TransactionHash,
                request.TransactionData.ToByteArray(),
                request.SenderPeerId,
                context.CancellationToken);

            var response = new ReceiveTransactionResponse
            {
                Accepted = result.Accepted,
                AlreadyKnown = result.AlreadyKnown,
                TransactionId = result.TransactionId ?? string.Empty
            };

            foreach (var error in result.ValidationErrors)
            {
                response.ValidationErrors.Add(error);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving transaction {TransactionHash}", request.TransactionHash);
            throw new RpcException(new Status(StatusCode.Internal, $"Transaction reception failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// ExchangeSignature RPC: Exchanges signatures during consensus
    /// </summary>
    /// <remarks>
    /// This method is called by peer validators to exchange consensus signatures.
    ///
    /// <para><b>User Story:</b> US3 - Distributed Consensus Achievement (P1)</para>
    /// <para><b>Acceptance Criteria:</b> AS2 - Exchange signatures with peers</para>
    /// </remarks>
    public override async Task<SignatureExchangeResponse> ExchangeSignature(
        SignatureExchangeRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "Received signature exchange for docket {DocketNumber} on register {RegisterId}",
            request.DocketNumber, request.RegisterId);

        if (_signatureCollector == null)
        {
            _logger.LogWarning("SignatureCollector not configured, rejecting signature exchange");
            return new SignatureExchangeResponse
            {
                Accepted = false,
                RejectionReason = "Signature collector not configured"
            };
        }

        try
        {
            // Convert incoming vote to domain model
            var incomingVote = MapProtoToConsensusVote(request.Vote);

            // Add the incoming signature to our collector
            var added = await _signatureCollector.AddSignatureAsync(
                request.RegisterId,
                request.DocketId,
                incomingVote,
                context.CancellationToken);

            if (!added)
            {
                return new SignatureExchangeResponse
                {
                    Accepted = false,
                    RejectionReason = "Signature not accepted (duplicate or invalid)"
                };
            }

            // Return our local vote for this docket if we have one
            var localVote = await _signatureCollector.GetLocalVoteAsync(
                request.RegisterId,
                request.DocketId,
                context.CancellationToken);

            var response = new SignatureExchangeResponse
            {
                Accepted = true
            };

            if (localVote != null)
            {
                response.LocalVote = MapConsensusVoteToProto(localVote);
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging signature for docket {DocketId}", request.DocketId);
            throw new RpcException(new Status(StatusCode.Internal, $"Signature exchange failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// ReceiveConfirmedDocket RPC: Receives a confirmed docket for persistence
    /// </summary>
    /// <remarks>
    /// This method is called when a peer broadcasts a confirmed docket.
    /// The validator validates and persists it to the Register Service.
    ///
    /// <para><b>User Story:</b> US5 - Peer Docket Validation (P2)</para>
    /// <para><b>Acceptance Criteria:</b> AS2 - Persist valid confirmed dockets</para>
    /// </remarks>
    public override async Task<ReceiveConfirmedDocketResponse> ReceiveConfirmedDocket(
        ReceiveConfirmedDocketRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "Received confirmed docket {DocketNumber} from peer {PeerId}",
            request.DocketNumber, request.SenderPeerId);

        try
        {
            // Convert to domain model
            var docket = MapProtoToDocket(request);

            // Validate the docket using DocketConfirmer if available
            if (_docketConfirmer != null)
            {
                var confirmResult = await _docketConfirmer.ConfirmDocketAsync(
                    docket,
                    docket.ProposerSignature,
                    0, // Term from leader election (not used for peer-received dockets)
                    context.CancellationToken);

                if (!confirmResult.Confirmed)
                {
                    var reason = confirmResult.RejectionReason?.ToString() ?? "Validation failed";
                    _logger.LogWarning(
                        "Confirmed docket {DocketNumber} failed validation: {Reason}",
                        request.DocketNumber, reason);

                    return new ReceiveConfirmedDocketResponse
                    {
                        Accepted = false,
                        ValidationErrors = { reason }
                    };
                }
            }

            // Submit to Register Service if DocketDistributor is available
            var persisted = false;
            if (_docketDistributor != null)
            {
                persisted = await _docketDistributor.SubmitToRegisterServiceAsync(
                    docket,
                    context.CancellationToken);
            }

            _logger.LogInformation(
                "Confirmed docket {DocketNumber} accepted, persisted: {Persisted}",
                request.DocketNumber, persisted);

            return new ReceiveConfirmedDocketResponse
            {
                Accepted = true,
                Persisted = persisted
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving confirmed docket {DocketNumber}", request.DocketNumber);
            throw new RpcException(new Status(StatusCode.Internal, $"Docket reception failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Maps gRPC VoteRequest to domain Docket model
    /// </summary>
    private static Models.Docket MapProtoToDocket(VoteRequest request)
    {
        var transactions = request.Transactions.Select(MapProtoToTransaction).ToList();

        return new Models.Docket
        {
            DocketId = request.DocketId,
            RegisterId = request.RegisterId,
            DocketNumber = request.DocketNumber,
            DocketHash = request.DocketHash,
            PreviousHash = string.IsNullOrEmpty(request.PreviousHash) ? null : request.PreviousHash,
            CreatedAt = request.CreatedAt.ToDateTimeOffset(),
            Transactions = transactions,
            Status = Models.DocketStatus.Proposed,
            ProposerValidatorId = request.ProposerValidatorId,
            ProposerSignature = new RegisterSignature
            {
                PublicKey = Base64Url.DecodeFromChars(request.ProposerSignature.PublicKey),
                SignatureValue = Base64Url.DecodeFromChars(request.ProposerSignature.SignatureValue),
                Algorithm = request.ProposerSignature.Algorithm,
                SignedAt = DateTimeOffset.UtcNow // Using current time as fallback
            },
            MerkleRoot = request.MerkleRoot
        };
    }

    /// <summary>
    /// Maps gRPC DocketValidationRequest to domain Docket model
    /// </summary>
    private static Models.Docket MapProtoToDocket(DocketValidationRequest request)
    {
        var transactions = request.Transactions.Select(MapProtoToTransaction).ToList();
        var consensusVotes = request.Votes.Select(MapProtoToConsensusVote).ToList();

        return new Models.Docket
        {
            DocketId = request.DocketId,
            RegisterId = request.RegisterId,
            DocketNumber = request.DocketNumber,
            DocketHash = request.DocketHash,
            PreviousHash = string.IsNullOrEmpty(request.PreviousHash) ? null : request.PreviousHash,
            CreatedAt = request.CreatedAt.ToDateTimeOffset(),
            Transactions = transactions,
            Status = Models.DocketStatus.Confirmed,
            ProposerValidatorId = request.ProposerValidatorId,
            ProposerSignature = new RegisterSignature
            {
                PublicKey = Base64Url.DecodeFromChars(request.ProposerSignature.PublicKey),
                SignatureValue = Base64Url.DecodeFromChars(request.ProposerSignature.SignatureValue),
                Algorithm = request.ProposerSignature.Algorithm,
                SignedAt = DateTimeOffset.UtcNow // Using current time as fallback
            },
            MerkleRoot = request.MerkleRoot,
            Votes = consensusVotes
        };
    }

    /// <summary>
    /// Maps gRPC Transaction to domain Transaction model
    /// </summary>
    private static Models.Transaction MapProtoToTransaction(Grpc.V1.Transaction protoTx)
    {
        var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            protoTx.PayloadJson);

        var signatures = protoTx.Signatures.Select(s => new RegisterSignature
        {
            PublicKey = Base64Url.DecodeFromChars(s.PublicKey),
            SignatureValue = Base64Url.DecodeFromChars(s.SignatureValue),
            Algorithm = s.Algorithm,
            SignedAt = DateTimeOffset.UtcNow // Proto doesn't have timestamp, using current time
        }).ToList();

        var metadata = new Dictionary<string, string>();
        foreach (var kvp in protoTx.Metadata)
        {
            metadata[kvp.Key] = kvp.Value;
        }

        return new Models.Transaction
        {
            TransactionId = protoTx.TransactionId,
            RegisterId = protoTx.RegisterId,
            BlueprintId = protoTx.BlueprintId,
            ActionId = protoTx.ActionId,
            Payload = payload,
            CreatedAt = protoTx.CreatedAt.ToDateTimeOffset(),
            ExpiresAt = protoTx.ExpiresAt != null ? protoTx.ExpiresAt.ToDateTimeOffset() : null,
            Signatures = signatures,
            PayloadHash = protoTx.PayloadHash,
            Priority = protoTx.Priority switch
            {
                Grpc.V1.TransactionPriority.High => Models.TransactionPriority.High,
                Grpc.V1.TransactionPriority.Normal => Models.TransactionPriority.Normal,
                Grpc.V1.TransactionPriority.Low => Models.TransactionPriority.Low,
                _ => Models.TransactionPriority.Normal
            },
            Metadata = metadata
        };
    }

    /// <summary>
    /// Maps gRPC ConsensusVote to domain ConsensusVote model
    /// </summary>
    private static ConsensusVote MapProtoToConsensusVote(Grpc.V1.ConsensusVote protoVote)
    {
        return new ConsensusVote
        {
            VoteId = protoVote.VoteId,
            DocketId = protoVote.DocketId,
            ValidatorId = protoVote.ValidatorId,
            Decision = protoVote.Decision == Grpc.V1.VoteDecision.Approve
                ? VoteDecision.Approve
                : VoteDecision.Reject,
            RejectionReason = string.IsNullOrEmpty(protoVote.RejectionReason) ? null : protoVote.RejectionReason,
            VotedAt = protoVote.VotedAt.ToDateTimeOffset(),
            ValidatorSignature = new RegisterSignature
            {
                PublicKey = Base64Url.DecodeFromChars(protoVote.ValidatorSignature.PublicKey),
                SignatureValue = Base64Url.DecodeFromChars(protoVote.ValidatorSignature.SignatureValue),
                Algorithm = protoVote.ValidatorSignature.Algorithm,
                SignedAt = protoVote.VotedAt.ToDateTimeOffset()
            },
            DocketHash = protoVote.DocketHash
        };
    }

    /// <summary>
    /// Maps domain ConsensusVote to gRPC ConsensusVote
    /// </summary>
    private static Grpc.V1.ConsensusVote MapConsensusVoteToProto(ConsensusVote vote)
    {
        return new Grpc.V1.ConsensusVote
        {
            VoteId = vote.VoteId,
            DocketId = vote.DocketId,
            ValidatorId = vote.ValidatorId,
            Decision = vote.Decision == VoteDecision.Approve
                ? Grpc.V1.VoteDecision.Approve
                : Grpc.V1.VoteDecision.Reject,
            RejectionReason = vote.RejectionReason ?? string.Empty,
            VotedAt = Timestamp.FromDateTimeOffset(vote.VotedAt),
            ValidatorSignature = new Grpc.V1.Signature
            {
                PublicKey = Base64Url.EncodeToString(vote.ValidatorSignature.PublicKey),
                SignatureValue = Base64Url.EncodeToString(vote.ValidatorSignature.SignatureValue),
                Algorithm = vote.ValidatorSignature.Algorithm
            },
            DocketHash = vote.DocketHash
        };
    }

    /// <summary>
    /// Maps gRPC ReceiveConfirmedDocketRequest to domain Docket model
    /// </summary>
    private static Models.Docket MapProtoToDocket(ReceiveConfirmedDocketRequest request)
    {
        var transactions = request.Transactions.Select(MapProtoToTransaction).ToList();
        var consensusVotes = request.Votes.Select(MapProtoToConsensusVote).ToList();

        return new Models.Docket
        {
            DocketId = request.DocketId,
            RegisterId = request.RegisterId,
            DocketNumber = request.DocketNumber,
            DocketHash = request.DocketHash,
            PreviousHash = string.IsNullOrEmpty(request.PreviousHash) ? null : request.PreviousHash,
            CreatedAt = request.CreatedAt.ToDateTimeOffset(),
            Transactions = transactions,
            Status = Models.DocketStatus.Confirmed,
            ProposerValidatorId = request.ProposerValidatorId,
            ProposerSignature = new RegisterSignature
            {
                PublicKey = Base64Url.DecodeFromChars(request.ProposerSignature.PublicKey),
                SignatureValue = Base64Url.DecodeFromChars(request.ProposerSignature.SignatureValue),
                Algorithm = request.ProposerSignature.Algorithm,
                SignedAt = DateTimeOffset.UtcNow
            },
            MerkleRoot = request.MerkleRoot,
            Votes = consensusVotes
        };
    }
}
