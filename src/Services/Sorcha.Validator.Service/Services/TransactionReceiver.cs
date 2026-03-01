// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Receives transactions from external sources (Peer Service, API endpoints).
/// </summary>
public class TransactionReceiver : ITransactionReceiver
{
    private readonly IMemPoolManager _memPoolManager;
    private readonly IValidationEngine _validationEngine;
    private readonly TransactionReceiverConfiguration _config;
    private readonly ILogger<TransactionReceiver> _logger;

    // Track known transaction hashes (for duplicate detection)
    private readonly ConcurrentDictionary<string, DateTimeOffset> _knownTransactions = new();
    private readonly object _cleanupLock = new();
    private DateTimeOffset _lastCleanup = DateTimeOffset.UtcNow;

    // Statistics
    private long _totalReceived;
    private long _totalAccepted;
    private long _totalRejected;
    private long _totalDuplicates;
    private DateTimeOffset? _lastReceivedAt;
    private readonly ConcurrentQueue<DateTimeOffset> _recentReceiveTimes = new();

    public TransactionReceiver(
        IMemPoolManager memPoolManager,
        IValidationEngine validationEngine,
        IOptions<TransactionReceiverConfiguration> config,
        ILogger<TransactionReceiver> logger)
    {
        _memPoolManager = memPoolManager ?? throw new ArgumentNullException(nameof(memPoolManager));
        _validationEngine = validationEngine ?? throw new ArgumentNullException(nameof(validationEngine));
        _config = config?.Value ?? new TransactionReceiverConfiguration();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<TransactionReceptionResult> ReceiveTransactionAsync(
        string transactionHash,
        byte[] transactionData,
        string senderPeerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionHash);
        ArgumentNullException.ThrowIfNull(transactionData);

        Interlocked.Increment(ref _totalReceived);
        var receivedAt = DateTimeOffset.UtcNow;
        _lastReceivedAt = receivedAt;
        RecordReceiveTime(receivedAt);

        _logger.LogDebug(
            "Receiving transaction {TransactionHash} from peer {PeerId} ({Size} bytes)",
            transactionHash, senderPeerId, transactionData.Length);

        try
        {
            // Check for duplicate
            if (await IsTransactionKnownAsync(transactionHash, ct))
            {
                Interlocked.Increment(ref _totalDuplicates);
                _logger.LogDebug(
                    "Transaction {TransactionHash} already known, ignoring duplicate",
                    transactionHash);

                return new TransactionReceptionResult
                {
                    Accepted = false,
                    AlreadyKnown = true,
                    ReceivedAt = receivedAt
                };
            }

            // Enforce transaction size limit
            if (transactionData.Length > _config.MaxTransactionSizeBytes)
            {
                Interlocked.Increment(ref _totalRejected);
                _logger.LogWarning(
                    "Transaction {TransactionHash} from peer {PeerId} exceeds size limit ({Size} > {MaxSize} bytes)",
                    transactionHash, senderPeerId, transactionData.Length, _config.MaxTransactionSizeBytes);

                return new TransactionReceptionResult
                {
                    Accepted = false,
                    ValidationErrors = new[] { $"TRANSACTION_TOO_LARGE: Transaction size {transactionData.Length} bytes exceeds maximum {_config.MaxTransactionSizeBytes} bytes" },
                    ReceivedAt = receivedAt
                };
            }

            // Deserialize transaction
            Transaction? transaction;
            try
            {
                transaction = DeserializeTransaction(transactionData);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _totalRejected);
                _logger.LogWarning(
                    ex,
                    "Failed to deserialize transaction {TransactionHash} from peer {PeerId}",
                    transactionHash, senderPeerId);

                return new TransactionReceptionResult
                {
                    Accepted = false,
                    ValidationErrors = new[] { $"Deserialization failed: {ex.Message}" },
                    ReceivedAt = receivedAt
                };
            }

            if (transaction == null)
            {
                Interlocked.Increment(ref _totalRejected);
                return new TransactionReceptionResult
                {
                    Accepted = false,
                    ValidationErrors = new[] { "Deserialization returned null" },
                    ReceivedAt = receivedAt
                };
            }

            // Verify hash matches
            if (transaction.PayloadHash != transactionHash)
            {
                Interlocked.Increment(ref _totalRejected);
                _logger.LogWarning(
                    "Transaction hash mismatch: expected {Expected}, got {Actual}",
                    transactionHash, transaction.PayloadHash);

                return new TransactionReceptionResult
                {
                    Accepted = false,
                    ValidationErrors = new[] { "Transaction hash mismatch" },
                    TransactionId = transaction.TransactionId,
                    ReceivedAt = receivedAt
                };
            }

            // Validate transaction
            var validationResult = await _validationEngine.ValidateTransactionAsync(transaction, ct);

            if (!validationResult.IsValid)
            {
                Interlocked.Increment(ref _totalRejected);
                var errors = validationResult.Errors.Select(e => e.Message).ToList();
                _logger.LogWarning(
                    "Transaction {TransactionId} failed validation: {Errors}",
                    transaction.TransactionId,
                    string.Join(", ", errors));

                return new TransactionReceptionResult
                {
                    Accepted = false,
                    ValidationErrors = errors,
                    TransactionId = transaction.TransactionId,
                    ReceivedAt = receivedAt
                };
            }

            // Add to memory pool
            var added = await _memPoolManager.AddTransactionAsync(transaction.RegisterId, transaction, ct);

            if (!added)
            {
                Interlocked.Increment(ref _totalRejected);
                _logger.LogWarning(
                    "Transaction {TransactionId} rejected by memory pool",
                    transaction.TransactionId);

                return new TransactionReceptionResult
                {
                    Accepted = false,
                    ValidationErrors = new[] { "Rejected by memory pool (full or duplicate)" },
                    TransactionId = transaction.TransactionId,
                    ReceivedAt = receivedAt
                };
            }

            // Mark as known
            _knownTransactions.TryAdd(transactionHash, receivedAt);
            CleanupKnownTransactionsIfNeeded();

            Interlocked.Increment(ref _totalAccepted);
            _logger.LogInformation(
                "Accepted transaction {TransactionId} from peer {PeerId}",
                transaction.TransactionId, senderPeerId);

            return new TransactionReceptionResult
            {
                Accepted = true,
                TransactionId = transaction.TransactionId,
                ReceivedAt = receivedAt
            };
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _totalRejected);
            _logger.LogError(
                ex,
                "Error processing transaction {TransactionHash} from peer {PeerId}",
                transactionHash, senderPeerId);

            return new TransactionReceptionResult
            {
                Accepted = false,
                ValidationErrors = new[] { $"Processing error: {ex.Message}" },
                ReceivedAt = receivedAt
            };
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsTransactionKnownAsync(
        string transactionHash,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionHash);

        // Check in-memory known transactions cache (fastest)
        if (_knownTransactions.ContainsKey(transactionHash))
        {
            return true;
        }

        // Check Redis-backed memory pool for duplicate payload hash
        if (await _memPoolManager.TransactionExistsAsync(transactionHash, ct))
        {
            _logger.LogDebug(
                "Transaction with hash {TransactionHash} found in memory pool",
                transactionHash);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public TransactionReceiverStats GetStats()
    {
        // Calculate transactions per second based on recent receive times
        var tps = CalculateTransactionsPerSecond();

        return new TransactionReceiverStats
        {
            TotalReceived = Interlocked.Read(ref _totalReceived),
            TotalAccepted = Interlocked.Read(ref _totalAccepted),
            TotalRejected = Interlocked.Read(ref _totalRejected),
            TotalDuplicates = Interlocked.Read(ref _totalDuplicates),
            TransactionsPerSecond = tps,
            LastReceivedAt = _lastReceivedAt
        };
    }

    private static Transaction? DeserializeTransaction(byte[] data)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        return JsonSerializer.Deserialize<Transaction>(data, options);
    }

    private void RecordReceiveTime(DateTimeOffset time)
    {
        _recentReceiveTimes.Enqueue(time);

        // Keep only last 100 receive times
        while (_recentReceiveTimes.Count > 100 &&
               _recentReceiveTimes.TryDequeue(out _))
        {
        }
    }

    private double CalculateTransactionsPerSecond()
    {
        var times = _recentReceiveTimes.ToArray();
        if (times.Length < 2)
            return 0;

        var oldest = times[0];
        var newest = times[^1];
        var duration = (newest - oldest).TotalSeconds;

        if (duration <= 0)
            return 0;

        return times.Length / duration;
    }

    private void CleanupKnownTransactionsIfNeeded()
    {
        var now = DateTimeOffset.UtcNow;

        if (now - _lastCleanup < _config.CleanupInterval)
            return;

        lock (_cleanupLock)
        {
            if (now - _lastCleanup < _config.CleanupInterval)
                return;

            _lastCleanup = now;

            // Remove expired entries
            var cutoff = now - _config.KnownTransactionRetention;
            var toRemove = _knownTransactions
                .Where(kvp => kvp.Value < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in toRemove)
            {
                _knownTransactions.TryRemove(key, out _);
            }

            if (toRemove.Count > 0)
            {
                _logger.LogDebug(
                    "Cleaned up {Count} expired known transaction entries",
                    toRemove.Count);
            }
        }
    }
}
