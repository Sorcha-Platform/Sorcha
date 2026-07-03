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
    /// Maximum number of times a transaction may be returned to the unverified pool before it is
    /// evicted (dropped, not re-submitted) and an operator alert is raised (<c>LogCritical</c> +
    /// the <c>sorcha_validator_pool_transaction_evicted_total</c> counter). Distinct from
    /// <see cref="MaxRetries"/> (transient Redis-operation retries): this bounds a transaction's
    /// *lifetime* in the pool so one that can never be sealed cannot re-submit at ~1 Hz forever
    /// (issue #787). Set high enough to clear legitimately-delayed transactions given your docket
    /// cadence; <c>0</c> disables the bound (legacy unbounded behaviour).
    /// </summary>
    public int MaxTransactionRetries { get; set; } = 50;

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
