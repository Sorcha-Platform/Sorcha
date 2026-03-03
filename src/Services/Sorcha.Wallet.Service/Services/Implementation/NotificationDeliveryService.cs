// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Models;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Delivers inbound action notifications to users.
/// Resolves wallet address → wallet → user, checks notification preferences,
/// publishes real-time events via Redis pub/sub (wallet:notifications channel)
/// for the EventsHub bridge, or queues to Redis sorted set (wallet:digest:{userId}) for digest batching.
/// </summary>
public sealed class NotificationDeliveryService : INotificationDeliveryService
{
    private const string PubSubChannel = "wallet:notifications";
    private const string DigestKeyPrefix = "wallet:digest:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IWalletRepository _walletRepository;
    private readonly INotificationRateLimiter _rateLimiter;
    private readonly INotificationPreferenceProvider _preferenceProvider;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<NotificationDeliveryService> _logger;

    public NotificationDeliveryService(
        IWalletRepository walletRepository,
        INotificationRateLimiter rateLimiter,
        INotificationPreferenceProvider preferenceProvider,
        IConnectionMultiplexer redis,
        ILogger<NotificationDeliveryService> logger)
    {
        _walletRepository = walletRepository;
        _rateLimiter = rateLimiter;
        _preferenceProvider = preferenceProvider;
        _redis = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<NotificationDeliveryResult> DeliverAsync(
        string recipientAddress,
        string transactionId,
        string registerId,
        long docketNumber,
        string? blueprintId,
        string? instanceId,
        uint actionId,
        uint nextActionId,
        string? senderAddress,
        DateTimeOffset timestamp,
        bool isRecovery,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Resolve address → wallet → user
        var wallet = await _walletRepository.GetByAddressAsync(
            recipientAddress, cancellationToken: cancellationToken);

        if (wallet is null)
        {
            _logger.LogDebug(
                "No wallet found for address {Address} — bloom filter false positive or deleted wallet",
                recipientAddress);
            return NotificationDeliveryResult.NoUserFound;
        }

        var userId = wallet.Owner;
        var tenantId = wallet.Tenant;

        // Step 2: Check notification preferences
        var prefs = await _preferenceProvider.GetPreferencesAsync(userId, cancellationToken);

        if (!prefs.NotificationsEnabled)
        {
            _logger.LogDebug("Notifications disabled for user {UserId}", userId);
            return NotificationDeliveryResult.NoUserFound;
        }

        // Log warning for email/push preference without transport
        if (prefs.WantsEmail)
        {
            _logger.LogWarning(
                "User {UserId} has email notifications configured but email transport is not available. Delivering in-app only.",
                userId);
        }
        if (prefs.WantsPush)
        {
            _logger.LogWarning(
                "User {UserId} has push notifications configured but push transport is not available. Delivering in-app only.",
                userId);
        }

        // Build the event
        var actionEvent = new InboundActionEvent
        {
            WalletAddress = recipientAddress,
            WalletId = Guid.TryParse(wallet.Address, out _) ? Guid.Empty : Guid.Empty,
            UserId = userId,
            TenantId = tenantId,
            BlueprintId = blueprintId,
            InstanceId = instanceId,
            ActionId = actionId,
            NextActionId = nextActionId,
            SenderAddress = senderAddress,
            TransactionId = transactionId,
            RegisterId = registerId,
            DocketNumber = docketNumber,
            Timestamp = timestamp,
            IsRecoveryEvent = isRecovery
        };

        // Step 3: Route based on preference and rate limit
        if (!prefs.IsRealTime)
        {
            // User prefers digest — queue directly
            await QueueForDigestAsync(userId, actionEvent);
            _logger.LogDebug(
                "Digest-queued notification for user {UserId}, tx {TxId}",
                userId, transactionId);
            return NotificationDeliveryResult.QueuedForDigest;
        }

        // Real-time path — check rate limit
        var allowed = await _rateLimiter.TryAcquireAsync(userId, cancellationToken);

        if (!allowed)
        {
            // Rate-limited — overflow to digest
            await QueueForDigestAsync(userId, actionEvent);
            _logger.LogInformation(
                "Rate-limited notification for user {UserId}, overflow to digest. Tx {TxId}",
                userId, transactionId);
            return NotificationDeliveryResult.RateLimited;
        }

        // Deliver real-time via Redis pub/sub
        await PublishRealTimeAsync(actionEvent);
        _logger.LogDebug(
            "Real-time notification published for user {UserId}, tx {TxId}",
            userId, transactionId);
        return NotificationDeliveryResult.DeliveredRealTime;
    }

    private async Task PublishRealTimeAsync(InboundActionEvent actionEvent)
    {
        var subscriber = _redis.GetSubscriber();
        var json = JsonSerializer.Serialize(actionEvent, JsonOptions);
        await subscriber.PublishAsync(RedisChannel.Literal(PubSubChannel), json);
    }

    private async Task QueueForDigestAsync(string userId, InboundActionEvent actionEvent)
    {
        var db = _redis.GetDatabase();
        var key = $"{DigestKeyPrefix}{userId}";
        var json = JsonSerializer.Serialize(actionEvent, JsonOptions);
        var score = actionEvent.Timestamp.ToUnixTimeMilliseconds();
        await db.SortedSetAddAsync(key, json, score);
    }
}
