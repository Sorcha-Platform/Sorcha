// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Register.Models.Genesis;
using Sorcha.ServiceDefaults;

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// Reads this node's trust anchor from the configured or embedded system-register genesis, once.
/// </summary>
/// <remarks>
/// Registered as a singleton: the genesis is a deploy-time fact, and re-reading it per request would
/// turn a provenance view into file IO. A missing, unreadable or placeholder genesis leaves
/// <see cref="IsKnown"/> false rather than throwing — a node that cannot say what it trusts must
/// still be able to serve the rest of a provenance view (FR-020).
/// </remarks>
public sealed class NodeTrustAnchor : INodeTrustAnchor
{
    /// <inheritdoc/>
    public bool IsKnown { get; }

    /// <inheritdoc/>
    public string? NetworkId { get; }

    /// <inheritdoc/>
    public string? GenesisPublicKeyFingerprint { get; }

    /// <inheritdoc/>
    public string? GenesisPayloadHash { get; }

    /// <summary>Loads the anchor from the system-register genesis.</summary>
    public NodeTrustAnchor(IOptions<SystemRegisterOptions> options, ILogger<NodeTrustAnchor> logger)
    {
        SystemRegisterGenesis? genesis = null;

        try
        {
            genesis = GenesisFileLoader.Load(options.Value.GenesisFile);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Could not load the system register genesis; this node holds no trust anchor and " +
                "provenance anchor checks will report unverified");
        }

        var fingerprint = genesis?.GenesisPublicKeyFingerprint;
        var payloadHash = genesis?.GenesisTransaction?.PayloadHash;

        IsKnown = !string.IsNullOrWhiteSpace(fingerprint) && !string.IsNullOrWhiteSpace(payloadHash);
        NetworkId = genesis?.NetworkId;
        GenesisPublicKeyFingerprint = string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint;
        GenesisPayloadHash = string.IsNullOrWhiteSpace(payloadHash) ? null : payloadHash;

        if (IsKnown)
        {
            logger.LogInformation(
                "Provenance trust anchor loaded: network {NetworkId}, genesis key fingerprint {Fingerprint}",
                NetworkId, GenesisPublicKeyFingerprint);
        }
        else
        {
            logger.LogInformation(
                "This node holds no provenance trust anchor (no genesis, or genesis carries no " +
                "fingerprint). Anchor checks will report unverified — see issue #1374");
        }
    }
}
