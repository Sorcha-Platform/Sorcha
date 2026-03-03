// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Peer.Service.Protos;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// Client interface for Docket Sync gRPC service.
/// Direction: Register Service → Peer Service.
/// Recovery sync — stream missing dockets and query network head.
/// </summary>
public interface IDocketSyncClient
{
    /// <summary>Get the latest docket number for a register from the peer network.</summary>
    Task<GetLatestDocketNumberResponse> GetLatestDocketNumberAsync(
        string registerId, CancellationToken ct = default);

    /// <summary>Stream dockets from a starting point to the network head.</summary>
    IAsyncEnumerable<SyncDocketEntry> SyncDocketsAsync(
        string registerId, long fromDocketNumber, long toDocketNumber = 0,
        int maxCount = 0, CancellationToken ct = default);
}
