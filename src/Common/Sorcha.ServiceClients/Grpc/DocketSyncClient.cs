// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// gRPC client for Docket Sync Service.
/// Recovery sync — stream missing dockets and query network head.
/// Uses GrpcClientFactory for Aspire service discovery and HTTP handler pooling.
/// </summary>
public class DocketSyncClient : IDocketSyncClient
{
    internal const string ClientName = "DocketSync";

    private readonly GrpcClientFactory _clientFactory;
    private readonly ILogger<DocketSyncClient> _logger;

    public DocketSyncClient(GrpcClientFactory clientFactory, ILogger<DocketSyncClient> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GetLatestDocketNumberResponse> GetLatestDocketNumberAsync(
        string registerId, CancellationToken ct = default)
    {
        _logger.LogDebug("Querying latest docket number for register {RegisterId}", registerId);
        var client = _clientFactory.CreateClient<DocketSyncService.DocketSyncServiceClient>(ClientName);
        return await client.GetLatestDocketNumberAsync(
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

        var client = _clientFactory.CreateClient<DocketSyncService.DocketSyncServiceClient>(ClientName);
        using var call = client.SyncDockets(new SyncDocketsRequest
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
}
