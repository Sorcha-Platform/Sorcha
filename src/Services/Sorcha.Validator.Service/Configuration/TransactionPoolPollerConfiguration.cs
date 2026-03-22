// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Configuration for the transaction pool poller
/// </summary>
public class TransactionPoolPollerConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings
    /// </summary>
    public const string SectionName = "TransactionPoolPoller";

    /// <summary>
    /// Interval between polling attempts when queue is empty
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum number of transactions to fetch per poll
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Redis key prefix for unverified transaction queues
    /// </summary>
    public string KeyPrefix { get; set; } = "sorcha:validator:unverified:";

    /// <summary>
    /// Maximum time to wait for transactions (Redis BRPOP timeout)
    /// </summary>
    public TimeSpan BlockingTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether to use blocking pop operations
    /// </summary>
    public bool UseBlockingPop { get; set; } = true;

    /// <summary>
    /// Maximum retries for failed Redis operations
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// TTL for transactions in the unverified pool
    /// </summary>
    public TimeSpan TransactionTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether the poller is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum transaction payload size in bytes. Transactions exceeding this limit
    /// are rejected at submission time. Default: 1 MB.
    /// </summary>
    public long MaxTransactionPayloadBytes { get; set; } = 1_048_576;
}
