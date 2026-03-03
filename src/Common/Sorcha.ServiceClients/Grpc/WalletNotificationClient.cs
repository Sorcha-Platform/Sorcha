// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Grpc;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// gRPC client for Wallet Notification Service.
/// Resolves addresses to users and delivers inbound action notifications.
/// Uses GrpcClientFactory for Aspire service discovery and HTTP handler pooling.
/// </summary>
public class WalletNotificationClient : IWalletNotificationClient
{
    internal const string ClientName = "WalletNotification";

    private readonly GrpcClientFactory _clientFactory;
    private readonly ILogger<WalletNotificationClient> _logger;

    public WalletNotificationClient(GrpcClientFactory clientFactory, ILogger<WalletNotificationClient> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LocalAddressEntry> GetAllLocalAddressesAsync(
        string registerId, bool activeOnly = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogDebug("Streaming all local addresses for register {RegisterId}", registerId);
        var client = _clientFactory.CreateClient<WalletNotificationService.WalletNotificationServiceClient>(ClientName);
        using var call = client.GetAllLocalAddresses(
            new GetAllLocalAddressesRequest { RegisterId = registerId, ActiveOnly = activeOnly },
            cancellationToken: ct);

        await foreach (var entry in call.ResponseStream.ReadAllAsync(ct))
        {
            yield return entry;
        }
    }

    /// <inheritdoc />
    public async Task<NotifyInboundTransactionResponse> NotifyInboundTransactionAsync(
        NotifyInboundTransactionRequest request, CancellationToken ct = default)
    {
        _logger.LogDebug("Notifying inbound transaction {TransactionId} for address {Address}",
            request.TransactionId, request.RecipientAddress);
        var client = _clientFactory.CreateClient<WalletNotificationService.WalletNotificationServiceClient>(ClientName);
        return await client.NotifyInboundTransactionAsync(request, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<NotifyInboundTransactionBatchResponse> NotifyInboundTransactionBatchAsync(
        NotifyInboundTransactionBatchRequest request, CancellationToken ct = default)
    {
        _logger.LogDebug("Notifying batch of {Count} inbound transactions", request.Transactions.Count);
        var client = _clientFactory.CreateClient<WalletNotificationService.WalletNotificationServiceClient>(ClientName);
        return await client.NotifyInboundTransactionBatchAsync(request, cancellationToken: ct);
    }
}
