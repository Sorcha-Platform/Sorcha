// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Register.Models.Genesis;

namespace Sorcha.Register.Core.Provenance;

/// <summary>
/// Reads this node's trust anchor from the configured or embedded system-register genesis, once.
/// </summary>
/// <remarks>
/// <para>
/// Registered as a singleton: the genesis is a deploy-time fact, and re-reading it per request would
/// turn a provenance view into file IO. A missing, unreadable or placeholder genesis leaves
/// <see cref="IsKnown"/> false rather than throwing — a node that cannot say what it trusts must
/// still be able to serve the rest of a provenance view (FR-020).
/// </para>
/// <para>
/// <b>Takes a path, not <c>IOptions&lt;SystemRegisterOptions&gt;</c> (Feature 196).</b> That options
/// type lives in <c>Sorcha.ServiceDefaults</c>, which this Core library deliberately does not
/// reference — binding it here would pull ASP.NET hosting into a business-logic library. Each host
/// resolves its own configured path and passes it in, so both the Register Service and the Validator
/// Service run *this* loader over *their* configured genesis. One implementation, one meaning of
/// "the network's root of trust", two hosts.
/// </para>
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
    /// <param name="genesisFilePath">
    /// Absolute path to the genesis JSON file. Null uses the embedded assembly resource.
    /// </param>
    /// <param name="logger">Logger for load outcome.</param>
    public NodeTrustAnchor(string? genesisFilePath, ILogger<NodeTrustAnchor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        SystemRegisterGenesis? genesis = null;

        try
        {
            genesis = GenesisFileLoader.Load(genesisFilePath);
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
