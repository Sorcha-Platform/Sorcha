// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;
using Sorcha.Register.Core.Provenance;

namespace Sorcha.Register.Service.Provenance;

/// <summary>
/// Reads a docket, its predecessor and this node's anchor, and shapes them into engine evidence.
/// </summary>
/// <remarks>
/// <para>
/// The data-shaping layer the engine boundary costs (plan D1, recorded honestly there). It maps
/// storage's "absent" conventions onto the engine's: the register stores <c>MerkleRoot</c> and
/// <c>ProposerValidatorId</c> as non-nullable strings defaulting to empty, so a docket sealed before
/// Feature 187 arrives blank rather than null. Blank is normalised to null here, because the engine's
/// contract is that null means "could not be established" and a blank string would otherwise be
/// compared as a value.
/// </para>
/// <para>
/// <b>Never throws for missing evidence.</b> A view that cannot assemble one link must render the
/// rest and mark that link unverified with a reason (FR-020, SC-009). Only "no such docket" is
/// distinguished, as null, so the endpoint can return 404.
/// </para>
/// </remarks>
public sealed class DocketEvidenceAssembler : IDocketEvidenceAssembler
{
    private readonly IReadOnlyRegisterRepository _repository;
    private readonly INodeTrustAnchor _anchor;
    private readonly DocketHasher _docketHasher;
    private readonly ILogger<DocketEvidenceAssembler> _logger;

    /// <summary>Creates an assembler over the register's read-only store and the node's anchor.</summary>
    public DocketEvidenceAssembler(
        IReadOnlyRegisterRepository repository,
        INodeTrustAnchor anchor,
        IHashProvider hashProvider,
        ILogger<DocketEvidenceAssembler> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
        _docketHasher = new DocketHasher(hashProvider);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<DocketEvidenceBundle?> AssembleAsync(
        string registerId,
        ulong docketNumber,
        CancellationToken cancellationToken = default)
    {
        var docket = await _repository.GetDocketAsync(registerId, docketNumber, cancellationToken);
        if (docket is null)
        {
            return null;
        }

        var predecessorHash = await ReadPredecessorHashAsync(registerId, docketNumber, cancellationToken);
        var anchor = await AssembleAnchorAsync(registerId, cancellationToken);
        var leafHashes = await ComputeLeafHashesAsync(registerId, docket, cancellationToken);

        var evidence = new DocketEvidence
        {
            DocketNumber = docket.Id,
            Hash = docket.Hash,
            ClaimedPreviousHash = Blank(docket.PreviousHash),
            PredecessorHash = predecessorHash,
            TransactionIds = docket.TransactionIds ?? [],
            LeafHashes = leafHashes,
            SealedMerkleRoot = Blank(docket.MerkleRoot),
            ProposerValidatorId = Blank(docket.ProposerValidatorId),
            Votes = (docket.Votes ?? [])
                .Select(v => new DocketVoteEvidence
                {
                    ValidatorId = v.ValidatorId,
                    PublicKey = v.ValidatorSignature?.PublicKey is { Length: > 0 } key
                        ? Convert.ToBase64String(key)
                        : null,
                    Decision = v.Decision.ToString(),
                })
                .ToList(),
        };

        return new DocketEvidenceBundle(evidence, anchor);
    }

    /// <summary>
    /// Rebuilds the Merkle leaves this docket's commitment was computed over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A docket does not commit to its transaction ids.</b> <c>DocketBuilder</c> builds the tree
    /// from per-transaction composite hashes of <c>(TransactionId, PayloadHash, Timestamp)</c> via
    /// <see cref="DocketHasher.ComputeTransactionHash"/>. Recomputing over raw ids never matches —
    /// a false tamper report on every docket of every register, which is how this was found: by
    /// running the check against real n1 dockets and getting <c>seal: failed</c> on a healthy ledger.
    /// </para>
    /// <para>
    /// This delegates to the same <see cref="DocketHasher"/> the sealing path uses, and mirrors the
    /// projection this service's own inclusion-proof endpoint already relies on
    /// (<c>VerificationEndpoints</c>) — the one confirmed during the Feature 187 deploy to return a
    /// root byte-identical to the persisted <c>DocketHeader.MerkleRoot</c>. Do not re-derive it here.
    /// </para>
    /// <para>
    /// <b>Leaves follow the docket's own <c>TransactionIds</c>, in order.</b> That is what makes
    /// tampering with the stored id list detectable (SC-003): alter, remove or reorder an id and the
    /// leaf sequence changes, so the recomputed root changes. Building from whatever the store
    /// returns would leave the id list uncommitted and the tamper check vacuous.
    /// </para>
    /// <para>
    /// Returns null when any listed transaction is not held here — the engine renders that
    /// Unverified, never Failed.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>?> ComputeLeafHashesAsync(
        string registerId,
        DocketHeader docket,
        CancellationToken cancellationToken)
    {
        try
        {
            var held = (await _repository.GetTransactionsByDocketAsync(
                registerId, docket.Id, cancellationToken)).ToList();

            // ONE leaf rule for the whole service (#1372). The Feature 188 Seal check and the
            // Register Service's own inclusion-proof and ZK-proof endpoints must agree about what a
            // docket committed to, or a proof and a provenance trail can report different verdicts on
            // the same docket — with no error to say which is right.
            var leaves = Verification.DocketMerkleCommitment.BuildLeaves(docket, held, _docketHasher);

            if (leaves is null)
            {
                _logger.LogDebug(
                    "Docket {Docket} of register {RegisterId} lists a transaction this node does not hold; the seal check cannot recompute",
                    docket.Id, registerId);
                return null;
            }

            return leaves.Hashes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not assemble the Merkle leaves for docket {Docket} of register {RegisterId}",
                docket.Id, registerId);
            return null;
        }
    }

    /// <summary>
    /// The hash of the docket actually preceding this one, or null when this node does not hold it.
    /// </summary>
    /// <remarks>
    /// Null here is what makes a partial replica report Chain as unverified rather than failed. It is
    /// also correct for the genesis docket, which has no predecessor.
    /// </remarks>
    private async Task<string?> ReadPredecessorHashAsync(
        string registerId,
        ulong docketNumber,
        CancellationToken cancellationToken)
    {
        if (docketNumber == 0)
        {
            return null;
        }

        try
        {
            var predecessor = await _repository.GetDocketAsync(registerId, docketNumber - 1, cancellationToken);
            return Blank(predecessor?.Hash);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not read docket {Predecessor} of register {RegisterId} while assembling provenance evidence",
                docketNumber - 1, registerId);
            return null;
        }
    }

    /// <summary>
    /// What this node can say about where the register came from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Scope, stated plainly because overstating it would be the worst thing this feature could
    /// do.</b> A node's trust anchor is the system-register genesis it was deployed with, and on this
    /// platform it covers <i>only</i> the system register: nothing chains an organisation's register
    /// back to the network genesis key (see <c>GenesisIngestionService</c> and
    /// <c>SystemRegisterSyncVerifier</c>, its only two consumers). So:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>System register</b> — the stored genesis transaction's payload hash is compared with the
    ///     payload hash the trust anchor describes. Agreement establishes that the system register
    ///     this node holds is the one its anchor covers; disagreement is a genuine contradiction
    ///     between two known values, and is reported as failed.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Any other register</b> — reported unverified, with a reason saying why: its origin is
    ///     attested by its owner, and this node's anchor does not cover it. That is the truthful
    ///     answer, not a hedge, and it is what FR-004 requires of a check that cannot run.
    ///   </description></item>
    /// </list>
    /// <para>
    /// This deliberately does NOT re-verify the genesis signature. That reconstruction already exists
    /// in <c>SystemRegisterSyncVerifier</c>, and writing a second one here is exactly the
    /// duplicate-implementation trap this feature keeps warning about — two implementations agree
    /// until they don't, and here the disagreement would surface as a false tamper report. Comparing
    /// the payload hash answers "is this the genesis my anchor describes" without duplicating any
    /// cryptography.
    /// </para>
    /// </remarks>
    private async Task<AnchorEvidence> AssembleAnchorAsync(string registerId, CancellationToken cancellationToken)
    {
        if (!_anchor.IsKnown)
        {
            return new AnchorEvidence { IsAnchorKnown = false };
        }

        var isSystemRegister = string.Equals(
            registerId,
            SystemRegisterConstants.SystemRegisterId,
            StringComparison.OrdinalIgnoreCase);

        if (!isSystemRegister)
        {
            return new AnchorEvidence
            {
                IsAnchorKnown = true,
                AnchorFingerprint = _anchor.GenesisPublicKeyFingerprint,
                OriginFingerprint = null,
                OriginDescription = "this register's genesis, attested by its owner",
                UntraceableReason =
                    $"this node's trust anchor covers the {_anchor.NetworkId ?? "network"} system register; " +
                    "this register's origin is attested by its owner rather than by the network genesis key, " +
                    "so there is nothing to trace it to (issue #1374)",
            };
        }

        var storedPayloadHash = await ReadStoredGenesisPayloadHashAsync(registerId, cancellationToken);
        var description =
            $"the payload of this node's stored system-register genesis transaction, compared with the " +
            $"genesis its {_anchor.NetworkId ?? "network"} trust anchor describes " +
            $"(anchor key fingerprint {_anchor.GenesisPublicKeyFingerprint})";

        if (storedPayloadHash is null)
        {
            return new AnchorEvidence
            {
                IsAnchorKnown = true,
                AnchorFingerprint = _anchor.GenesisPayloadHash,
                OriginFingerprint = null,
                OriginDescription = description,
                UntraceableReason =
                    "this node holds no readable system-register genesis transaction to compare against its anchor",
            };
        }

        var traces = string.Equals(
            storedPayloadHash, _anchor.GenesisPayloadHash, StringComparison.OrdinalIgnoreCase);

        if (traces)
        {
            return new AnchorEvidence
            {
                IsAnchorKnown = true,
                AnchorFingerprint = _anchor.GenesisPayloadHash,
                OriginFingerprint = storedPayloadHash,
                OriginDescription = description,
            };
        }

        // A DIFFERENCE HERE IS NOT EVIDENCE OF FORGERY, and must not be reported as one.
        //
        // From a single node there is no way to tell "this system register is not the one my anchor
        // covers" from "my anchor is not this network's". Both produce two known, differing hashes.
        // Reconciling across nodes is explicitly out of scope (spec: "One node's view"), so the
        // second reading cannot be ruled out — and it is by far the more likely: a node deployed
        // with a mounted genesis while its image carries the embedded one lands here immediately.
        //
        // That is precisely the state issue #1374 describes, and the spec is explicit that it "will
        // correctly report its origin check as unverifiable". Observed on n1: the node holds the
        // embedded sorcha-dev anchor while running the mounted n1-dev network, and reporting Failed
        // told an operator their healthy system register did not match its origin.
        //
        // So: Unverified, with BOTH hashes in the detail so the operator can see at a glance that
        // the networks differ. The engine keeps its Failed branch for when anchors are independently
        // configurable (#1374) and a mismatch becomes interpretable.
        return new AnchorEvidence
        {
            IsAnchorKnown = true,
            AnchorFingerprint = _anchor.GenesisPayloadHash,
            OriginFingerprint = null,
            OriginDescription =
                $"{description} · stored genesis payload hash {storedPayloadHash} · anchor expects " +
                $"{_anchor.GenesisPayloadHash}",
            UntraceableReason =
                $"this node's trust anchor describes the '{_anchor.NetworkId ?? "unknown"}' network, whose " +
                $"genesis payload hash does not match the system register this node actually holds. From " +
                $"one node this cannot be told apart from the node simply being configured with another " +
                $"network's anchor, which is the far more likely cause — see issue #1374",
        };
    }

    /// <summary>
    /// Hashes the stored genesis transaction's payload the same way the genesis ceremony did.
    /// </summary>
    /// <remarks>
    /// The transaction id is deterministic (<see cref="GenesisSignatureVerifier.ComputeGenesisTxId"/>),
    /// and the payload is stored base64 or base64url depending on the writer, so both are accepted —
    /// the same smart decode <c>GovernanceRosterService</c> and <c>SystemRegisterSyncVerifier</c> use.
    /// </remarks>
    private async Task<string?> ReadStoredGenesisPayloadHashAsync(
        string registerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var genesisTxId = GenesisSignatureVerifier.ComputeGenesisTxId();
            var transaction = await _repository.GetTransactionAsync(registerId, genesisTxId, cancellationToken);

            var data = transaction?.Payloads?.FirstOrDefault()?.Data;
            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            var bytes = data.Contains('+') || data.Contains('/') || data.Contains('=')
                ? Convert.FromBase64String(data)
                : System.Buffers.Text.Base64Url.DecodeFromChars(data);

            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not read the stored genesis transaction of register {RegisterId} while assembling anchor evidence",
                registerId);
            return null;
        }
    }

    /// <summary>
    /// Normalises storage's empty-string "absent" to the engine's null "absent".
    /// </summary>
    /// <remarks>
    /// Load-bearing: <c>DocketHeader.MerkleRoot</c> and <c>ProposerValidatorId</c> are non-nullable
    /// strings defaulting to empty, so a pre-Feature-187 docket arrives blank. Passing the blank
    /// through would have the engine compare it as a value rather than treat it as missing evidence.
    /// (The engine also guards against blanks, so this is belt and braces — deliberately, because the
    /// failure mode is a false tamper report.)
    /// </remarks>
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
