// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Grpc;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.GrpcServices;

/// <summary>
/// gRPC service implementation for Wallet Notification Service integration.
/// </summary>
/// <remarks>
/// <para>
/// This service provides gRPC endpoints for resolving local wallet addresses and
/// delivering inbound transaction notifications. It is called by the Register Service
/// when bloom filter matches are found against registered wallet addresses.
/// </para>
///
/// <para><b>Key Operations:</b></para>
/// <list type="bullet">
///   <item>GetAllLocalAddresses — Stream all local wallet addresses for bloom filter rebuild (Phase 3 US1)</item>
///   <item>NotifyInboundTransaction — Notify of an inbound transaction match (Phase 4 US2, not yet implemented)</item>
///   <item>NotifyInboundTransactionBatch — Batch notify for recovery processing (Phase 4 US2, not yet implemented)</item>
/// </list>
///
/// <para><b>Security:</b></para>
/// <list type="bullet">
///   <item>Service-to-service authentication required (Register Service caller)</item>
///   <item>Only address strings are streamed — no private keys or sensitive wallet internals</item>
///   <item>All streaming operations logged for audit trail</item>
/// </list>
///
/// <para><b>Related Requirements:</b></para>
/// <list type="bullet">
///   <item>T015: Implement GetAllLocalAddresses server-streaming endpoint (Phase 3 US1)</item>
///   <item>T016: Register Service bloom filter rebuild using streamed addresses</item>
/// </list>
/// </remarks>
public class WalletNotificationGrpcService : WalletNotificationService.WalletNotificationServiceBase
{
    private const int PageSize = 100;

    private readonly IWalletRepository _repository;
    private readonly INotificationDeliveryService _deliveryService;
    private readonly ILogger<WalletNotificationGrpcService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WalletNotificationGrpcService"/> class.
    /// </summary>
    /// <param name="repository">Wallet repository for data access.</param>
    /// <param name="deliveryService">Notification delivery service for routing inbound action notifications.</param>
    /// <param name="logger">Logger instance.</param>
    public WalletNotificationGrpcService(
        IWalletRepository repository,
        INotificationDeliveryService deliveryService,
        ILogger<WalletNotificationGrpcService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _deliveryService = deliveryService ?? throw new ArgumentNullException(nameof(deliveryService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Streams all local wallet addresses to the caller for bloom filter rebuild.
    /// </summary>
    /// <param name="request">
    /// Request specifying optional filter criteria: <c>register_id</c> (unused in Phase 3)
    /// and <c>active_only</c> to restrict results to active wallets only.
    /// </param>
    /// <param name="responseStream">Server-streaming response channel.</param>
    /// <param name="context">gRPC server call context.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous streaming operation.</returns>
    /// <remarks>
    /// <para>
    /// For each wallet, both the wallet's primary address and all BIP44-derived child
    /// addresses in its <c>Addresses</c> collection are streamed as separate
    /// <see cref="LocalAddressEntry"/> messages. This ensures the bloom filter captures
    /// the full address space managed by this Wallet Service instance.
    /// </para>
    ///
    /// <para>
    /// Wallets are fetched in pages of <c>100</c> using <see cref="IWalletRepository.GetByTenantAsync"/>.
    /// An empty string is used as the tenant discriminator to retrieve wallets across all
    /// tenants — callers should ensure this behaviour is supported by the active repository
    /// implementation. Paging continues until a page returns fewer entries than the page size.
    /// </para>
    ///
    /// <para><b>active_only behaviour:</b></para>
    /// <list type="bullet">
    ///   <item>When <c>true</c>: only wallets with <see cref="WalletStatus.Active"/> are included.</item>
    ///   <item>When <c>false</c>: wallets of any status (including Archived, Locked, Deleted) are included.</item>
    /// </list>
    ///
    /// <para><b>Error Handling:</b></para>
    /// <list type="bullet">
    ///   <item>Unavailable — Repository query failure (e.g., database connection error).</item>
    /// </list>
    /// </remarks>
    public override async Task GetAllLocalAddresses(
        GetAllLocalAddressesRequest request,
        IServerStreamWriter<LocalAddressEntry> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "GetAllLocalAddresses called — active_only: {ActiveOnly}, register_id: {RegisterId}",
            request.ActiveOnly,
            string.IsNullOrWhiteSpace(request.RegisterId) ? "(all)" : request.RegisterId);

        var streamed = 0;
        var skip = 0;

        try
        {
            while (!context.CancellationToken.IsCancellationRequested)
            {
                IEnumerable<Core.Domain.Entities.Wallet> page;

                try
                {
                    page = await _repository.GetByTenantAsync(
                        tenant: string.Empty,
                        skip: skip,
                        take: PageSize,
                        cancellationToken: context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve wallet page at skip={Skip}", skip);
                    throw new RpcException(new Status(StatusCode.Unavailable,
                        "Failed to retrieve wallets from repository"));
                }

                var wallets = page.ToList();
                if (wallets.Count == 0)
                    break;

                foreach (var wallet in wallets)
                {
                    if (context.CancellationToken.IsCancellationRequested)
                        break;

                    var isActive = wallet.Status == WalletStatus.Active;

                    if (request.ActiveOnly && !isActive)
                        continue;

                    // Stream the wallet's primary address.
                    await responseStream.WriteAsync(new LocalAddressEntry
                    {
                        Address = wallet.Address,
                        WalletId = wallet.Address,
                        UserId = wallet.Owner,
                        IsActive = isActive
                    }, context.CancellationToken);

                    streamed++;

                    // Stream all BIP44-derived child addresses registered under this wallet.
                    foreach (var derivedAddress in wallet.Addresses)
                    {
                        if (context.CancellationToken.IsCancellationRequested)
                            break;

                        await responseStream.WriteAsync(new LocalAddressEntry
                        {
                            Address = derivedAddress.Address,
                            WalletId = wallet.Address,
                            UserId = wallet.Owner,
                            IsActive = isActive
                        }, context.CancellationToken);

                        streamed++;
                    }
                }

                // If the page was smaller than the page size, we have reached the end.
                if (wallets.Count < PageSize)
                    break;

                skip += PageSize;
            }
        }
        catch (RpcException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "GetAllLocalAddresses cancelled by caller after streaming {Count} address entries",
                streamed);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during GetAllLocalAddresses after {Count} entries", streamed);
            throw new RpcException(new Status(StatusCode.Internal,
                "Unexpected error while streaming local addresses"));
        }

        _logger.LogInformation(
            "GetAllLocalAddresses completed — streamed {Count} address entries (active_only: {ActiveOnly})",
            streamed,
            request.ActiveOnly);
    }

    /// <summary>
    /// Notifies the Wallet Service of a single inbound transaction matching a local address.
    /// Resolves the recipient address to a wallet and user, checks notification preferences,
    /// and delivers via the appropriate channel (real-time SignalR or digest queue).
    /// </summary>
    /// <param name="request">Inbound transaction notification request with recipient address and metadata.</param>
    /// <param name="context">gRPC server call context.</param>
    /// <returns>Notification delivery response indicating success and delivery method.</returns>
    public override async Task<NotifyInboundTransactionResponse> NotifyInboundTransaction(
        NotifyInboundTransactionRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "NotifyInboundTransaction called for address {Address}, tx {TxId}",
            request.RecipientAddress, request.TransactionId);

        try
        {
            var result = await _deliveryService.DeliverAsync(
                recipientAddress: request.RecipientAddress,
                transactionId: request.TransactionId,
                registerId: request.RegisterId,
                docketNumber: request.DocketNumber,
                blueprintId: request.BlueprintId,
                instanceId: request.InstanceId,
                actionId: request.ActionId,
                nextActionId: request.NextActionId,
                senderAddress: request.SenderAddress,
                timestamp: request.Timestamp?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                isRecovery: request.IsRecovery,
                cancellationToken: context.CancellationToken);

            return new NotifyInboundTransactionResponse
            {
                Success = result != NotificationDeliveryResult.NoUserFound,
                Delivery = MapDeliveryResult(result),
                Message = result.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to deliver notification for address {Address}, tx {TxId}",
                request.RecipientAddress, request.TransactionId);
            throw new RpcException(new Status(StatusCode.Internal,
                $"Notification delivery failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Batch-notifies the Wallet Service of multiple inbound transactions during recovery.
    /// Processes each transaction sequentially and returns aggregate delivery counts.
    /// </summary>
    /// <param name="request">Batch notification request containing multiple transactions.</param>
    /// <param name="context">gRPC server call context.</param>
    /// <returns>Aggregate delivery summary with counts per delivery outcome.</returns>
    public override async Task<NotifyInboundTransactionBatchResponse> NotifyInboundTransactionBatch(
        NotifyInboundTransactionBatchRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation(
            "NotifyInboundTransactionBatch called with {Count} transactions",
            request.Transactions.Count);

        var response = new NotifyInboundTransactionBatchResponse
        {
            Total = request.Transactions.Count
        };

        foreach (var tx in request.Transactions)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            try
            {
                var result = await _deliveryService.DeliverAsync(
                    recipientAddress: tx.RecipientAddress,
                    transactionId: tx.TransactionId,
                    registerId: tx.RegisterId,
                    docketNumber: tx.DocketNumber,
                    blueprintId: tx.BlueprintId,
                    instanceId: tx.InstanceId,
                    actionId: tx.ActionId,
                    nextActionId: tx.NextActionId,
                    senderAddress: tx.SenderAddress,
                    timestamp: tx.Timestamp?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                    isRecovery: tx.IsRecovery,
                    cancellationToken: context.CancellationToken);

                switch (result)
                {
                    case NotificationDeliveryResult.DeliveredRealTime:
                        response.DeliveredRealTime++;
                        break;
                    case NotificationDeliveryResult.QueuedForDigest:
                        response.QueuedForDigest++;
                        break;
                    case NotificationDeliveryResult.RateLimited:
                        response.RateLimited++;
                        break;
                    case NotificationDeliveryResult.NoUserFound:
                        response.NoUserFound++;
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to deliver batch notification for address {Address}, tx {TxId}",
                    tx.RecipientAddress, tx.TransactionId);
                response.Errors.Add($"tx {tx.TransactionId}: {ex.Message}");
            }
        }

        _logger.LogInformation(
            "Batch complete: {Total} total, {RealTime} real-time, {Digest} digest, {Limited} rate-limited, {NoUser} no-user, {Errors} errors",
            response.Total, response.DeliveredRealTime, response.QueuedForDigest,
            response.RateLimited, response.NoUserFound, response.Errors.Count);

        return response;
    }

    private static NotificationDelivery MapDeliveryResult(NotificationDeliveryResult result) => result switch
    {
        NotificationDeliveryResult.DeliveredRealTime => NotificationDelivery.RealTime,
        NotificationDeliveryResult.QueuedForDigest => NotificationDelivery.DigestQueued,
        NotificationDeliveryResult.RateLimited => NotificationDelivery.RateLimited,
        NotificationDeliveryResult.NoUserFound => NotificationDelivery.NoUser,
        _ => NotificationDelivery.Unspecified
    };
}
