// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Services.Interfaces;
using Sorcha.ServiceClients.Grpc;
using Sorcha.Wallet.Service.Grpc;

namespace Sorcha.Register.Service.Services.Implementation;

/// <summary>
/// Routes inbound transactions by checking recipient addresses against the local bloom filter.
/// Only action-type transactions are processed; all other types are skipped.
/// </summary>
public sealed class InboundTransactionRouter : IInboundTransactionRouter
{
    private readonly ILocalAddressIndex _addressIndex;
    private readonly IWalletNotificationClient _walletNotificationClient;
    private readonly ILogger<InboundTransactionRouter> _logger;

    public InboundTransactionRouter(
        ILocalAddressIndex addressIndex,
        IWalletNotificationClient walletNotificationClient,
        ILogger<InboundTransactionRouter> logger)
    {
        _addressIndex = addressIndex;
        _walletNotificationClient = walletNotificationClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> RouteTransactionAsync(
        string registerId,
        string transactionId,
        TransactionType transactionType,
        IReadOnlyList<string> recipientAddresses,
        string? senderAddress,
        TransactionMetaData? metadata,
        long docketNumber,
        bool isRecovery = false,
        CancellationToken cancellationToken = default)
    {
        // Only route action-type transactions
        if (transactionType != TransactionType.Action)
        {
            _logger.LogDebug(
                "Skipping non-action transaction {TxId} (type: {Type})",
                transactionId, transactionType);
            return 0;
        }

        if (recipientAddresses.Count == 0)
        {
            _logger.LogDebug("Transaction {TxId} has no recipient addresses", transactionId);
            return 0;
        }

        var matchCount = 0;

        foreach (var address in recipientAddresses)
        {
            if (string.IsNullOrEmpty(address))
                continue;

            var isLocal = await _addressIndex.MayContainAsync(registerId, address, cancellationToken);

            if (!isLocal)
                continue;

            _logger.LogInformation(
                "Bloom filter match for address {Address} in transaction {TxId} (recovery: {IsRecovery})",
                address, transactionId, isRecovery);

            try
            {
                var request = new NotifyInboundTransactionRequest
                {
                    RecipientAddress = address,
                    TransactionId = transactionId,
                    RegisterId = registerId,
                    DocketNumber = docketNumber,
                    BlueprintId = metadata?.BlueprintId ?? string.Empty,
                    InstanceId = metadata?.InstanceId ?? string.Empty,
                    ActionId = metadata?.ActionId ?? 0,
                    NextActionId = metadata?.NextActionId ?? 0,
                    SenderAddress = senderAddress ?? string.Empty,
                    Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
                    IsRecovery = isRecovery
                };

                var response = await _walletNotificationClient.NotifyInboundTransactionAsync(
                    request, cancellationToken);

                if (response.Success)
                {
                    matchCount++;
                    _logger.LogDebug(
                        "Notification delivered for {Address}: {Delivery}",
                        address, response.Delivery);
                }
                else
                {
                    _logger.LogWarning(
                        "Notification failed for {Address}: {Message}",
                        address, response.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to notify Wallet Service for address {Address} in transaction {TxId}",
                    address, transactionId);
            }
        }

        return matchCount;
    }
}
