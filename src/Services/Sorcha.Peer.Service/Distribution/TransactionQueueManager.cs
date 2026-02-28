// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using System.Collections.Concurrent;

namespace Sorcha.Peer.Service.Distribution;

/// <summary>
/// Manages queuing of transactions for offline mode and retry logic
/// </summary>
public class TransactionQueueManager : IDisposable
{
    private readonly ILogger<TransactionQueueManager> _logger;
    private readonly OfflineModeConfiguration _configuration;
    private readonly ConcurrentQueue<QueuedTransaction> _queue;
    private readonly SqliteConnection? _dbConnection;
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public TransactionQueueManager(
        ILogger<TransactionQueueManager> logger,
        IOptions<PeerServiceConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value?.OfflineMode ?? throw new ArgumentNullException(nameof(configuration));
        _queue = new ConcurrentQueue<QueuedTransaction>();

        // Initialize persistence if enabled
        if (_configuration.QueuePersistence)
        {
            var dbPath = _configuration.PersistencePath;
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _dbConnection = new SqliteConnection($"Data Source={dbPath}");
            _dbConnection.Open();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (!_initialized)
            {
                await InitializeDatabaseAsync();
                _initialized = true;
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Enqueues a transaction for distribution
    /// </summary>
    public async Task<bool> EnqueueAsync(TransactionNotification transaction, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        // Check queue size limit
        if (_queue.Count >= _configuration.MaxQueueSize)
        {
            _logger.LogWarning("Transaction queue is full ({Count}/{Max}), rejecting transaction {TxId}",
                _queue.Count, _configuration.MaxQueueSize, transaction.TransactionId);
            return false;
        }

        var queuedTx = new QueuedTransaction
        {
            Id = Guid.NewGuid().ToString(),
            Transaction = transaction,
            EnqueuedAt = DateTimeOffset.UtcNow,
            RetryCount = 0,
            Status = QueueStatus.Pending
        };

        _queue.Enqueue(queuedTx);
        _logger.LogInformation("Enqueued transaction {TxId} (queue size: {Size})",
            transaction.TransactionId, _queue.Count);

        // Persist if enabled
        if (_configuration.QueuePersistence)
        {
            await PersistTransactionAsync(queuedTx, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Dequeues the next transaction for processing
    /// </summary>
    public bool TryDequeue(out QueuedTransaction? transaction)
    {
        return _queue.TryDequeue(out transaction);
    }

    /// <summary>
    /// Peeks at the next transaction without removing it
    /// </summary>
    public bool TryPeek(out QueuedTransaction? transaction)
    {
        return _queue.TryPeek(out transaction);
    }

    /// <summary>
    /// Marks a transaction as successfully processed
    /// </summary>
    public async Task MarkAsProcessedAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        _logger.LogDebug("Marked transaction {Id} as processed", id);

        if (_configuration.QueuePersistence)
        {
            await DeleteTransactionAsync(id, cancellationToken);
        }
    }

    /// <summary>
    /// Marks a transaction as failed and requeues if retries remain
    /// </summary>
    public async Task<bool> MarkAsFailedAsync(QueuedTransaction transaction, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        if (transaction == null)
            return false;

        transaction.RetryCount++;
        transaction.LastAttemptAt = DateTimeOffset.UtcNow;

        if (transaction.RetryCount >= _configuration.MaxRetries)
        {
            _logger.LogWarning("Transaction {TxId} exceeded max retries ({Count}), dropping",
                transaction.Transaction.TransactionId, transaction.RetryCount);
            transaction.Status = QueueStatus.Failed;

            if (_configuration.QueuePersistence)
            {
                await DeleteTransactionAsync(transaction.Id, cancellationToken);
            }

            return false;
        }

        _logger.LogInformation("Transaction {TxId} failed, retry {Retry}/{Max}",
            transaction.Transaction.TransactionId, transaction.RetryCount, _configuration.MaxRetries);

        transaction.Status = QueueStatus.Pending;
        _queue.Enqueue(transaction);

        if (_configuration.QueuePersistence)
        {
            await PersistTransactionAsync(transaction, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Gets the current queue size
    /// </summary>
    public int GetQueueSize() => _queue.Count;

    /// <summary>
    /// Checks if the queue is empty
    /// </summary>
    public bool IsEmpty() => _queue.IsEmpty;

    /// <summary>
    /// Loads transactions from database on startup
    /// </summary>
    public async Task LoadFromDatabaseAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync();

        if (!_configuration.QueuePersistence || _dbConnection == null)
            return;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var command = _dbConnection.CreateCommand();
            command.CommandText = @"
                SELECT id, transaction_id, origin_peer_id, timestamp, data_size, data_hash,
                       gossip_round, hop_count, ttl, has_full_data, transaction_data,
                       enqueued_at, retry_count, status
                FROM transaction_queue
                WHERE status = 'Pending'
                ORDER BY enqueued_at";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var loadedCount = 0;

            while (await reader.ReadAsync(cancellationToken))
            {
                var queuedTx = new QueuedTransaction
                {
                    Id = reader.GetString(0),
                    Transaction = new TransactionNotification
                    {
                        TransactionId = reader.GetString(1),
                        OriginPeerId = reader.GetString(2),
                        Timestamp = DateTimeOffset.Parse(reader.GetString(3)),
                        DataSize = reader.GetInt32(4),
                        DataHash = reader.GetString(5),
                        GossipRound = reader.GetInt32(6),
                        HopCount = reader.GetInt32(7),
                        TTL = reader.GetInt32(8),
                        HasFullData = reader.GetBoolean(9),
                        TransactionData = reader.IsDBNull(10) ? null : (byte[])reader.GetValue(10)
                    },
                    EnqueuedAt = DateTimeOffset.Parse(reader.GetString(11)),
                    RetryCount = reader.GetInt32(12),
                    Status = Enum.Parse<QueueStatus>(reader.GetString(13))
                };

                _queue.Enqueue(queuedTx);
                loadedCount++;
            }

            _logger.LogInformation("Loaded {Count} transactions from queue database", loadedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading transactions from database");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Initializes the database schema
    /// </summary>
    private async Task InitializeDatabaseAsync()
    {
        if (_dbConnection == null)
            return;

        await _dbLock.WaitAsync();
        try
        {
            using var command = _dbConnection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS transaction_queue (
                    id TEXT PRIMARY KEY,
                    transaction_id TEXT NOT NULL,
                    origin_peer_id TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    data_size INTEGER NOT NULL,
                    data_hash TEXT NOT NULL,
                    gossip_round INTEGER NOT NULL,
                    hop_count INTEGER NOT NULL,
                    ttl INTEGER NOT NULL,
                    has_full_data INTEGER NOT NULL,
                    transaction_data BLOB,
                    enqueued_at TEXT NOT NULL,
                    retry_count INTEGER NOT NULL,
                    status TEXT NOT NULL
                )";

            await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Transaction queue database initialized");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Persists a transaction to the database
    /// </summary>
    private async Task PersistTransactionAsync(QueuedTransaction queuedTx, CancellationToken cancellationToken)
    {
        if (_dbConnection == null)
            return;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var command = _dbConnection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO transaction_queue
                (id, transaction_id, origin_peer_id, timestamp, data_size, data_hash,
                 gossip_round, hop_count, ttl, has_full_data, transaction_data,
                 enqueued_at, retry_count, status)
                VALUES ($id, $txId, $originPeer, $timestamp, $dataSize, $dataHash,
                        $gossipRound, $hopCount, $ttl, $hasFullData, $txData,
                        $enqueuedAt, $retryCount, $status)";

            var tx = queuedTx.Transaction;
            command.Parameters.AddWithValue("$id", queuedTx.Id);
            command.Parameters.AddWithValue("$txId", tx.TransactionId);
            command.Parameters.AddWithValue("$originPeer", tx.OriginPeerId);
            command.Parameters.AddWithValue("$timestamp", tx.Timestamp.ToString("o"));
            command.Parameters.AddWithValue("$dataSize", tx.DataSize);
            command.Parameters.AddWithValue("$dataHash", tx.DataHash);
            command.Parameters.AddWithValue("$gossipRound", tx.GossipRound);
            command.Parameters.AddWithValue("$hopCount", tx.HopCount);
            command.Parameters.AddWithValue("$ttl", tx.TTL);
            command.Parameters.AddWithValue("$hasFullData", tx.HasFullData ? 1 : 0);
            command.Parameters.AddWithValue("$txData", (object?)tx.TransactionData ?? DBNull.Value);
            command.Parameters.AddWithValue("$enqueuedAt", queuedTx.EnqueuedAt.ToString("o"));
            command.Parameters.AddWithValue("$retryCount", queuedTx.RetryCount);
            command.Parameters.AddWithValue("$status", queuedTx.Status.ToString());

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting transaction {TxId}", queuedTx.Transaction.TransactionId);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    /// <summary>
    /// Deletes a transaction from the database
    /// </summary>
    private async Task DeleteTransactionAsync(string id, CancellationToken cancellationToken)
    {
        if (_dbConnection == null)
            return;

        await _dbLock.WaitAsync(cancellationToken);
        try
        {
            using var command = _dbConnection.CreateCommand();
            command.CommandText = "DELETE FROM transaction_queue WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting transaction {Id}", id);
        }
        finally
        {
            _dbLock.Release();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _initLock.Dispose();
            _dbLock.Dispose();
            _dbConnection?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// A queued transaction with retry information
/// </summary>
public class QueuedTransaction
{
    public string Id { get; set; } = string.Empty;
    public TransactionNotification Transaction { get; set; } = null!;
    public DateTimeOffset EnqueuedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public int RetryCount { get; set; }
    public QueueStatus Status { get; set; }
}

/// <summary>
/// Queue status for transactions
/// </summary>
public enum QueueStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}
