// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Sorcha.Register.Core.Events;
using StackExchange.Redis;

namespace Sorcha.Register.Storage.Redis;

/// <summary>
/// Publishes register domain events to Redis Streams
/// </summary>
public class RedisStreamEventPublisher : IEventPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisEventStreamConfiguration _config;
    private readonly ILogger<RedisStreamEventPublisher> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public RedisStreamEventPublisher(
        IConnectionMultiplexer redis,
        IOptions<RedisEventStreamConfiguration> options,
        ILogger<RedisStreamEventPublisher> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _config = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = _config.CircuitBreakerFailureRatio,
                SamplingDuration = TimeSpan.FromSeconds(_config.CircuitBreakerSamplingDurationSeconds),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(_config.CircuitBreakerBreakDurationSeconds),
                ShouldHandle = new PredicateBuilder().Handle<RedisException>()
            })
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(_config.PublishTimeoutSeconds)
            })
            .Build();
    }

    public async Task PublishAsync<TEvent>(string topic, TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        var streamKey = $"{_config.StreamPrefix}{topic}";
        var typeName = typeof(TEvent).Name;
        var json = JsonSerializer.Serialize(eventData, JsonOptions);

        try
        {
            await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var db = _redis.GetDatabase();
                await db.StreamAddAsync(
                    streamKey,
                    [
                        new NameValueEntry("type", typeName),
                        new NameValueEntry("data", json),
                        new NameValueEntry("timestamp", DateTime.UtcNow.ToString("O")),
                        new NameValueEntry("source", _config.ConsumerGroup)
                    ],
                    maxLength: _config.MaxStreamLength,
                    useApproximateMaxLength: true);
            }, cancellationToken);

            _logger.LogDebug("Published {EventType} to stream {StreamKey}", typeName, streamKey);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Circuit breaker open — dropping {EventType} for stream {StreamKey}", typeName, streamKey);
        }
        catch (TimeoutRejectedException)
        {
            _logger.LogWarning("Timeout publishing {EventType} to stream {StreamKey}", typeName, streamKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to publish {EventType} to stream {StreamKey}", typeName, streamKey);
        }
    }
}
