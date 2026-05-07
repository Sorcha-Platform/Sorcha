// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Inbox;
using Sorcha.ServiceClients.Models;
using Sorcha.ServiceClients.Participant;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Background service that periodically drains pending digest events from
/// Redis sorted sets (one per user) and writes a single consolidated inbox
/// entry per user per cycle to the durable Tenant inbox.
/// </summary>
/// <remarks>
/// <para>
/// Feature 118 / T076. Replaces the previous behaviour of republishing the
/// digest as JSON to the legacy <c>wallet:notifications</c> Redis pub/sub
/// channel — pre-release means no parallel-fire window. The sorted-set
/// dequeue path stays (it is the source of digest events; T075 already
/// migrated the real-time path off pub/sub). <c>EventsHubNotificationBridge</c>
/// in Blueprint Service is the only remaining consumer of the legacy channel
/// and stays in tree until T121 retires EventsHub entirely.
/// </para>
/// <para>
/// Lua atomic dequeue is preserved verbatim — multi-instance correctness was
/// never about how the digest was *delivered*, only how it was *read*.
/// </para>
/// </remarks>
public sealed class NotificationDigestWorker : BackgroundService
{
    private const string DigestKeyPrefix = "wallet:digest:";
    private const string DigestActiveUsersKey = "wallet:digest:active-users";

    /// <summary>Bit-flag value matching <c>Sorcha.Tenant.Service.Models.ChannelHints.Inbox | Digest</c>.</summary>
    private const int InboxAndDigestChannelHints = 1 | 8;

    /// <summary>
    /// Lua script for atomic dequeue: read all entries up to a score, then remove them.
    /// Prevents double delivery when multiple instances run concurrently.
    /// </summary>
    private const string AtomicDequeueLuaScript = @"
        local entries = redis.call('ZRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
        if #entries > 0 then
            redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1])
        end
        return entries
    ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly NotificationMetrics _metrics;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDigestWorker> _logger;

    private readonly int _checkIntervalMinutes;

    /// <summary>Initialises a new <see cref="NotificationDigestWorker"/>.</summary>
    public NotificationDigestWorker(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        NotificationMetrics metrics,
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationDigestWorker> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var notifSection = configuration.GetSection("Notifications");
        _checkIntervalMinutes = notifSection.GetValue("DigestCheckIntervalMinutes", 5);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NotificationDigestWorker started — checking every {Interval} minutes",
            _checkIntervalMinutes);

        // Initial delay to let other services start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingDigestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing digest notifications");
            }

            await Task.Delay(TimeSpan.FromMinutes(_checkIntervalMinutes), stoppingToken);
        }

        _logger.LogInformation("NotificationDigestWorker stopped");
    }

    /// <summary>
    /// Process all pending digest queues across all users.
    /// Uses an active-users SET instead of SCAN to avoid walking the entire keyspace.
    /// </summary>
    internal async Task ProcessPendingDigestsAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        var activeUsers = await db.SetMembersAsync(DigestActiveUsersKey);

        if (activeUsers.Length == 0)
        {
            _logger.LogDebug("No pending digest queues found");
            return;
        }

        _logger.LogDebug("Found {Count} digest queues to process", activeUsers.Length);

        // One scope for the whole sweep — keeps the participant + inbox HttpClients
        // recycled between users while staying short-lived overall.
        using var scope = _scopeFactory.CreateScope();
        var participants = scope.ServiceProvider.GetRequiredService<IParticipantServiceClient>();
        var inbox = scope.ServiceProvider.GetRequiredService<IPlatformInboxClient>();

        foreach (var userIdValue in activeUsers)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var userId = userIdValue.ToString();
            var key = (RedisKey)$"{DigestKeyPrefix}{userId}";
            await ProcessUserDigestAsync(db, key, userId, participants, inbox, cancellationToken);
        }
    }

    private async Task ProcessUserDigestAsync(
        IDatabase db,
        RedisKey key,
        string userId,
        IParticipantServiceClient participants,
        IPlatformInboxClient inbox,
        CancellationToken cancellationToken)
    {
        try
        {
            var maxScore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var result = await db.ScriptEvaluateAsync(
                AtomicDequeueLuaScript,
                keys: [key],
                values: [maxScore]);

            if (result.IsNull || result.Resp2Type != ResultType.Array)
                return;

            var entries = (RedisResult[])result!;
            if (entries.Length == 0)
                return;

            var events = new List<InboundActionEvent>(entries.Length);
            foreach (var entry in entries)
            {
                var json = entry.ToString();
                if (string.IsNullOrEmpty(json))
                    continue;

                try
                {
                    var actionEvent = JsonSerializer.Deserialize<InboundActionEvent>(json, JsonOptions);
                    if (actionEvent is not null)
                        events.Add(actionEvent);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize digest event for user {UserId}", userId);
                }
            }

            if (events.Count == 0)
                return;

            var grouped = events
                .GroupBy(e => e.BlueprintId ?? "unknown")
                .Select(g => new
                {
                    BlueprintId = g.Key,
                    Count = g.Count(),
                    Latest = g.Max(e => e.Timestamp),
                })
                .OrderByDescending(g => g.Latest)
                .ToList();

            var written = await TryWriteDigestInboxAsync(
                userId, events, grouped.Count, participants, inbox, cancellationToken)
                .ConfigureAwait(false);

            if (written)
            {
                _metrics.RecordDigestDelivered(events.Count);
                _logger.LogInformation(
                    "Digest inbox entry written for user {UserId}: {EventCount} events across {BlueprintCount} blueprints",
                    userId, events.Count, grouped.Count);
            }

            // Remove from active-users set if sorted set is now empty.
            // Done regardless of inbox-write success — the events have been atomically
            // removed from the queue, so leaving the active flag set would be a lie.
            var remainingCount = await db.SortedSetLengthAsync(key);
            if (remainingCount == 0)
            {
                await db.SetRemoveAsync(DigestActiveUsersKey, userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process digest for user {UserId}", userId);
        }
    }

    private async Task<bool> TryWriteDigestInboxAsync(
        string userId,
        IReadOnlyList<InboundActionEvent> events,
        int blueprintGroupCount,
        IParticipantServiceClient participants,
        IPlatformInboxClient inbox,
        CancellationToken ct)
    {
        try
        {
            // Resolve a recipient wallet → participant → PlatformUserId. Every event
            // in this batch belongs to the same userId (the queue key is per-user),
            // so any event's WalletAddress works.
            var firstWallet = events[0].WalletAddress;
            var participant = await participants.GetByWalletAddressAsync(firstWallet, ct).ConfigureAwait(false);
            if (participant is null)
            {
                _logger.LogWarning(
                    "Digest skip — no participant for wallet {Wallet} (user {UserId})",
                    firstWallet, userId);
                return false;
            }

            var platformUserId = await inbox.ResolvePlatformUserIdAsync(participant.UserId, ct).ConfigureAwait(false);
            if (platformUserId is null)
            {
                _logger.LogWarning(
                    "Digest skip — could not resolve PlatformUserId for UserIdentity {UserIdentityId} (user {UserId})",
                    participant.UserId, userId);
                return false;
            }

            var sourceEventId = DeterministicSourceEventId(userId, events);
            var latest = events.Max(e => e.Timestamp);
            var title = events.Count == 1
                ? "1 action awaiting your attention"
                : $"{events.Count} actions awaiting your attention";
            var summary = blueprintGroupCount == 1
                ? null
                : $"Across {blueprintGroupCount} blueprints";

            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId.Value,
                Category: "Action",
                Severity: "Info",
                CorrelationKey: $"digest:{userId}:{latest.ToUnixTimeMilliseconds()}",
                DetailHref: "/api/me/inbox",
                SourceEventId: sourceEventId,
                OccurredAt: latest,
                Title: title,
                Summary: summary,
                IconKey: "action.digest",
                ChannelHints: InboxAndDigestChannelHints);

            var outcome = await inbox.WriteAsync(payload, ct).ConfigureAwait(false);
            _logger.LogDebug(
                "Digest inbox entry {Outcome} for user {UserId} EntryId={EntryId}",
                outcome.Idempotent ? "idempotent" : "created",
                userId, outcome.EntryId);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inbox-write failed for digest — UserId={UserId}", userId);
            return false;
        }
    }

    private static Guid DeterministicSourceEventId(string userId, IReadOnlyList<InboundActionEvent> events)
    {
        // Stable across re-runs that drain the same set of events. Built from
        // userId + the txIds in stable (sorted) order — order in the sorted-set
        // dequeue is timestamp-sorted but we sort to defend against reorderings.
        var txIds = events.Select(e => e.TransactionId).OrderBy(t => t, StringComparer.Ordinal);
        var input = $"sorcha.inbox.digest:{userId}:{string.Join(',', txIds)}";
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
