// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Register.Core.Events;
using StackExchange.Redis;

namespace Sorcha.Register.Storage.Redis;

/// <summary>
/// Subscribes to register domain events from Redis Streams using consumer groups
/// </summary>
public class RedisStreamEventSubscriber : IEventSubscriber
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisEventStreamConfiguration _config;
    private readonly ILogger<RedisStreamEventSubscriber> _logger;
    private readonly string _consumerName;

    // Plain Dictionary protected by _lock — preferred over ConcurrentDictionary for coordinated multi-field updates
    private readonly Dictionary<string, List<SubscriptionEntry>> _subscriptions = new();
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public RedisStreamEventSubscriber(
        IConnectionMultiplexer redis,
        IOptions<RedisEventStreamConfiguration> options,
        ILogger<RedisStreamEventSubscriber> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _consumerName = $"{_config.ConsumerGroup}-{Environment.MachineName}-{Environment.ProcessId}";
    }

    public Task SubscribeAsync<TEvent>(string topic, Func<TEvent, Task> handler, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var streamKey = $"{_config.StreamPrefix}{topic}";
        var typeName = typeof(TEvent).Name;

        lock (_lock)
        {
            if (!_subscriptions.TryGetValue(streamKey, out var handlers))
            {
                handlers = new List<SubscriptionEntry>();
                _subscriptions[streamKey] = handlers;
            }

            handlers.Add(new SubscriptionEntry(
                typeName,
                typeof(TEvent),
                async obj => await handler((TEvent)obj)));
        }

        _logger.LogInformation(
            "Registered subscription for {EventType} on stream {StreamKey}",
            typeName, streamKey);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the long-running processing loop. Called by EventSubscriptionHostedService.
    /// </summary>
    public async Task StartProcessingAsync(CancellationToken cancellationToken)
    {
        Dictionary<string, List<SubscriptionEntry>> subscriptionSnapshot;
        lock (_lock)
        {
            subscriptionSnapshot = new Dictionary<string, List<SubscriptionEntry>>(_subscriptions);
        }

        if (subscriptionSnapshot.Count == 0)
        {
            _logger.LogInformation("No event subscriptions registered, skipping processing");
            return;
        }

        // Ensure consumer groups exist for all subscribed streams
        var db = _redis.GetDatabase();
        foreach (var streamKey in subscriptionSnapshot.Keys)
        {
            await EnsureConsumerGroupAsync(db, streamKey);
        }

        var streamKeys = subscriptionSnapshot.Keys.ToArray();
        var positions = streamKeys.Select(_ => (RedisValue)">").ToArray();
        var lastPendingClaim = DateTime.UtcNow;

        // Pre-build a lookup dictionary for O(1) stream key -> index resolution
        // instead of O(n) Array.IndexOf on every iteration
        var streamKeyIndex = new Dictionary<string, int>(streamKeys.Length);
        for (var i = 0; i < streamKeys.Length; i++)
        {
            streamKeyIndex[streamKeys[i]] = i;
        }

        _logger.LogInformation(
            "Starting event processing loop for {StreamCount} streams as consumer {Consumer}",
            streamKeys.Length, _consumerName);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Read new messages from all subscribed streams
                var results = await db.StreamReadGroupAsync(
                    streamKeys.Select(k => new StreamPosition(k, positions[streamKeyIndex[k]])).ToArray(),
                    _config.ConsumerGroup,
                    _consumerName,
                    _config.BatchSize);

                if (results is not null)
                {
                    foreach (var result in results)
                    {
                        if (result.Entries.Length == 0) continue;

                        if (subscriptionSnapshot.TryGetValue(result.Key.ToString(), out var handlers))
                        {
                            foreach (var entry in result.Entries)
                            {
                                await ProcessEntryAsync(db, result.Key.ToString(), entry, handlers);
                            }
                        }
                    }
                }

                // Periodically reclaim stale pending messages
                if (DateTime.UtcNow - lastPendingClaim > _config.PendingIdleTimeout)
                {
                    foreach (var streamKey in streamKeys)
                    {
                        await ReclaimPendingMessagesAsync(db, streamKey, subscriptionSnapshot[streamKey]);
                    }
                    lastPendingClaim = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex, "Redis error during event processing, retrying after delay");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during event processing");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        _logger.LogInformation("Event processing loop stopped for consumer {Consumer}", _consumerName);
    }

    private async Task EnsureConsumerGroupAsync(IDatabase db, string streamKey)
    {
        try
        {
            // XGROUP CREATE ... MKSTREAM — creates group and stream if they don't exist
            // Start reading from new messages only ("$")
            await db.StreamCreateConsumerGroupAsync(streamKey, _config.ConsumerGroup, "$", createStream: true);
            _logger.LogDebug("Created consumer group {Group} for stream {Stream}", _config.ConsumerGroup, streamKey);
        }
        catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Consumer group already exists — this is expected and fine
            _logger.LogDebug("Consumer group {Group} already exists for stream {Stream}", _config.ConsumerGroup, streamKey);
        }
    }

    private async Task ProcessEntryAsync(
        IDatabase db,
        string streamKey,
        StreamEntry entry,
        List<SubscriptionEntry> handlers)
    {
        var typeName = entry["type"].ToString();
        var data = entry["data"].ToString();

        var matchingHandlers = handlers.Where(h => h.TypeName == typeName).ToList();
        if (matchingHandlers.Count == 0)
        {
            // No handler for this type — acknowledge and skip
            await db.StreamAcknowledgeAsync(streamKey, _config.ConsumerGroup, entry.Id);
            return;
        }

        var allSucceeded = true;
        foreach (var handler in matchingHandlers)
        {
            try
            {
                var eventObj = JsonSerializer.Deserialize(data, handler.EventType, JsonOptions);
                if (eventObj is not null)
                {
                    await handler.Handler(eventObj);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Handler failed for {EventType} on stream {StreamKey}, message {MessageId}",
                    typeName, streamKey, entry.Id);
                allSucceeded = false;
            }
        }

        if (allSucceeded)
        {
            await db.StreamAcknowledgeAsync(streamKey, _config.ConsumerGroup, entry.Id);
        }
        // On failure, leave message pending for reclaim
    }

    private async Task ReclaimPendingMessagesAsync(
        IDatabase db,
        string streamKey,
        List<SubscriptionEntry> handlers)
    {
        try
        {
            var pendingInfo = await db.StreamPendingMessagesAsync(
                streamKey,
                _config.ConsumerGroup,
                10,
                RedisValue.Null);

            foreach (var pending in pendingInfo)
            {
                if (pending.IdleTimeInMilliseconds >= (long)_config.PendingIdleTimeout.TotalMilliseconds)
                {
                    var claimed = await db.StreamClaimAsync(
                        streamKey,
                        _config.ConsumerGroup,
                        _consumerName,
                        (long)_config.PendingIdleTimeout.TotalMilliseconds,
                        [pending.MessageId]);

                    foreach (var entry in claimed)
                    {
                        await ProcessEntryAsync(db, streamKey, entry, handlers);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error reclaiming pending messages for stream {StreamKey}", streamKey);
        }
    }

    internal record SubscriptionEntry(string TypeName, Type EventType, Func<object, Task> Handler);
}
