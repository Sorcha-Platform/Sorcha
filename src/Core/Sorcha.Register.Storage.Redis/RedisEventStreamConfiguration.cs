// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Storage.Redis;

/// <summary>
/// Configuration for Redis Streams event infrastructure
/// </summary>
public class RedisEventStreamConfiguration
{
    /// <summary>
    /// Redis key prefix for event streams (e.g. "sorcha:events:")
    /// </summary>
    public string StreamPrefix { get; set; } = "sorcha:events:";

    /// <summary>
    /// Consumer group name identifying this service instance
    /// </summary>
    public string ConsumerGroup { get; set; } = "register-service";

    /// <summary>
    /// Approximate maximum stream length for XADD MAXLEN ~ trimming
    /// </summary>
    public int MaxStreamLength { get; set; } = 10000;

    /// <summary>
    /// Block timeout in milliseconds for XREADGROUP
    /// </summary>
    public int ReadBlockMilliseconds { get; set; } = 5000;

    /// <summary>
    /// Reclaim pending messages after this idle time
    /// </summary>
    public TimeSpan PendingIdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Messages per XREADGROUP call
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Startup delay in seconds before the event processing loop begins.
    /// Allows subscriptions to be registered before processing starts.
    /// Default: 2 seconds.
    /// </summary>
    public int StartupDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Circuit breaker failure ratio threshold (0.0 to 1.0).
    /// Default: 0.5 (50% failure rate triggers the breaker).
    /// </summary>
    public double CircuitBreakerFailureRatio { get; set; } = 0.5;

    /// <summary>
    /// Circuit breaker sampling duration in seconds.
    /// Default: 30 seconds.
    /// </summary>
    public int CircuitBreakerSamplingDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Circuit breaker break duration in seconds.
    /// Default: 15 seconds.
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 15;

    /// <summary>
    /// Timeout in seconds for individual publish operations.
    /// Default: 5 seconds.
    /// </summary>
    public int PublishTimeoutSeconds { get; set; } = 5;
}
