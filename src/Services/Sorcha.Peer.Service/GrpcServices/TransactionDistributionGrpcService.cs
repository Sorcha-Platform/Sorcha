// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Distribution;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.Peer.Service.GrpcServices;

/// <summary>
/// gRPC service implementation for transaction distribution across the peer network.
/// Handles gossip notifications, transaction retrieval, and chunked streaming for large transactions.
/// </summary>
public class TransactionDistributionGrpcService : TransactionDistribution.TransactionDistributionBase
{
    private readonly ILogger<TransactionDistributionGrpcService> _logger;
    private readonly GossipProtocolEngine _gossipEngine;
    private readonly TransactionQueueManager _queueManager;
    private readonly RegisterCache _registerCache;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Chunk size for streaming large transactions (64 KB).
    /// </summary>
    private const int ChunkSize = 64 * 1024;

    public TransactionDistributionGrpcService(
        ILogger<TransactionDistributionGrpcService> logger,
        GossipProtocolEngine gossipEngine,
        TransactionQueueManager queueManager,
        RegisterCache registerCache,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gossipEngine = gossipEngine ?? throw new ArgumentNullException(nameof(gossipEngine));
        _queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
    }

    /// <summary>
    /// Receives a gossip notification about a new transaction.
    /// Checks if the transaction is already known in the local cache;
    /// if not, queues it for retrieval via the transaction queue manager.
    /// </summary>
    public override async Task<NotificationAck> NotifyTransaction(
        TransactionNotification request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "Received transaction notification for {TxHash} from peer {PeerId} (register: {RegisterId})",
            request.TransactionHash, request.SenderPeerId, request.RegisterId);

        // Check if we already have this transaction in the register cache
        var alreadyKnown = false;
        if (!string.IsNullOrEmpty(request.RegisterId))
        {
            var cacheEntry = _registerCache.Get(request.RegisterId);
            if (cacheEntry != null)
            {
                var existing = cacheEntry.GetTransaction(request.TransactionHash);
                alreadyKnown = existing != null;
            }
        }

        // Also check the gossip engine's seen state
        if (!alreadyKnown)
        {
            var gossipState = _gossipEngine.GetGossipState(request.TransactionHash);
            alreadyKnown = gossipState != null;
        }

        var willRequest = false;

        if (!alreadyKnown)
        {
            // Record that we've seen this transaction hash
            _gossipEngine.RecordSeen(request.TransactionHash);

            // Queue for retrieval
            var notification = new Core.TransactionNotification
            {
                TransactionId = request.TransactionHash,
                RegisterId = request.RegisterId,
                OriginPeerId = request.SenderPeerId,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(request.Timestamp),
                DataSize = (int)request.TransactionSize,
                DataHash = request.TransactionHash,
                HasFullData = false
            };

            willRequest = await _queueManager.EnqueueAsync(notification, context.CancellationToken);

            _logger.LogDebug(
                "Transaction {TxHash} queued for retrieval: {WillRequest}",
                request.TransactionHash, willRequest);
        }
        else
        {
            _logger.LogDebug(
                "Transaction {TxHash} already known, skipping",
                request.TransactionHash);
        }

        return new NotificationAck
        {
            AlreadyKnown = alreadyKnown,
            WillRequest = willRequest
        };
    }

    /// <summary>
    /// Looks up a transaction by hash in the local register cache and returns the full data.
    /// Returns found=false if the transaction is not available locally.
    /// </summary>
    public override Task<TransactionResponse> GetTransaction(
        TransactionRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "GetTransaction request for {TxHash} from peer {PeerId} (register: {RegisterId})",
            request.TransactionHash, request.RequestingPeerId, request.RegisterId);

        CachedTransaction? cachedTx = null;

        if (!string.IsNullOrEmpty(request.RegisterId))
        {
            // Look up in the specific register's cache
            var cacheEntry = _registerCache.Get(request.RegisterId);
            cachedTx = cacheEntry?.GetTransaction(request.TransactionHash);
        }
        else
        {
            // Search across all cached registers
            foreach (var registerId in _registerCache.GetCachedRegisterIds())
            {
                var entry = _registerCache.GetOrCreate(registerId);
                cachedTx = entry.GetTransaction(request.TransactionHash);
                if (cachedTx != null)
                    break;
            }
        }

        if (cachedTx == null)
        {
            _logger.LogDebug("Transaction {TxHash} not found in local cache", request.TransactionHash);

            return Task.FromResult(new TransactionResponse
            {
                TransactionHash = request.TransactionHash,
                Found = false
            });
        }

        _logger.LogDebug(
            "Transaction {TxHash} found, returning {Size} bytes",
            request.TransactionHash, cachedTx.Data.Length);

        return Task.FromResult(new TransactionResponse
        {
            TransactionHash = request.TransactionHash,
            TransactionData = ByteString.CopyFrom(cachedTx.Data),
            Found = true
        });
    }

    /// <summary>
    /// Streams a large transaction in 64 KB chunks.
    /// Looks up the transaction in the register cache and sends it as a series of TransactionChunk messages.
    /// Returns NOT_FOUND status if the transaction is not available locally.
    /// </summary>
    public override async Task StreamTransaction(
        TransactionRequest request,
        IServerStreamWriter<TransactionChunk> responseStream,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "StreamTransaction request for {TxHash} from peer {PeerId} (register: {RegisterId})",
            request.TransactionHash, request.RequestingPeerId, request.RegisterId);

        CachedTransaction? cachedTx = null;

        if (!string.IsNullOrEmpty(request.RegisterId))
        {
            var cacheEntry = _registerCache.Get(request.RegisterId);
            cachedTx = cacheEntry?.GetTransaction(request.TransactionHash);
        }
        else
        {
            foreach (var registerId in _registerCache.GetCachedRegisterIds())
            {
                var entry = _registerCache.GetOrCreate(registerId);
                cachedTx = entry.GetTransaction(request.TransactionHash);
                if (cachedTx != null)
                    break;
            }
        }

        if (cachedTx == null)
        {
            _logger.LogDebug("Transaction {TxHash} not found for streaming", request.TransactionHash);
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Transaction {request.TransactionHash} not found in local cache"));
        }

        var data = cachedTx.Data;
        var totalChunks = (int)Math.Ceiling((double)data.Length / ChunkSize);

        _logger.LogDebug(
            "Streaming transaction {TxHash}: {Size} bytes in {Chunks} chunks",
            request.TransactionHash, data.Length, totalChunks);

        for (var i = 0; i < totalChunks; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();

            var offset = i * ChunkSize;
            var length = Math.Min(ChunkSize, data.Length - offset);

            var chunk = new TransactionChunk
            {
                TransactionHash = request.TransactionHash,
                ChunkIndex = i,
                TotalChunks = totalChunks,
                ChunkData = ByteString.CopyFrom(data, offset, length)
            };

            await responseStream.WriteAsync(chunk, context.CancellationToken);
        }

        _logger.LogDebug(
            "Completed streaming transaction {TxHash} ({Chunks} chunks sent)",
            request.TransactionHash, totalChunks);
    }

    /// <summary>
    /// Feature 108. Receives a forwarded signed submission from a NAT'd subscriber peer.
    /// Deserialises the submission JSON and hands it to the local Validator.Service mempool.
    /// If this node is on the register's roster, the normal sealing pipeline will produce a
    /// docket; otherwise the tx sits in the pool for onward gossip.
    /// </summary>
    public override async Task<SubmitTransactionResponse> SubmitTransaction(
        SubmitTransactionRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.RegisterId) || request.SubmissionJson.IsEmpty)
        {
            return new SubmitTransactionResponse
            {
                Accepted = false,
                RejectReason = "register_id or submission_json missing"
            };
        }

        try
        {
            TransactionSubmission? submission;
            try
            {
                submission = JsonSerializer.Deserialize<TransactionSubmission>(
                    request.SubmissionJson.ToByteArray(),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "SubmitTransaction: submission_json failed to deserialise for register {RegisterId}",
                    request.RegisterId);
                return new SubmitTransactionResponse
                {
                    Accepted = false,
                    RejectReason = "submission_json not deserialisable"
                };
            }

            if (submission is null)
            {
                return new SubmitTransactionResponse
                {
                    Accepted = false,
                    RejectReason = "submission_json empty"
                };
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var validatorClient = scope.ServiceProvider.GetRequiredService<IValidatorServiceClient>();

            var result = await validatorClient.SubmitTransactionAsync(submission, context.CancellationToken);

            _logger.LogInformation(
                "SubmitTransaction forwarded from peer {Origin} for register {RegisterId} → local validator ({Success})",
                request.OriginPeerId, request.RegisterId, result.Success);

            return new SubmitTransactionResponse
            {
                Accepted = result.Success,
                RejectReason = result.Success ? string.Empty : $"{result.ErrorCode}: {result.ErrorMessage}",
                // ReceiverIsValidator left at proto default (false). Honest signal: the
                // peer service forwards to its local validator-service via gRPC and reports
                // the result; whether the receiving NODE is itself on the register's roster
                // is a separate question we can't answer without consulting
                // IRegisterLocalRelationshipService.IsValidator(registerId). Wiring that
                // through is tracked as Feature 108 follow-up #1.
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SubmitTransaction from peer {Origin} for register {RegisterId} errored",
                request.OriginPeerId, request.RegisterId);
            return new SubmitTransactionResponse
            {
                Accepted = false,
                RejectReason = ex.Message
            };
        }
    }
}
