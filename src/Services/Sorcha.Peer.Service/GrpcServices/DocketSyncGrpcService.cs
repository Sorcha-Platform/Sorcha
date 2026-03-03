// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.GrpcServices;

/// <summary>
/// gRPC service for docket synchronization during recovery mode.
/// Called by Register Service when it detects a gap between local state and network head.
/// </summary>
public class DocketSyncGrpcService : DocketSyncService.DocketSyncServiceBase
{
    private readonly RegisterCache _registerCache;
    private readonly ILogger<DocketSyncGrpcService> _logger;

    public DocketSyncGrpcService(
        RegisterCache registerCache,
        ILogger<DocketSyncGrpcService> logger)
    {
        _registerCache = registerCache ?? throw new ArgumentNullException(nameof(registerCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns the latest docket number for a register from the local cache.
    /// Used by Register Service to detect if it needs recovery.
    /// </summary>
    public override Task<GetLatestDocketNumberResponse> GetLatestDocketNumber(
        GetLatestDocketNumberRequest request,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "GetLatestDocketNumber requested for register {RegisterId}",
            request.RegisterId);

        var cacheEntry = _registerCache.Get(request.RegisterId);

        if (cacheEntry == null)
        {
            _logger.LogDebug(
                "Register {RegisterId} not found in cache, returning 0",
                request.RegisterId);

            return Task.FromResult(new GetLatestDocketNumberResponse
            {
                LatestDocketNumber = 0,
                SourcePeerId = Environment.MachineName,
                QueriedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                NetworkAvailable = true
            });
        }

        var latestDocket = cacheEntry.GetLatestDocketVersion();
        _logger.LogDebug(
            "Register {RegisterId} latest docket: {LatestDocket}",
            request.RegisterId, latestDocket);

        return Task.FromResult(new GetLatestDocketNumberResponse
        {
            LatestDocketNumber = latestDocket,
            SourcePeerId = Environment.MachineName,
            QueriedAt = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            NetworkAvailable = true
        });
    }

    /// <summary>
    /// Streams dockets from from_docket_number+1 to head (or to_docket_number).
    /// Supports max_count for flow control.
    /// </summary>
    public override async Task SyncDockets(
        SyncDocketsRequest request,
        IServerStreamWriter<SyncDocketEntry> responseStream,
        ServerCallContext context)
    {
        _logger.LogDebug(
            "SyncDockets requested for register {RegisterId} from docket {FromDocket} to {ToDocket} (max {MaxCount})",
            request.RegisterId, request.FromDocketNumber, request.ToDocketNumber, request.MaxCount);

        var cacheEntry = _registerCache.Get(request.RegisterId);
        if (cacheEntry == null)
        {
            _logger.LogDebug(
                "Register {RegisterId} not found in cache, no dockets to stream",
                request.RegisterId);
            return;
        }

        var fromDocket = request.FromDocketNumber;
        var toDocket = request.ToDocketNumber > 0
            ? request.ToDocketNumber
            : cacheEntry.GetLatestDocketVersion();
        var maxCount = request.MaxCount > 0 ? request.MaxCount : int.MaxValue;
        var streamed = 0;

        for (var docketNumber = fromDocket + 1; docketNumber <= toDocket; docketNumber++)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            if (streamed >= maxCount)
                break;

            var docket = cacheEntry.GetDocket(docketNumber);
            if (docket == null)
            {
                _logger.LogWarning(
                    "Docket {DocketNumber} not found in cache for register {RegisterId}, stopping stream",
                    docketNumber, request.RegisterId);
                break;
            }

            await responseStream.WriteAsync(new SyncDocketEntry
            {
                DocketNumber = docket.Version,
                DocketHash = docket.DocketHash,
                PreviousDocketHash = docket.PreviousHash ?? string.Empty,
                MerkleRoot = string.Empty,
                DocketData = Google.Protobuf.ByteString.CopyFrom(docket.Data),
                TransactionCount = docket.TransactionIds.Count,
                SealedAt = Timestamp.FromDateTimeOffset(docket.CreatedAt),
                ValidatorSignature = string.Empty,
                ValidatorId = string.Empty
            }, context.CancellationToken);

            streamed++;
        }

        _logger.LogDebug(
            "SyncDockets completed for register {RegisterId}: streamed {Count} dockets",
            request.RegisterId, streamed);
    }
}
