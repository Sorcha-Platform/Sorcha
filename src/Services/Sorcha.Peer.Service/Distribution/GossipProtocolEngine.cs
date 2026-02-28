// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Peer.Service.Distribution;

/// <summary>
/// Implements gossip protocol for efficient transaction distribution
/// Based on epidemic broadcast algorithms with logarithmic message complexity
/// </summary>
public class GossipProtocolEngine
{
    private readonly ILogger<GossipProtocolEngine> _logger;
    private readonly TransactionDistributionConfiguration _configuration;
    private readonly PeerListManager _peerListManager;
    private readonly ConcurrentDictionary<string, TransactionGossipState> _gossipState;
    private readonly ConcurrentDictionary<string, byte[]> _bloomFilter;
    private const int BloomFilterSize = 10000;

    public GossipProtocolEngine(
        ILogger<GossipProtocolEngine> logger,
        IOptions<PeerServiceConfiguration> configuration,
        PeerListManager peerListManager)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value?.TransactionDistribution ?? throw new ArgumentNullException(nameof(configuration));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _gossipState = new ConcurrentDictionary<string, TransactionGossipState>();
        _bloomFilter = new ConcurrentDictionary<string, byte[]>();
    }

    /// <summary>
    /// Selects peers for gossip distribution using fanout algorithm
    /// </summary>
    public IReadOnlyList<PeerNode> SelectGossipTargets(string transactionId, int round)
    {
        var fanout = _configuration.FanoutFactor;
        var allPeers = _peerListManager.GetHealthyPeers();

        if (allPeers.Count == 0)
        {
            _logger.LogWarning("No healthy peers available for gossip");
            return Array.Empty<PeerNode>();
        }

        // Get peers we haven't sent to yet for this transaction
        var state = _gossipState.GetOrAdd(transactionId, _ => new TransactionGossipState
        {
            TransactionId = transactionId,
            StartedAt = DateTimeOffset.UtcNow,
            CurrentRound = round
        });

        var availablePeers = allPeers
            .Where(p => !state.SentToPeers.ContainsKey(p.PeerId))
            .ToList();

        if (availablePeers.Count == 0)
        {
            _logger.LogDebug("All peers have received transaction {TxId}", transactionId);
            return Array.Empty<PeerNode>();
        }

        // Select random fanout peers
        var selectedPeers = availablePeers
            .OrderBy(_ => Random.Shared.Next())
            .Take(fanout)
            .ToList();

        // Mark as sent
        foreach (var peer in selectedPeers)
        {
            state.SentToPeers.TryAdd(peer.PeerId, 0);
        }

        state.CurrentRound = round;
        state.LastGossipAt = DateTimeOffset.UtcNow;

        _logger.LogDebug("Selected {Count} peers for gossip round {Round} of transaction {TxId}",
            selectedPeers.Count, round, transactionId);

        return selectedPeers;
    }

    /// <summary>
    /// Checks if a transaction should be gossiped (not already seen)
    /// </summary>
    public bool ShouldGossip(TransactionNotification transaction)
    {
        // Check if we've already seen this transaction
        if (_gossipState.ContainsKey(transaction.TransactionId))
        {
            return false;
        }

        // Check TTL
        if (transaction.TTL <= 0)
        {
            _logger.LogDebug("Transaction {TxId} has expired TTL", transaction.TransactionId);
            return false;
        }

        // Check max gossip rounds
        if (transaction.GossipRound >= _configuration.GossipRounds)
        {
            _logger.LogDebug("Transaction {TxId} has reached max gossip rounds", transaction.TransactionId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Records that we've seen a transaction
    /// </summary>
    public void RecordSeen(string transactionId)
    {
        _gossipState.TryAdd(transactionId, new TransactionGossipState
        {
            TransactionId = transactionId,
            StartedAt = DateTimeOffset.UtcNow,
            CurrentRound = 0
        });

        _logger.LogTrace("Recorded transaction {TxId} as seen", transactionId);
    }

    /// <summary>
    /// Prepares a transaction for the next gossip round
    /// </summary>
    public TransactionNotification PrepareForNextRound(TransactionNotification transaction)
    {
        var nextTx = new TransactionNotification
        {
            TransactionId = transaction.TransactionId,
            OriginPeerId = transaction.OriginPeerId,
            Timestamp = transaction.Timestamp,
            DataSize = transaction.DataSize,
            DataHash = transaction.DataHash,
            GossipRound = transaction.GossipRound + 1,
            HopCount = transaction.HopCount + 1,
            TTL = transaction.TTL - 1,
            HasFullData = transaction.HasFullData,
            TransactionData = transaction.TransactionData
        };

        _logger.LogTrace("Prepared transaction {TxId} for round {Round}",
            transaction.TransactionId, nextTx.GossipRound);

        return nextTx;
    }

    /// <summary>
    /// Gets gossip statistics for a transaction
    /// </summary>
    public TransactionGossipState? GetGossipState(string transactionId)
    {
        _gossipState.TryGetValue(transactionId, out var state);
        return state;
    }

    /// <summary>
    /// Cleans up old gossip state entries
    /// </summary>
    public int CleanupOldState(TimeSpan maxAge)
    {
        var cutoffTime = DateTimeOffset.UtcNow - maxAge;
        var removedCount = 0;

        foreach (var kvp in _gossipState)
        {
            if (kvp.Value.StartedAt < cutoffTime)
            {
                if (_gossipState.TryRemove(kvp.Key, out _))
                {
                    removedCount++;
                }
            }
        }

        if (removedCount > 0)
        {
            _logger.LogInformation("Cleaned up {Count} old gossip state entries", removedCount);
        }

        return removedCount;
    }

    /// <summary>
    /// Checks if transaction hash exists in bloom filter (for deduplication)
    /// </summary>
    public bool CheckBloomFilter(string peerId, string transactionHash)
    {
        if (!_bloomFilter.TryGetValue(peerId, out var filter))
        {
            return false;
        }

        var hashes = GetBloomFilterHashes(transactionHash);
        foreach (var hash in hashes)
        {
            var byteIndex = hash / 8;
            var bitIndex = hash % 8;

            if ((filter[byteIndex] & (1 << bitIndex)) == 0)
            {
                return false; // Definitely not seen
            }
        }

        return true; // Probably seen
    }

    /// <summary>
    /// Adds transaction hash to bloom filter
    /// </summary>
    public void AddToBloomFilter(string peerId, string transactionHash)
    {
        var filter = _bloomFilter.GetOrAdd(peerId, _ => new byte[BloomFilterSize]);

        var hashes = GetBloomFilterHashes(transactionHash);
        foreach (var hash in hashes)
        {
            var byteIndex = hash / 8;
            var bitIndex = hash % 8;
            filter[byteIndex] |= (byte)(1 << bitIndex);
        }

        _logger.LogTrace("Added transaction {Hash} to bloom filter for {PeerId}",
            transactionHash, peerId);
    }

    /// <summary>
    /// Gets hash indices for bloom filter (using double hashing)
    /// </summary>
    private int[] GetBloomFilterHashes(string input)
    {
        var hash1 = ComputeHash(input, 0);
        var hash2 = ComputeHash(input, 1);

        // Use 3 hash functions for bloom filter
        return new int[]
        {
            Math.Abs(hash1) % (BloomFilterSize * 8),
            Math.Abs(hash2) % (BloomFilterSize * 8),
            Math.Abs((hash1 + hash2)) % (BloomFilterSize * 8)
        };
    }

    /// <summary>
    /// Computes a hash for bloom filter
    /// </summary>
    private int ComputeHash(string input, int seed)
    {
        var bytes = Encoding.UTF8.GetBytes(input + seed);
        var hash = SHA256.HashData(bytes);
        return BitConverter.ToInt32(hash, 0);
    }
}

/// <summary>
/// Gossip state for a transaction
/// </summary>
public class TransactionGossipState
{
    public string TransactionId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? LastGossipAt { get; set; }
    public int CurrentRound { get; set; }
    public ConcurrentDictionary<string, byte> SentToPeers { get; set; } = new();
}
