// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Runtime.CompilerServices;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Grpc;

namespace Sorcha.ServiceClients.Grpc;

/// <summary>
/// gRPC client for Wallet Notification Service.
/// Resolves addresses to users and delivers inbound action notifications.
/// </summary>
public class WalletNotificationClient : IWalletNotificationClient, IDisposable
{
    private readonly WalletNotificationService.WalletNotificationServiceClient _client;
    private readonly GrpcChannel _channel;
    private readonly ILogger<WalletNotificationClient> _logger;

    public WalletNotificationClient(IConfiguration configuration, ILogger<WalletNotificationClient> logger)
    {
        _logger = logger;
        var address = configuration["ServiceClients:WalletService:GrpcAddress"]
            ?? configuration["ServiceClients:WalletService:Address"]
            ?? "https://localhost:7001";
        _channel = GrpcChannel.ForAddress(address);
        _client = new WalletNotificationService.WalletNotificationServiceClient(_channel);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LocalAddressEntry> GetAllLocalAddressesAsync(
        string registerId, bool activeOnly = true,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogDebug("Streaming all local addresses for register {RegisterId}", registerId);
        using var call = _client.GetAllLocalAddresses(
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
        return await _client.NotifyInboundTransactionAsync(request, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<NotifyInboundTransactionBatchResponse> NotifyInboundTransactionBatchAsync(
        NotifyInboundTransactionBatchRequest request, CancellationToken ct = default)
    {
        _logger.LogDebug("Notifying batch of {Count} inbound transactions", request.Transactions.Count);
        return await _client.NotifyInboundTransactionBatchAsync(request, cancellationToken: ct);
    }

    public void Dispose()
    {
        _channel.Dispose();
    }
}
