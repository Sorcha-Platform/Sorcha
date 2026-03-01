// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Configuration for the TransactionReceiver service.
/// </summary>
public class TransactionReceiverConfiguration
{
    /// <summary>
    /// Configuration section name in appsettings.
    /// </summary>
    public const string SectionName = "TransactionReceiver";

    /// <summary>
    /// How long to retain known transaction hashes for duplicate detection.
    /// Default: 1 hour.
    /// </summary>
    public TimeSpan KnownTransactionRetention { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Interval for cleaning up expired known transaction entries.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum size of a transaction in bytes.
    /// Default: 4 MB.
    /// </summary>
    public int MaxTransactionSizeBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// Whether to validate transaction signatures on receipt.
    /// Default: true.
    /// </summary>
    public bool ValidateSignaturesOnReceipt { get; set; } = true;

    /// <summary>
    /// Whether to validate transaction payload schema on receipt.
    /// Default: true.
    /// </summary>
    public bool ValidateSchemaOnReceipt { get; set; } = true;

    /// <summary>
    /// Maximum number of known transaction hashes to retain.
    /// Default: 100,000.
    /// </summary>
    public int MaxKnownTransactions { get; set; } = 100_000;
}
