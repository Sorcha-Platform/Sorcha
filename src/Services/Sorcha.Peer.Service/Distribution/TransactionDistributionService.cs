// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Communication.Models;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Protos;
using Sorcha.Register.Models.LocalRelationship;
using Sorcha.ServiceClients.Register;

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
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _localPeerId;

    public TransactionDistributionService(
        ILogger<TransactionDistributionService> logger,
        IOptions<PeerServiceConfiguration> configuration,
        TransactionQueueManager queueManager,
        GossipProtocolEngine gossipEngine,
        RelayCommunicationService relayCommunication,
        PeerConnectionPool? peerConnectionPool = null,
        PeerListManager? peerListManager = null,
        ReverseStreamManager? reverseStreams = null,
        IServiceScopeFactory? scopeFactory = null)
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
        // Feature 145 (T017): optional so existing call sites/tests compile; production DI injects it.
        // Used to resolve the scoped IRegisterServiceClient for roster-based sealer selection — a node
        // on the register's control-record roster seals locally (no fan-out), so the roster, not a
        // seeds/topology heuristic, decides ownership. Null ⇒ fall back to the topology heuristic.
        _scopeFactory = scopeFactory;
        _localPeerId = _configuration.ResolvedPeerId ?? "unknown";
    }

    /// <summary>
    /// Feature 108. Forwards a fully-signed transaction submission (JSON-encoded) to all
    /// connected peers that hold this register — typically the owner (for NAT'd subscribers)
    /// or validator peers. Returns counts of targets attempted vs accepted.
    /// No-op with <c>LocallyOwned == true</c> when the local node has no active channels
    /// for this register (i.e., we own it or have no source peers connected).
    /// </summary>
    public async Task<(int TargetCount, int AcceptedCount, bool LocallyOwned)> ForwardSubmissionAsync(
        string registerId,
        byte[] submissionJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(submissionJson);

        // Feature 145 (T017): roster-based sealer selection. The register's control-record roster —
        // not a seeds/topology heuristic — decides who seals. If this node holds the register and is
        // on its roster as an Owner or Validator, the co-located validator seals locally, so no fan-out
        // is required (LocallyOwned). Authoritative when the relationship resolves; when it doesn't
        // (register not held locally yet, or the lookup failed) we fall through to the topology
        // heuristic below so behaviour is never worse than before.
        var relationship = await TryGetLocalRelationshipAsync(registerId, cancellationToken);
        if (relationship is not null && (relationship.IsOwner || relationship.IsValidator))
        {
            _logger.LogDebug(
                "ForwardSubmissionAsync: register {RegisterId} is locally sealable (roles={Roles}) — " +
                "locally owned per roster, no fan-out required",
                registerId, relationship.Roles);
            return (0, 0, LocallyOwned: true);
        }

        if (_peerConnectionPool is null)
            return (0, 0, LocallyOwned: true);

        var channels = _peerConnectionPool.GetChannelsForRegister(registerId);
        if (channels.Count == 0)
        {
            // Feature 143: a NAT'd owner advertises this register but has no direct channel (empty
            // Address → excluded from GetChannelsForRegister). If we hold its reverse stream, broker
            // the submission to it over that stream so its validator can seal — the relay transport
            // analog of the direct SubmitTransaction RPC.
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

                    return (relayOwners.Count, relayAccepted, LocallyOwned: false);
                }
            }

            // No peer is currently advertised as holding this register. Do NOT infer local
            // ownership from an empty channel set — right after a restart the register
            // advertisements have not been re-exchanged yet, so a subscriber/replica would
            // wrongly report LocallyOwned=true here and the caller would never fan the
            // transaction out (it ends up stranded in the local unverified pool).
            //
            // Ownership is decided by configuration, not by runtime channel state: a node with
            // seed node(s) configured is a subscriber that must forward to its seed (the owner
            // /source); a node with NO seeds is the owner/standalone. Fall back to the
            // bootstrap-established seed channel(s) — the write-path analog of the replication
            // seed-node fallback (Feature 137).
            var seeds = _peerListManager?.GetSeedNodes() ?? [];
            if (seeds.Count == 0)
            {
                _logger.LogDebug(
                    "ForwardSubmissionAsync: no channels and no seed nodes for register {RegisterId} — locally owned (no fan-out required)",
                    registerId);
                return (0, 0, LocallyOwned: true);
            }

            var seedChannels = new List<(string PeerId, GrpcChannel Channel)>();
            foreach (var seed in seeds)
            {
                var seedChannel = _peerConnectionPool.GetChannel(seed.PeerId);
                if (seedChannel is not null)
                    seedChannels.Add((seed.PeerId, seedChannel));
            }

            if (seedChannels.Count == 0)
            {
                // Subscriber (seeds are configured) but no seed channel is connected yet — the
                // topology is still cold (e.g. immediately after a restart, before the seed
                // channel re-establishes). This is NOT locally owned; surface a no-target fan-out
                // so the caller does not mistake it for an owned register. The seed channel
                // re-establishes on the next relay poll / heartbeat.
                _logger.LogWarning(
                    "ForwardSubmissionAsync: register {RegisterId} has no advertised peer and no connected seed channel — " +
                    "fan-out unavailable (topology cold). Not treating as locally owned.",
                    registerId);
                return (0, 0, LocallyOwned: false);
            }

            _logger.LogInformation(
                "ForwardSubmissionAsync: no advertised peer for register {RegisterId} — forwarding to {Count} configured seed node(s) as fallback",
                registerId, seedChannels.Count);
            channels = seedChannels;
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

        return (channels.Count, accepted, LocallyOwned: false);
    }

    /// <summary>
    /// Feature 145 (T017): resolve this node's derived relationship to the register from the
    /// co-located Register service (Feature 108 <c>GET /api/registers/{id}/local-relationship</c>,
    /// roster-cached server-side). Returns <c>null</c> — and the caller falls back to the topology
    /// heuristic — when the scope factory is unavailable (e.g. unit tests), the register is not yet
    /// known locally, or the lookup fails. The relationship service lives in the Register process,
    /// so we cross the local service boundary via the scoped <see cref="IRegisterServiceClient"/>.
    /// </summary>
    private async Task<RegisterLocalRelationship?> TryGetLocalRelationshipAsync(
        string registerId,
        CancellationToken cancellationToken)
    {
        if (_scopeFactory is null)
            return null;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
            return await registerClient.GetLocalRelationshipAsync(registerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ForwardSubmissionAsync: roster lookup failed for register {RegisterId}; " +
                "falling back to topology heuristic for sealer selection",
                registerId);
            return null;
        }
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

        try
        {
            var address = $"http://{peer.Address}:{peer.Port}";
            using var channel = GrpcChannel.ForAddress(address);
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
