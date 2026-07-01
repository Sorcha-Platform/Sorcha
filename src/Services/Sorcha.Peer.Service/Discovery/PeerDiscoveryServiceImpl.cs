// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Identity;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Discovery;

/// <summary>
/// gRPC service implementation for peer discovery
/// </summary>
public class PeerDiscoveryServiceImpl : PeerDiscovery.PeerDiscoveryBase
{
    private readonly ILogger<PeerDiscoveryServiceImpl> _logger;
    private readonly PeerServiceConfiguration _configuration;
    private readonly PeerListManager _peerListManager;
    private readonly IPeerChallengeStore _challengeStore;

    public PeerDiscoveryServiceImpl(
        ILogger<PeerDiscoveryServiceImpl> logger,
        IOptions<PeerServiceConfiguration> configuration,
        PeerListManager peerListManager,
        IPeerChallengeStore challengeStore)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _challengeStore = challengeStore ?? throw new ArgumentNullException(nameof(challengeStore));
    }

    /// <summary>
    /// Issues a one-time challenge nonce (Feature 175) that the caller signs with its node key to
    /// prove node-identity possession in the subsequent <see cref="RegisterPeer"/>.
    /// </summary>
    public override Task<RegistrationChallengeResponse> GetRegistrationChallenge(
        RegistrationChallengeRequest request, ServerCallContext context)
    {
        var (nonce, expiresAt) = _challengeStore.Issue(request.RequestingPeerId ?? string.Empty);
        _logger.LogDebug("Issued registration challenge to {PeerId}", request.RequestingPeerId);
        return Task.FromResult(new RegistrationChallengeResponse
        {
            Nonce = nonce,
            ExpiresAtUnix = expiresAt.ToUnixTimeSeconds()
        });
    }

    /// <summary>
    /// Handles GetPeerList requests - returns a list of known peers
    /// </summary>
    public override Task<PeerListResponse> GetPeerList(PeerListRequest request, ServerCallContext context)
    {
        _logger.LogDebug("GetPeerList request from {PeerId}", request.RequestingPeerId);

        try
        {
            // Get healthy peers
            var healthyPeers = _peerListManager.GetHealthyPeers();

            // Limit to requested count
            var maxPeers = request.MaxPeers > 0 ? request.MaxPeers : 100;
            var peersToReturn = healthyPeers
                .Take(maxPeers)
                .Select(ConvertToPeerInfo)
                .ToList();

            var response = new PeerListResponse
            {
                TotalPeers = healthyPeers.Count
            };
            response.Peers.AddRange(peersToReturn);

            _logger.LogInformation("Returning {Count} peers to {PeerId}",
                peersToReturn.Count, request.RequestingPeerId);

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing GetPeerList request");
            throw new RpcException(new Status(StatusCode.Internal, "Error retrieving peer list"));
        }
    }

    /// <summary>
    /// Handles RegisterPeer requests - adds a new peer to the list
    /// </summary>
    public override async Task<RegisterPeerResponse> RegisterPeer(RegisterPeerRequest request, ServerCallContext context)
    {
        _logger.LogInformation("RegisterPeer request from {PeerId} at {Address}:{Port}",
            request.PeerInfo.PeerId, request.PeerInfo.Address, request.PeerInfo.Port);

        try
        {
            // Validate peer info
            if (string.IsNullOrEmpty(request.PeerInfo.PeerId))
            {
                return new RegisterPeerResponse
                {
                    Success = false,
                    Message = "Invalid peer ID"
                };
            }

            if (string.IsNullOrEmpty(request.PeerInfo.Address))
            {
                return new RegisterPeerResponse
                {
                    Success = false,
                    Message = "Invalid peer address"
                };
            }

            // Feature 175 — node-identity proof. Optional for backward compatibility: a peer that
            // supplies no proof registers unverified (as before). A peer that DOES supply proof must
            // pass, else it is refused fail-closed (a bad/replayed proof is not silently downgraded).
            var proofAttempted = !string.IsNullOrEmpty(request.ChallengeNonce)
                || !request.NodeSignature.IsEmpty
                || !request.NodePublicKey.IsEmpty;
            var nodeIdentityVerified = false;

            if (proofAttempted)
            {
                var consumed = _challengeStore.TryConsume(request.PeerInfo.PeerId, request.ChallengeNonce);
                var publicKey = request.NodePublicKey.ToByteArray();
                var signature = request.NodeSignature.ToByteArray();

                if (!consumed)
                {
                    _logger.LogWarning(
                        "RegisterPeer from {PeerId} presented an unknown/expired challenge nonce", request.PeerInfo.PeerId);
                    return new RegisterPeerResponse
                    {
                        Success = false,
                        Message = "Invalid or expired registration challenge"
                    };
                }

                if (!NodeChallenge.Verify(publicKey, request.ChallengeNonce, signature))
                {
                    _logger.LogWarning(
                        "RegisterPeer from {PeerId} failed node-identity signature verification", request.PeerInfo.PeerId);
                    return new RegisterPeerResponse
                    {
                        Success = false,
                        Message = "Node-identity proof verification failed"
                    };
                }

                nodeIdentityVerified = true;
                _logger.LogInformation(
                    "RegisterPeer from {PeerId} verified node identity {NodeIdentity}",
                    request.PeerInfo.PeerId, NodeChallenge.IdentityFromPublicKey(publicKey));
            }

            // Convert and add peer
            var peer = ConvertToPeerNode(request.PeerInfo);
            var added = await _peerListManager.AddOrUpdatePeerAsync(peer);

            if (added)
            {
                _logger.LogInformation(
                    "Successfully registered peer {PeerId} (node identity verified: {Verified})",
                    peer.PeerId, nodeIdentityVerified);
                return new RegisterPeerResponse
                {
                    Success = true,
                    Message = "Peer registered successfully",
                    NodeIdentityVerified = nodeIdentityVerified
                };
            }
            else
            {
                return new RegisterPeerResponse
                {
                    Success = false,
                    Message = "Peer list is full"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing RegisterPeer request");
            return new RegisterPeerResponse
            {
                Success = false,
                Message = "Internal server error"
            };
        }
    }

    /// <summary>
    /// Handles Ping requests - simple health check
    /// </summary>
    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        _logger.LogTrace("Ping request from {PeerId}", request.PeerId);

        // Update last seen time for the peer
        if (!string.IsNullOrEmpty(request.PeerId))
        {
            _ = _peerListManager.UpdateLastSeenAsync(request.PeerId);
        }

        return Task.FromResult(new PingResponse
        {
            PeerId = _configuration.NodeId ?? "unknown",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Status = PeerStatus.Online
        });
    }

    /// <summary>
    /// Handles ExchangePeers requests - gossip-style peer list exchange
    /// </summary>
    public override async Task<PeerExchangeResponse> ExchangePeers(PeerExchangeRequest request, ServerCallContext context)
    {
        _logger.LogDebug("ExchangePeers request from {PeerId} with {Count} peers",
            request.PeerId, request.KnownPeers.Count);

        try
        {
            // Merge incoming peers into our list
            foreach (var remotePeer in request.KnownPeers)
            {
                if (remotePeer.PeerId == (_configuration.NodeId ?? "unknown"))
                    continue; // Don't add ourselves

                var peer = ConvertToPeerNode(remotePeer);
                await _peerListManager.AddOrUpdatePeerAsync(peer);
            }

            // Return our healthy peers
            var maxPeers = request.MaxPeers > 0 ? request.MaxPeers : PeerServiceConstants.MaxPeersInExchangeResponse;
            var ourPeers = _peerListManager.GetHealthyPeers()
                .Take(maxPeers)
                .Select(ConvertToPeerInfo)
                .ToList();

            var response = new PeerExchangeResponse { Success = true };
            response.KnownPeers.AddRange(ourPeers);

            _logger.LogDebug("Exchanged {Received} peers from {PeerId}, returning {Sent} peers",
                request.KnownPeers.Count, request.PeerId, ourPeers.Count);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing ExchangePeers request");
            return new PeerExchangeResponse
            {
                Success = false,
                Message = "Internal server error"
            };
        }
    }

    /// <summary>
    /// Handles FindPeersForRegister requests - returns peers that hold a specific register
    /// </summary>
    public override Task<FindPeersForRegisterResponse> FindPeersForRegister(FindPeersForRegisterRequest request, ServerCallContext context)
    {
        _logger.LogDebug("FindPeersForRegister request from {PeerId} for register {RegisterId}",
            request.RequestingPeerId, request.RegisterId);

        try
        {
            var maxPeers = request.MaxPeers > 0 ? request.MaxPeers : 20;

            var peers = request.RequireFullReplica
                ? _peerListManager.GetFullReplicaPeersForRegister(request.RegisterId)
                : _peerListManager.GetPeersForRegister(request.RegisterId);

            var peerInfos = peers
                .Take(maxPeers)
                .Select(ConvertToPeerInfo)
                .ToList();

            var response = new FindPeersForRegisterResponse
            {
                TotalPeers = peers.Count
            };
            response.Peers.AddRange(peerInfos);

            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing FindPeersForRegister request");
            throw new RpcException(new Status(StatusCode.Internal, "Error finding peers for register"));
        }
    }

    /// <summary>
    /// Converts a PeerNode to PeerInfo proto message
    /// </summary>
    private PeerInfo ConvertToPeerInfo(PeerNode peer)
    {
        var peerInfo = new PeerInfo
        {
            PeerId = peer.PeerId,
            NodeName = peer.NodeName ?? "",
            Address = peer.Address,
            Port = peer.Port,
            IsSeedNode = peer.IsSeedNode,
            LastSeen = peer.LastSeen.ToUnixTimeSeconds()
        };
        peerInfo.SupportedProtocols.AddRange(peer.SupportedProtocols);

        foreach (var reg in peer.AdvertisedRegisters)
        {
            peerInfo.AdvertisedRegisters.Add(new PeerRegisterAdvertisement
            {
                RegisterId = reg.RegisterId,
                HasFullReplica = reg.CanServeFullReplica,
                LatestVersion = reg.LatestVersion,
                IsPublic = reg.IsPublic
            });
        }

        return peerInfo;
    }

    /// <summary>
    /// Converts a PeerInfo proto message to PeerNode
    /// </summary>
    private PeerNode ConvertToPeerNode(PeerInfo peerInfo)
    {
        return new PeerNode
        {
            PeerId = peerInfo.PeerId,
            NodeName = string.IsNullOrEmpty(peerInfo.NodeName) ? null : peerInfo.NodeName,
            Address = peerInfo.Address,
            Port = peerInfo.Port,
            SupportedProtocols = peerInfo.SupportedProtocols.ToList(),
            FirstSeen = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
            FailureCount = 0,
            IsSeedNode = peerInfo.IsSeedNode,
            AverageLatencyMs = 0,
            AdvertisedRegisters = peerInfo.AdvertisedRegisters
                .Select(r => new PeerRegisterInfo
                {
                    RegisterId = r.RegisterId,
                    SyncState = r.HasFullReplica ? RegisterSyncState.FullyReplicated : RegisterSyncState.Active,
                    LatestVersion = r.LatestVersion,
                    IsPublic = r.IsPublic
                })
                .ToList(),
            Capabilities = new Core.PeerCapabilities
            {
                SupportsStreaming = peerInfo.SupportedProtocols.Contains("GrpcStream"),
                SupportsTransactionDistribution = true,
                MaxTransactionSize = 10 * 1024 * 1024
            }
        };
    }
}
