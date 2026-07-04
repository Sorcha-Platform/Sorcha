// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Communication.Models;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Distribution;

/// <summary>
/// Service for distributing transactions using gossip protocol
/// </summary>
public class TransactionDistributionService
{
    private readonly ILogger<TransactionDistributionService> _logger;
    private readonly PeerServiceConfiguration _configuration;
    private readonly TransactionQueueManager _queueManager;
    private readonly GossipProtocolEngine _gossipEngine;
    private readonly RelayCommunicationService _relayCommunication;
    private readonly PeerConnectionPool? _peerConnectionPool;
    private readonly PeerListManager? _peerListManager;
    private readonly ReverseStreamManager? _reverseStreams;
    private readonly string _localPeerId;

    public TransactionDistributionService(
        ILogger<TransactionDistributionService> logger,
        IOptions<PeerServiceConfiguration> configuration,
        TransactionQueueManager queueManager,
        GossipProtocolEngine gossipEngine,
        RelayCommunicationService relayCommunication,
        PeerConnectionPool? peerConnectionPool = null,
        PeerListManager? peerListManager = null,
        ReverseStreamManager? reverseStreams = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _queueManager = queueManager ?? throw new ArgumentNullException(nameof(queueManager));
        _gossipEngine = gossipEngine ?? throw new ArgumentNullException(nameof(gossipEngine));
        _relayCommunication = relayCommunication ?? throw new ArgumentNullException(nameof(relayCommunication));
        _peerConnectionPool = peerConnectionPool;
        _peerListManager = peerListManager;
        // Feature 143: optional so existing call sites/tests compile; production DI injects it. Lets a
        // rendezvous broker a submission to a NAT'd owner (no direct channel) over its reverse stream.
        _reverseStreams = reverseStreams;
        _localPeerId = _configuration.ResolvedPeerId ?? "unknown";
    }

    /// <summary>
    /// Feature 145 (T034) — carrier-aware fan-out. Forwards a fully-signed transaction submission
    /// (JSON-encoded) ONLY to peers that actually carry/subscribe to <b>this</b> register — direct
    /// channels (peers advertising the register) plus NAT'd owners reachable over a reverse stream
    /// (Feature 143). Returns counts of targets attempted vs accepted.
    /// </summary>
    /// <remarks>
    /// There is deliberately <b>no seed/topology fallback</b>: a transaction is never forwarded to a
    /// node that does not carry the register (e.g. a configured bootstrap seed that never subscribed),
    /// which previously hung the submit when that seed was unreachable. When no carrier is known the
    /// method is a no-op — the local validator seals if this node is on the roster; otherwise the tx
    /// awaits a carrier (durable hand-off + sealing-failure feedback to the consumer are tracked
    /// follow-ups). The submitter does not branch on ownership — there is no <c>LocallyOwned</c> signal.
    /// </remarks>
    public async Task<(int TargetCount, int AcceptedCount)> ForwardSubmissionAsync(
        string registerId,
        byte[] submissionJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(submissionJson);

        if (_peerConnectionPool is null)
            return (0, 0);

        var channels = _peerConnectionPool.GetChannelsForRegister(registerId);
        if (channels.Count == 0)
        {
            // Feature 143: a NAT'd owner carries this register but has no direct channel (empty
            // Address → excluded from GetChannelsForRegister). If we hold its reverse stream, broker
            // the submission to it over that stream so its validator can seal — the relay transport
            // analog of the direct SubmitTransaction RPC. This is still carrier-scoped:
            // GetPeersForRegister only returns peers that advertise THIS register.
            if (_reverseStreams is not null && _peerListManager is not null)
            {
                var relayOwners = _peerListManager.GetPeersForRegister(registerId)
                    .Where(p => _reverseStreams.TryGetStream(p.PeerId, out _))
                    .ToList();

                if (relayOwners.Count > 0)
                {
                    var relayAccepted = 0;
                    foreach (var owner in relayOwners)
                    {
                        var correlationId = Guid.NewGuid().ToString();
                        var relayRequest = new SubmitTransactionRelayRequest
                        {
                            CorrelationId = correlationId,
                            RegisterId = registerId,
                            SubmissionJson = submissionJson,
                            OriginPeerId = _localPeerId
                        };

                        var relayResponse = await _relayCommunication.SendAndWaitAsync<SubmitTransactionRelayResponse>(
                            owner.PeerId,
                            MessageType.SubmitTransactionRequest,
                            relayRequest,
                            correlationId,
                            cancellationToken: cancellationToken);

                        if (relayResponse?.Accepted == true)
                        {
                            relayAccepted++;
                            _logger.LogInformation(
                                "Brokered submission for register {RegisterId} to NAT'd owner {PeerId} over reverse stream — accepted",
                                registerId, owner.PeerId);
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Brokered submission for register {RegisterId} to NAT'd owner {PeerId} — not accepted: {Reason}",
                                registerId, owner.PeerId, relayResponse?.RejectReason ?? "no response (timeout)");
                        }
                    }

                    return (relayOwners.Count, relayAccepted);
                }
            }

            // Feature 145 (T034): no peer carries this register (no direct channel, no reverse-stream
            // owner). Do NOT fall back to bootstrap seed nodes — a seed that never subscribed to this
            // register is not a carrier, and forwarding to an unreachable one hung the submit (the
            // 504). No-op: the local validator seals if this node is on the roster; otherwise the tx
            // awaits a carrier (the "must reach a sealer" durability guarantee is a tracked follow-up).
            _logger.LogDebug(
                "ForwardSubmissionAsync: no carrier known for register {RegisterId} — no fan-out (local validator seals if on roster)",
                registerId);
            return (0, 0);
        }

        var submittedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var accepted = 0;

        foreach (var (peerId, channel) in channels)
        {
            try
            {
                var client = new TransactionDistribution.TransactionDistributionClient(channel);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(_configuration.Communication.ConnectionTimeout));

                var response = await client.SubmitTransactionAsync(
                    new SubmitTransactionRequest
                    {
                        RegisterId = registerId,
                        SubmissionJson = ByteString.CopyFrom(submissionJson),
                        OriginPeerId = _localPeerId,
                        SubmittedAtUnixMs = submittedAt
                    },
                    cancellationToken: cts.Token);

                if (response.Accepted)
                {
                    accepted++;
                    _logger.LogInformation(
                        "Forwarded submission for register {RegisterId} to peer {PeerId} — accepted",
                        registerId, peerId);
                }
                else
                {
                    _logger.LogWarning(
                        "Forwarded submission for register {RegisterId} to peer {PeerId} — rejected: {Reason}",
                        registerId, peerId, response.RejectReason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Error forwarding submission for register {RegisterId} to peer {PeerId}",
                    registerId, peerId);
            }
        }

        return (channels.Count, accepted);
    }

    /// <summary>
    /// Distributes a transaction using gossip protocol
    /// </summary>
    public async Task<bool> DistributeTransactionAsync(
        Core.TransactionNotification transaction,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Distributing transaction {TxId} via gossip", transaction.TransactionId);

            // Check if we should gossip this transaction
            if (!_gossipEngine.ShouldGossip(transaction))
            {
                _logger.LogDebug("Skipping gossip for transaction {TxId} (already seen or expired)",
                    transaction.TransactionId);
                return false;
            }

            // Mark as seen to prevent loops
            _gossipEngine.RecordSeen(transaction.TransactionId);

            // Select gossip targets
            var targets = _gossipEngine.SelectGossipTargets(transaction.TransactionId, transaction.GossipRound);
            if (targets.Count == 0)
            {
                _logger.LogWarning("No gossip targets available for transaction {TxId}", transaction.TransactionId);
                return false;
            }

            // Prepare transaction for next round
            var nextRoundTx = _gossipEngine.PrepareForNextRound(transaction);

            // Send to all targets concurrently
            var sendTasks = targets.Select(peer =>
                SendToPeerAsync(peer, nextRoundTx, cancellationToken));

            var results = await Task.WhenAll(sendTasks);
            var successCount = results.Count(r => r);

            _logger.LogInformation("Distributed transaction {TxId} to {Success}/{Total} peers",
                transaction.TransactionId, successCount, targets.Count);

            return successCount > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error distributing transaction {TxId}", transaction.TransactionId);
            return false;
        }
    }

    /// <summary>
    /// Sends a transaction to a specific peer
    /// </summary>
    private async Task<bool> SendToPeerAsync(
        Core.PeerNode peer,
        Core.TransactionNotification transaction,
        CancellationToken cancellationToken)
    {
        // Relay fallback: NAT'd peers have empty Address
        if (string.IsNullOrEmpty(peer.Address))
        {
            _logger.LogDebug("Peer {PeerId} has no address, sending transaction via relay", peer.PeerId);
            return await _relayCommunication.SendViaRelayAsync(
                peer.PeerId,
                MessageType.TransactionNotification,
                transaction,
                cancellationToken);
        }

        // perf audit T4/F2: reuse the pooled gRPC channel for a connected peer rather than building and
        // tearing down a fresh HTTP/2 connection (TCP+TLS handshake) per gossip message. Only create a
        // throwaway channel when the peer isn't in the pool; the pool owns (and must not dispose) its own.
        GrpcChannel? ownedChannel = null;
        try
        {
            var channel = _peerConnectionPool?.GetChannel(peer.PeerId);
            if (channel is null)
            {
                var address = $"http://{peer.Address}:{peer.Port}";
                ownedChannel = GrpcChannel.ForAddress(address);
                channel = ownedChannel;
            }

            var client = new Protos.TransactionDistribution.TransactionDistributionClient(channel);

            var request = new Protos.TransactionNotification
            {
                TransactionHash = transaction.TransactionId,
                SenderPeerId = transaction.OriginPeerId,
                Timestamp = transaction.Timestamp.ToUnixTimeSeconds(),
                TransactionSize = transaction.DataSize
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_configuration.Communication.ConnectionTimeout));

            var response = await client.NotifyTransactionAsync(request, cancellationToken: cts.Token);

            _logger.LogDebug("Sent transaction {TxId} to peer {PeerId}: {Success}",
                transaction.TransactionId, peer.PeerId, response.WillRequest || response.AlreadyKnown);

            return response.WillRequest || response.AlreadyKnown;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send transaction {TxId} to peer {PeerId}",
                transaction.TransactionId, peer.PeerId);
            return false;
        }
        finally
        {
            ownedChannel?.Dispose();
        }
    }

    /// <summary>
    /// Queues a transaction for later distribution (offline mode)
    /// </summary>
    public async Task<bool> QueueTransactionAsync(
        Core.TransactionNotification transaction,
        CancellationToken cancellationToken = default)
    {
        return await _queueManager.EnqueueAsync(transaction, cancellationToken);
    }

    /// <summary>
    /// Processes queued transactions
    /// </summary>
    public async Task<int> ProcessQueueAsync(CancellationToken cancellationToken = default)
    {
        var processedCount = 0;
        var maxBatch = 10;

        for (int i = 0; i < maxBatch && !cancellationToken.IsCancellationRequested; i++)
        {
            if (!_queueManager.TryDequeue(out var queuedTx) || queuedTx == null)
            {
                break;
            }

            var success = await DistributeTransactionAsync(queuedTx.Transaction, cancellationToken);

            if (success)
            {
                await _queueManager.MarkAsProcessedAsync(queuedTx.Id, cancellationToken);
                processedCount++;
            }
            else
            {
                await _queueManager.MarkAsFailedAsync(queuedTx, cancellationToken);
            }
        }

        if (processedCount > 0)
        {
            _logger.LogInformation("Processed {Count} queued transactions", processedCount);
        }

        return processedCount;
    }

    /// <summary>
    /// Gets queue statistics
    /// </summary>
    public int GetQueueSize() => _queueManager.GetQueueSize();
}
