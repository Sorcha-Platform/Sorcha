// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Models;
using Sorcha.ServiceClients.Participant;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Delivers inbound action notifications to users.
/// </summary>
/// <remarks>
/// <para>
/// Real-time path (Feature 118 / T075): resolves wallet → participant →
/// PlatformUserId, then writes a durable Action inbox entry via
/// <see cref="IPlatformInboxClient"/>. TenantHub fans out the
/// <c>InboxEntryAdded</c> signal to the user's open connections; the legacy
/// <c>wallet:notifications</c> Redis pub/sub channel is removed (pre-release —
/// no parallel-fire window needed).
/// </para>
/// <para>
/// Digest path: still queues to the Redis sorted set
/// <c>wallet:digest:{userId}</c>. <c>NotificationDigestWorker</c> drains it on
/// the digest cadence; T076 will replace the worker output with inbox digest
/// entries.
/// </para>
/// </remarks>
public sealed class NotificationDeliveryService : INotificationDeliveryService
{
    private const string DigestKeyPrefix = "wallet:digest:";
    private const string DigestActiveUsersKey = "wallet:digest:active-users";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IWalletRepository _walletRepository;
    private readonly INotificationRateLimiter _rateLimiter;
    private readonly INotificationPreferenceProvider _preferenceProvider;
    private readonly NotificationMetrics _metrics;
    private readonly IConnectionMultiplexer _redis;
    private readonly IParticipantServiceClient _participants;
    private readonly IPlatformInboxClient _inbox;
    private readonly IInboundCredentialDetector? _credentialDetector;
    private readonly IInboundCredentialStatusHandler? _credentialStatusHandler;
    private readonly ILogger<NotificationDeliveryService> _logger;

    /// <summary>Initialises a new <see cref="NotificationDeliveryService"/>.</summary>
    public NotificationDeliveryService(
        IWalletRepository walletRepository,
        INotificationRateLimiter rateLimiter,
        INotificationPreferenceProvider preferenceProvider,
        NotificationMetrics metrics,
        IConnectionMultiplexer redis,
        IParticipantServiceClient participants,
        IPlatformInboxClient inbox,
        ILogger<NotificationDeliveryService> logger,
        IInboundCredentialDetector? credentialDetector = null,
        IInboundCredentialStatusHandler? credentialStatusHandler = null)
    {
        _walletRepository = walletRepository;
        _rateLimiter = rateLimiter;
        _preferenceProvider = preferenceProvider;
        _metrics = metrics;
        _redis = redis;
        _participants = participants;
        _inbox = inbox;
        _credentialDetector = credentialDetector;
        _credentialStatusHandler = credentialStatusHandler;
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
            _metrics.RecordNoUserFound();
            return NotificationDeliveryResult.NoUserFound;
        }

        var userId = wallet.Owner;
        var tenantId = wallet.Tenant;

        // Step 2b: Feature 106 — inbound credential detection.
        // Runs BEFORE preference check so holders always get their pending credential
        // persisted to the local wallet store regardless of notification preferences.
        // The detector is idempotent (dedup by credential id), never throws, and returns
        // a non-null extract only when a register-native credential was just persisted.
        InboundCredentialExtract? credentialExtract = null;
        if (_credentialDetector is not null)
        {
            try
            {
                credentialExtract = await _credentialDetector.TryExtractAsync(
                    recipientAddress, transactionId, registerId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The detector is contracted to never throw — catch-all defends against
                // implementation drift so a detector bug never breaks notification delivery.
                _logger.LogError(ex,
                    "InboundCredentialDetector threw for wallet {Wallet} tx {TxId} — delivery will proceed without credential enrichment",
                    recipientAddress, transactionId);
            }
        }

        // Step 2c: Multi-node audit CRITICAL #2 — apply inbound CredentialStatusChange
        // transactions to the holder's locally cached credential row. Runs alongside the
        // F106 detector; the handler is contracted to never throw and to no-op on any
        // non-CredentialStatusChange tx.
        if (_credentialStatusHandler is not null)
        {
            try
            {
                await _credentialStatusHandler.TryApplyAsync(
                    recipientAddress, transactionId, registerId, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "InboundCredentialStatusHandler threw for wallet {Wallet} tx {TxId} — delivery will proceed without status update",
                    recipientAddress, transactionId);
            }
        }

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

        // Build the event (still serialized into the digest sorted set; digest path migrates in T076)
        var actionEvent = new InboundActionEvent
        {
            WalletAddress = recipientAddress,
            WalletId = Guid.Empty, // Wallet entity uses string Address as PK, not a Guid
            UserId = userId,
            TenantId = tenantId,
            BlueprintId = blueprintId ?? credentialExtract?.BlueprintId,
            InstanceId = instanceId ?? credentialExtract?.InstanceId,
            ActionId = actionId,
            NextActionId = nextActionId,
            SenderAddress = senderAddress,
            TransactionId = transactionId,
            RegisterId = registerId,
            DocketNumber = docketNumber,
            Timestamp = timestamp,
            IsRecoveryEvent = isRecovery,
            CredentialOfferId = credentialExtract?.CredentialId,
        };

        // Step 3: Route based on preference and rate limit
        if (!prefs.IsRealTime)
        {
            // User prefers digest — queue directly
            await QueueForDigestAsync(userId, actionEvent);
            _metrics.RecordQueuedForDigest();
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
            _metrics.RecordRateLimited();
            _logger.LogInformation(
                "Rate-limited notification for user {UserId}, overflow to digest. Tx {TxId}",
                userId, transactionId);
            return NotificationDeliveryResult.RateLimited;
        }

        // Deliver real-time via the durable Tenant inbox.
        // Failure here returns NoUserFound rather than throwing — an inbox-write
        // outage must not poison the inbound-transaction pipeline. The signal
        // surfaces via metrics + WARN logs.
        var written = await TryWriteInboxEntryAsync(
            recipientAddress, actionEvent, cancellationToken).ConfigureAwait(false);

        if (!written)
        {
            _metrics.RecordNoUserFound();
            return NotificationDeliveryResult.NoUserFound;
        }

        _metrics.RecordDeliveredRealTime();
        _metrics.RecordDeliveryLatency((DateTimeOffset.UtcNow - timestamp).TotalMilliseconds);
        _logger.LogDebug(
            "Real-time inbox entry written for user {UserId}, tx {TxId}",
            userId, transactionId);
        return NotificationDeliveryResult.DeliveredRealTime;
    }

    private async Task<bool> TryWriteInboxEntryAsync(
        string recipientAddress,
        InboundActionEvent actionEvent,
        CancellationToken ct)
    {
        try
        {
            var participant = await _participants.GetByWalletAddressAsync(recipientAddress, ct).ConfigureAwait(false);
            if (participant is null)
            {
                _logger.LogDebug(
                    "Inbox skip — no participant for recipient wallet {Wallet}",
                    recipientAddress);
                return false;
            }

            var platformUserId = await _inbox.ResolvePlatformUserIdAsync(participant.UserId, ct).ConfigureAwait(false);
            if (platformUserId is null)
            {
                _logger.LogDebug(
                    "Inbox skip — could not resolve PlatformUserId for UserIdentity {UserIdentityId}",
                    participant.UserId);
                return false;
            }

            var sourceEventId = DeterministicSourceEventId(recipientAddress, actionEvent.TransactionId);
            var detailHref = BuildDetailHref(actionEvent);
            var title = actionEvent.IsRecoveryEvent
                ? "Recovered action requires your attention"
                : "Action required";

            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId.Value,
                Category: "Action",
                Severity: "ActionRequired",
                CorrelationKey: $"tx:{recipientAddress}:{actionEvent.TransactionId}",
                DetailHref: detailHref,
                SourceEventId: sourceEventId,
                OccurredAt: actionEvent.Timestamp,
                Title: title,
                Summary: null,
                IconKey: actionEvent.CredentialOfferId is null ? "action.required" : "credential.received");

            var outcome = await _inbox.WriteAsync(payload, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Inbox entry {Outcome} for inbound action — RecipientWallet={Wallet} TxId={TxId} EntryId={EntryId}",
                outcome.Idempotent ? "idempotent" : "created",
                recipientAddress, actionEvent.TransactionId, outcome.EntryId);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Inbox-write failed for inbound action — RecipientWallet={Wallet} TxId={TxId}",
                recipientAddress, actionEvent.TransactionId);
            return false;
        }
    }

    private static string BuildDetailHref(InboundActionEvent actionEvent)
    {
        if (!string.IsNullOrWhiteSpace(actionEvent.InstanceId))
        {
            return $"/api/instances/{actionEvent.InstanceId}/actions/{actionEvent.ActionId}";
        }
        return "/api/me/inbox";
    }

    private static Guid DeterministicSourceEventId(string recipientWalletAddress, string transactionId)
    {
        var input = $"sorcha.inbox.action-required:{recipientWalletAddress}:{transactionId}";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private async Task QueueForDigestAsync(string userId, InboundActionEvent actionEvent)
    {
        var db = _redis.GetDatabase();
        var key = $"{DigestKeyPrefix}{userId}";
        var json = JsonSerializer.Serialize(actionEvent, JsonOptions);
        var score = actionEvent.Timestamp.ToUnixTimeMilliseconds();
        await db.SortedSetAddAsync(key, json, score);
        await db.SetAddAsync(DigestActiveUsersKey, userId);
    }
}
