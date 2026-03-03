// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Sorcha.Wallet.Service.Grpc;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// Client interface for Wallet Notification gRPC service.
/// Direction: Register Service → Wallet Service.
/// Resolves addresses to users and delivers inbound action notifications.
/// </summary>
public interface IWalletNotificationClient
{
    /// <summary>Stream all local wallet addresses for bloom filter rebuild.</summary>
    IAsyncEnumerable<LocalAddressEntry> GetAllLocalAddressesAsync(
        string registerId, bool activeOnly = true, CancellationToken ct = default);

    /// <summary>Notify of a single inbound transaction matching a local address.</summary>
    Task<NotifyInboundTransactionResponse> NotifyInboundTransactionAsync(
        NotifyInboundTransactionRequest request, CancellationToken ct = default);

    /// <summary>Batch notify for multiple matched transactions (used during recovery).</summary>
    Task<NotifyInboundTransactionBatchResponse> NotifyInboundTransactionBatchAsync(
        NotifyInboundTransactionBatchRequest request, CancellationToken ct = default);
}
