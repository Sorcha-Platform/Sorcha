// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sorcha.ServiceClients.Models;
using StackExchange.Redis;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Background service that periodically processes pending digest notifications.
/// Scans Redis sorted sets for accumulated events, groups by blueprint,
/// and delivers consolidated notifications via Redis pub/sub for EventsHub push.
/// Uses Lua scripting for atomic dequeue to prevent double delivery.
/// </summary>
public sealed class NotificationDigestWorker : BackgroundService
{
    private const string PubSubChannel = "wallet:notifications";
    private const string DigestKeyPrefix = "wallet:digest:";
    private const string DigestActiveUsersKey = "wallet:digest:active-users";

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
    private readonly ILogger<NotificationDigestWorker> _logger;

    private readonly int _checkIntervalMinutes;

    public NotificationDigestWorker(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        NotificationMetrics metrics,
        ILogger<NotificationDigestWorker> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
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

        // Use active-users set instead of SCAN — O(M) where M is digest users, not O(N) total keys
        var activeUsers = await db.SetMembersAsync(DigestActiveUsersKey);

        if (activeUsers.Length == 0)
        {
            _logger.LogDebug("No pending digest queues found");
            return;
        }

        _logger.LogDebug("Found {Count} digest queues to process", activeUsers.Length);

        foreach (var userIdValue in activeUsers)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var userId = userIdValue.ToString();
            var key = (RedisKey)$"{DigestKeyPrefix}{userId}";
            await ProcessUserDigestAsync(db, key, userId, cancellationToken);
        }
    }

    private async Task ProcessUserDigestAsync(
        IDatabase db, RedisKey key, string userId, CancellationToken cancellationToken)
    {
        try
        {
            // Atomic dequeue: read and remove all entries up to current timestamp
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

            // Deserialize events
            var events = new List<InboundActionEvent>();
            foreach (var entry in entries)
            {
                var json = entry.ToString();
                if (string.IsNullOrEmpty(json))
                    continue;

                try
                {
                    var actionEvent = JsonSerializer.Deserialize<InboundActionEvent>(json, JsonOptions);
                    if (actionEvent != null)
                        events.Add(actionEvent);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize digest event for user {UserId}", userId);
                }
            }

            if (events.Count == 0)
                return;

            // Group by blueprint and create consolidated digest notification
            var grouped = events
                .GroupBy(e => e.BlueprintId ?? "unknown")
                .Select(g => new DigestBlueprintGroup
                {
                    BlueprintId = g.Key,
                    ActionCount = g.Count(),
                    LatestTimestamp = g.Max(e => e.Timestamp),
                    Events = g.ToList()
                })
                .ToList();

            // Publish consolidated digest notification
            var digestNotification = new DigestNotification
            {
                UserId = userId,
                TotalEvents = events.Count,
                BlueprintGroups = grouped,
                DigestTimestamp = DateTimeOffset.UtcNow
            };

            var subscriber = _redis.GetSubscriber();
            var digestJson = JsonSerializer.Serialize(digestNotification, JsonOptions);
            await subscriber.PublishAsync(RedisChannel.Literal(PubSubChannel), digestJson);
            _metrics.RecordDigestDelivered(events.Count);

            _logger.LogInformation(
                "Digest delivered for user {UserId}: {EventCount} events across {BlueprintCount} blueprints",
                userId, events.Count, grouped.Count);

            // Remove from active-users set if sorted set is now empty
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
}

/// <summary>
/// Consolidated digest notification published to Redis pub/sub.
/// Contains grouped events for a single user.
/// </summary>
internal record DigestNotification
{
    public required string UserId { get; init; }
    public int TotalEvents { get; init; }
    public required List<DigestBlueprintGroup> BlueprintGroups { get; init; }
    public DateTimeOffset DigestTimestamp { get; init; }
}

/// <summary>
/// Events grouped by blueprint within a digest.
/// </summary>
internal record DigestBlueprintGroup
{
    public required string BlueprintId { get; init; }
    public int ActionCount { get; init; }
    public DateTimeOffset LatestTimestamp { get; init; }
    public required List<InboundActionEvent> Events { get; init; }
}
