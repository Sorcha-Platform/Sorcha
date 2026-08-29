// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Provenance;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Models.Genesis;
using Sorcha.Validator.Service.Models;
using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// The single producer of <see cref="ExemptionDecision"/> — Feature 196, issue #1591.
/// </summary>
/// <remarks>
/// <para>
/// <b>What changed and why.</b> The validator waived six rules for a transaction that merely
/// <i>claimed</i> to be administrative, via either <c>Metadata["Type"]</c> or
/// <c>BlueprintId == "genesis"</c>. Neither is signed: the signed bytes are
/// <c>"{TransactionId}:{PayloadHash}"</c> and both of those are the same digest of the payload body,
/// so a submitter chooses those fields freely. <c>Control</c> was covered by a roster check keyed on
/// the same string, which made claiming it a trade rather than a win; <c>Genesis</c> and
/// <c>BlueprintPublish</c> substituted nothing at all.
/// </para>
/// <para>
/// <b>Why the discriminator was not simply moved into the signed payload</b> — the fix applied to the
/// lifecycle predicates in the 2026-07-29 review. That route is unavailable here for two of the three
/// values: a publication's signed payload <i>is</i> the canonical blueprint definition, so adding a
/// field to it would move every publication id on every register; and genesis's payload is a
/// pre-signed offline-ceremony artefact. Authority is derivable from the signer's key, which is
/// already signed material, so nothing on the ledger has to move.
/// </para>
/// <para>
/// <b>Signing a claim would not be enough anyway.</b> Signing makes a claim <i>attributable</i>; it
/// does not make it <i>authorised</i>. An attacker signing their own transaction produces a perfectly
/// valid signature over their own forged label. Since the exemption waives sender authorisation
/// itself, nothing downstream would then ask whether they were entitled to it.
/// </para>
/// <para>
/// <b>This changes who may claim an exemption, never what an exemption does.</b> Two of the six
/// waivers are load-bearing for governance quorum (F189 T054): approvals share a predecessor, a shape
/// only the fork bypass permits, and the chain-derived sender binding would otherwise treat the
/// second approver as an impostor. Narrowing either makes quorum unattainable.
/// </para>
/// </remarks>
public sealed class ExemptionAuthorityResolver : IExemptionAuthorityResolver
{
    private readonly INodeTrustAnchor _anchor;
    private readonly IGovernanceRosterService _rosterService;
    private readonly ILogger<ExemptionAuthorityResolver> _logger;
    private readonly ExemptionMetrics? _metrics;

    /// <summary>
    /// Per-scope memoisation. The decision is consulted from several points in the pipeline
    /// (sequence replay, schema, blueprint conformance, routing, crypto policy, timing) and each
    /// consultation would otherwise re-walk the register's control chain to rebuild the roster —
    /// turning one O(n) walk into six. The resolver is scoped, so this is bounded by the request.
    /// </summary>
    private readonly Dictionary<string, ExemptionDecision> _memo = new(StringComparer.Ordinal);

    /// <summary>Creates the resolver.</summary>
    public ExemptionAuthorityResolver(
        INodeTrustAnchor anchor,
        IGovernanceRosterService rosterService,
        ILogger<ExemptionAuthorityResolver> logger,
        ExemptionMetrics? metrics = null)
    {
        _anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
        _rosterService = rosterService ?? throw new ArgumentNullException(nameof(rosterService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
    }

    /// <inheritdoc/>
    public async Task<ExemptionDecision> ResolveAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (_memo.TryGetValue(transaction.TransactionId, out var memoised))
        {
            return memoised;
        }

        var claim = ReadClaim(transaction);

        if (!claim.IsClaimed)
        {
            var noClaim = ExemptionDecision.NoClaim(claim);
            _memo[transaction.TransactionId] = noClaim;
            return noClaim;
        }

        ExemptionDecision decision;
        try
        {
            decision = claim.Kind switch
            {
                ExemptionKind.Genesis => ResolveGenesis(transaction, claim),
                ExemptionKind.RegisterGenesis => await ResolveRegisterGenesisAsync(transaction, claim, ct),
                ExemptionKind.Control => await ResolveControlAsync(transaction, claim, ct),
                ExemptionKind.BlueprintPublish => await ResolveBlueprintPublishAsync(transaction, claim, ct),

                // Unreachable while ExemptionKindCoverageTests passes: every value must be handled.
                // Reached only if a value is added without a rule — fail closed, never grant.
                _ => ExemptionDecision.NotEntitled(claim, $"No authority rule for kind {claim.Kind}")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // FR-007: a node that could not consult the authority has not checked it. Withhold.
            _logger.LogError(ex,
                "Could not resolve exemption authority for transaction {TransactionId} on register "
                + "{RegisterId} claiming {ClaimedKind}; withholding the exemption",
                transaction.TransactionId, transaction.RegisterId, claim.Kind);

            decision = ExemptionDecision.Unresolvable(claim, ex.Message);
        }

        RecordOutcome(transaction, decision);
        _memo[transaction.TransactionId] = decision;
        return decision;
    }

    /// <summary>
    /// Reads what the transaction <b>asserts</b>. Every input here is untrusted.
    /// </summary>
    /// <remarks>
    /// Both routes are read, because they are independent: <c>BlueprintId == "genesis"</c> grants the
    /// same six waivers without touching metadata, so closing only the metadata route closes nothing.
    /// </remarks>
    public static ExemptionClaim ReadClaim(Transaction transaction)
    {
        // Route 1 — the blueprint-identifier route. Checked first because it is the arm the original
        // predicate evaluated first, and it needs no metadata at all.
        if (string.Equals(transaction.BlueprintId, GenesisConstants.BlueprintId, StringComparison.OrdinalIgnoreCase))
        {
            return new ExemptionClaim(
                IsSystemRegister(transaction) ? ExemptionKind.Genesis : ExemptionKind.RegisterGenesis,
                ExemptionClaimRoute.BlueprintIdentifier,
                transaction.BlueprintId);
        }

        if (!transaction.Metadata.TryGetValue("Type", out var label) || string.IsNullOrWhiteSpace(label))
        {
            return ExemptionClaim.None;
        }

        // Effective-kind disambiguation (#917): a blueprint PUBLISH of the governance blueprint is a
        // system seed, not a governance operation, and is distinguished by a SECOND key —
        // `transactionType`, not `Type`. Authority must be judged against what the transaction
        // effectively is, or a publication is measured against governance-roster authority it never
        // had. RightsEnforcementService already draws this same distinction.
        var effectiveLabel = label;
        if (transaction.Metadata.TryGetValue("transactionType", out var seedType)
            && string.Equals(seedType, "BlueprintPublish", StringComparison.OrdinalIgnoreCase))
        {
            effectiveLabel = "BlueprintPublish";
        }

        ExemptionKind? kind = effectiveLabel switch
        {
            // The SSR's genesis and an ordinary register's genesis carry the SAME label and are
            // told apart by which register they are on. Only the system register can hold the
            // network trust anchor, so anything else claiming genesis is a register genesis and is
            // judged by the far narrower "this register has no roster yet" rule.
            _ when string.Equals(effectiveLabel, "Genesis", StringComparison.OrdinalIgnoreCase)
                => IsSystemRegister(transaction) ? ExemptionKind.Genesis : ExemptionKind.RegisterGenesis,
            _ when string.Equals(effectiveLabel, "Control", StringComparison.OrdinalIgnoreCase)
                => ExemptionKind.Control,
            _ when string.Equals(effectiveLabel, "BlueprintPublish", StringComparison.OrdinalIgnoreCase)
                => ExemptionKind.BlueprintPublish,
            _ => null
        };

        return kind is null
            ? ExemptionClaim.None
            : new ExemptionClaim(kind, ExemptionClaimRoute.TypeLabel, label);
    }

    /// <summary>
    /// Genesis authority: the network's single genesis transaction, signed by the network's genesis
    /// key.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The transaction-id check alone is <b>not sufficient</b>, and it is important to understand why.
    /// <c>ComputeGenesisTxId()</c> is <c>SHA-256("genesis-{SystemRegisterId}")</c> — a compile-time
    /// constant, so there is exactly one valid genesis transaction id ever. But an attacker may set
    /// that id on a transaction carrying their own payload with a matching <c>PayloadHash</c>, and
    /// sign it with their own key: the signature verifies, because it is genuinely theirs. The
    /// anchor fingerprint is what actually closes the route.
    /// </para>
    /// <para>
    /// A node holding no anchor cannot tell, so it withholds (FR-007). That is not a regression for
    /// bootstrap: <c>GenesisIngestionService</c> already verifies the anchor before submitting, and a
    /// node with no anchor has no genesis to ingest.
    /// </para>
    /// </remarks>
    private ExemptionDecision ResolveGenesis(Transaction transaction, ExemptionClaim claim)
    {
        var expectedTxId = GenesisSignatureVerifier.ComputeGenesisTxId();

        if (!string.Equals(transaction.TransactionId, expectedTxId, StringComparison.OrdinalIgnoreCase))
        {
            return ExemptionDecision.NotEntitled(claim,
                $"transaction id is not the network's genesis transaction id ({expectedTxId})");
        }

        if (!string.Equals(transaction.RegisterId, SystemRegisterConstants.SystemRegisterId, StringComparison.OrdinalIgnoreCase))
        {
            return ExemptionDecision.NotEntitled(claim,
                "genesis exists only on the system register");
        }

        if (!_anchor.IsKnown || string.IsNullOrWhiteSpace(_anchor.GenesisPublicKeyFingerprint))
        {
            return ExemptionDecision.Unresolvable(claim,
                "this node holds no trust anchor, so it cannot verify a genesis claim (see #1374)");
        }

        foreach (var signature in transaction.Signatures)
        {
            if (signature.PublicKey is not { Length: > 0 })
            {
                continue;
            }

            var fingerprint = GenesisFileLoader.ComputeFingerprint(signature.PublicKey);
            if (string.Equals(fingerprint, _anchor.GenesisPublicKeyFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                return ExemptionDecision.Grant(ExemptionKind.Genesis, claim);
            }
        }

        return ExemptionDecision.NotEntitled(claim,
            "no signature is from the genesis key this node is anchored on");
    }

    /// <summary>Whether a transaction is on the system register.</summary>
    private static bool IsSystemRegister(Transaction transaction) =>
        string.Equals(
            transaction.RegisterId,
            SystemRegisterConstants.SystemRegisterId,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ordinary-register genesis authority: the register has no roster yet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A register genesis is the transaction that CREATES the roster, so there is no prior authority
    /// to check it against. <c>RightsEnforcementService</c> already resolves the same chicken-and-egg
    /// the same way (F189 R-002) — this states it as the exemption's own rule rather than leaving it
    /// implicit.
    /// </para>
    /// <para>
    /// <b>This is a narrowing, not a free pass.</b> It can be claimed at most once per register and
    /// never on one that has already sealed a roster — which is where a forged claim would be worth
    /// anything, since that is where real workflow traffic lives. What it replaces was an
    /// unconditional grant on every register for the register's whole life.
    /// </para>
    /// <para>
    /// It deliberately does NOT require the system-register id, the constant genesis transaction id,
    /// or the network trust anchor. Requiring those is what broke every register creation on the
    /// network: only the SSR's own genesis can satisfy them, and every other register's genesis then
    /// failed validation, never sealed, and left an empty roster behind.
    /// </para>
    /// </remarks>
    private async Task<ExemptionDecision> ResolveRegisterGenesisAsync(
        Transaction transaction, ExemptionClaim claim, CancellationToken ct)
    {
        var roster = await _rosterService.GetCurrentRosterAsync(transaction.RegisterId, ct);

        if (roster is not null)
        {
            return ExemptionDecision.NotEntitled(claim,
                "this register already has a governance roster, so it is past its genesis — a "
                + "transaction cannot claim to create a roster that already exists");
        }

        return ExemptionDecision.Grant(ExemptionKind.RegisterGenesis, claim);
    }

    /// <summary>
    /// Control authority: the signer is on the register's governance roster.
    /// </summary>
    /// <remarks>
    /// This check already existed in <c>RightsEnforcementService</c>, keyed on the same string as the
    /// exemption. Feature 196 makes the exemption <i>derive from</i> it rather than run alongside it,
    /// so the two can no longer drift apart. Behaviour is unchanged.
    /// </remarks>
    private async Task<ExemptionDecision> ResolveControlAsync(
        Transaction transaction, ExemptionClaim claim, CancellationToken ct)
    {
        var roster = await _rosterService.GetCurrentRosterAsync(transaction.RegisterId, ct);

        if (roster is null)
        {
            // A register genuinely has no roster until its genesis creates one (F189 R-002). That
            // allowance belongs to the genesis transaction only, and genesis is a different kind with
            // its own rule above, so a Control claim with no roster has no authority behind it.
            return ExemptionDecision.NotEntitled(claim,
                "the register has no governance roster, so there is no authority to check against");
        }

        var attestations = roster.ControlRecord?.Attestations;
        if (attestations is null || attestations.Count == 0)
        {
            return ExemptionDecision.NotEntitled(claim,
                "the register's governance roster carries no attestations");
        }

        foreach (var signature in transaction.Signatures)
        {
            if (attestations.Any(a => GovernanceKeyMatcher.Matches(a.PublicKey, signature.PublicKey)))
            {
                return ExemptionDecision.Grant(ExemptionKind.Control, claim);
            }
        }

        return ExemptionDecision.NotEntitled(claim,
            "no signature matches a member of the register's governance roster");
    }

    /// <summary>
    /// Blueprint-publication authority: the signer is on the register's <b>validator</b> roster under
    /// the <see cref="SorchaDerivationPaths.BlueprintPublish"/> derivation context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the governance roster.</b> A publication is signed by the NODE's system wallet at
    /// <see cref="SorchaDerivationPaths.RegisterControl"/> (slot 101), whereas the governance roster
    /// is built from genesis attestations recording ORGANISATION keys (slot 100). The node is never
    /// on it — that asymmetry is exactly why <c>GovernanceSigningService</c> exists. So the
    /// governance roster cannot answer this question.
    /// </para>
    /// <para>
    /// <b>Nor the validating node's own configured wallet</b>, which would be wrong rather than
    /// merely narrow: a replica re-validates transactions gossiped from peers, so matching against
    /// "my own system wallet" would accept a publication on the node that made it and refuse the
    /// same publication everywhere else, silently partitioning the register.
    /// </para>
    /// <para>
    /// The validator roster is the per-register, replicated, governance-updatable registry of node
    /// purpose-keys, and publication is a node duty exactly like docket signing, which it already
    /// governs.
    /// </para>
    /// <para>
    /// <b>Why the dedicated <c>sorcha:blueprint-publish</c> context and not <c>register-control</c>.</b>
    /// The two publish paths originally disagreed: the per-register endpoint signed with
    /// <c>register-control</c> while system-register seeding signed with <c>blueprint-publish</c>, so
    /// no single context could authorise both. Feature 196 unified them on the dedicated one, which
    /// is what the derivation constants were designed for and keeps a node's general control
    /// authority distinct from its right to publish definitions — a compromise of one is then not a
    /// compromise of the other. Unifying was free only because the platform is pre-release and the
    /// estate could be wiped; after release it would have needed a dual-accept transition.
    /// </para>
    /// </remarks>
    private async Task<ExemptionDecision> ResolveBlueprintPublishAsync(
        Transaction transaction, ExemptionClaim claim, CancellationToken ct)
    {
        var roster = await _rosterService.GetCurrentRosterAsync(transaction.RegisterId, ct);

        if (roster is null)
        {
            return ExemptionDecision.NotEntitled(claim,
                "the register has no roster, so there is no publishing authority to check against");
        }

        var validators = roster.ControlRecord?.Validators?.Validators;
        if (validators is null || validators.Count == 0)
        {
            return ExemptionDecision.NotEntitled(claim,
                "the register's validator roster is empty");
        }

        var publishers = validators
            .Where(v => v.Status == ValidatorKeyStatus.Active)
            .Where(v => string.Equals(
                v.DerivationContext, SorchaDerivationPaths.BlueprintPublish, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (publishers.Count == 0)
        {
            return ExemptionDecision.NotEntitled(claim,
                $"the register's validator roster carries no active entry under "
                + $"'{SorchaDerivationPaths.BlueprintPublish}', so no node is authorised to publish to it");
        }

        foreach (var signature in transaction.Signatures)
        {
            if (publishers.Any(v => GovernanceKeyMatcher.Matches(v.PublicKey, signature.PublicKey)))
            {
                return ExemptionDecision.Grant(ExemptionKind.BlueprintPublish, claim);
            }
        }

        return ExemptionDecision.NotEntitled(claim,
            "no signature matches an authorised publisher on the register's validator roster");
    }

    /// <summary>
    /// FR-013: a refused claim is recorded distinctly from an ordinary validation failure, because it
    /// is what an attempted bypass looks like on the wire — and "not entitled" is separated from
    /// "could not tell", because those call for different operator responses.
    /// </summary>
    private void RecordOutcome(Transaction transaction, ExemptionDecision decision)
    {
        if (!decision.IsRefusedClaim)
        {
            if (decision.Granted)
            {
                _logger.LogDebug(
                    "Exemption {Kind} granted to transaction {TransactionId} on register {RegisterId}",
                    decision.Kind, transaction.TransactionId, transaction.RegisterId);
            }

            return;
        }

        _logger.LogWarning(
            "Exemption REFUSED: transaction {TransactionId} on register {RegisterId} claimed {ClaimedKind} "
            + "via {Route} but {Reason} — {Detail}. The claim is not being honoured; ordinary validation applies.",
            transaction.TransactionId,
            transaction.RegisterId,
            decision.Claim.Kind,
            decision.Claim.Route,
            decision.RefusalReason,
            decision.Detail);

        _metrics?.RecordRefusedClaim(
            decision.Claim.Kind?.ToString() ?? "unknown",
            decision.Claim.Route.ToString(),
            decision.RefusalReason.ToString());
    }
}
