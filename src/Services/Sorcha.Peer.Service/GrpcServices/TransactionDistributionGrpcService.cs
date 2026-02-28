// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Distribution;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;

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

    /// <summary>
    /// Chunk size for streaming large transactions (64 KB).
    /// </summary>
    private const int ChunkSize = 64 * 1024;

    public TransactionDistributionGrpcService(
        ILogger<TransactionDistributionGrpcService> logger,
        GossipProtocolEngine gossipEngine,
        TransactionQueueManager queueManager,
        RegisterCache registerCache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _gossipEngine = gossipEngine ?? throw new ArgumentNullException(nameof(gossipEngine));
        _queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
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
}
