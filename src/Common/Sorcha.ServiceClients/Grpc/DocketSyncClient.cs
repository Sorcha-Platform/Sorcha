// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// gRPC client for Docket Sync Service.
/// Recovery sync — stream missing dockets and query network head.
/// </summary>
public class DocketSyncClient : IDocketSyncClient, IDisposable
{
    private readonly DocketSyncService.DocketSyncServiceClient _client;
    private readonly GrpcChannel _channel;
    private readonly ILogger<DocketSyncClient> _logger;

    public DocketSyncClient(IConfiguration configuration, ILogger<DocketSyncClient> logger)
    {
        _logger = logger;
        var address = configuration["ServiceClients:PeerService:GrpcAddress"]
            ?? configuration["ServiceClients:PeerService:Address"]
            ?? "https://localhost:7002";
        _channel = GrpcChannel.ForAddress(address);
        _client = new DocketSyncService.DocketSyncServiceClient(_channel);
    }

    /// <inheritdoc />
    public async Task<GetLatestDocketNumberResponse> GetLatestDocketNumberAsync(
        string registerId, CancellationToken ct = default)
    {
        _logger.LogDebug("Querying latest docket number for register {RegisterId}", registerId);
        return await _client.GetLatestDocketNumberAsync(
            new GetLatestDocketNumberRequest { RegisterId = registerId },
            cancellationToken: ct);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SyncDocketEntry> SyncDocketsAsync(
        string registerId, long fromDocketNumber, long toDocketNumber = 0,
        int maxCount = 0, [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting docket sync for register {RegisterId} from docket {FromDocket}",
            registerId, fromDocketNumber);

        using var call = _client.SyncDockets(new SyncDocketsRequest
        {
            RegisterId = registerId,
            FromDocketNumber = fromDocketNumber,
            ToDocketNumber = toDocketNumber,
            MaxCount = maxCount
        }, cancellationToken: ct);

        await foreach (var entry in call.ResponseStream.ReadAllAsync(ct))
        {
            yield return entry;
        }
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
