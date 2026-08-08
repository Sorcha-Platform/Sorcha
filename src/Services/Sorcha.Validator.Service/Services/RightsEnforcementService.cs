// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Diagnostics;
using System.Text.Json;
using Sorcha.Cryptography;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Validator.Service.Diagnostics;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Enforces governance rights for Control transactions by reconstructing the admin
/// roster from the register's Control transaction chain and verifying the submitter
/// has the required role. Zero dependency on Tenant Service.
/// </summary>
public class RightsEnforcementService : IRightsEnforcementService
{
    private readonly IGovernanceRosterService _rosterService;
    private readonly ICryptoModule _cryptoModule;
    private readonly ILogger<RightsEnforcementService> _logger;

    /// <summary>
    /// Optional so existing construction sites (and tests) are unaffected. Metrics are
    /// observability, never a precondition for an authorisation decision.
    /// </summary>
    private readonly GovernanceMetrics? _metrics;

    /// <summary>
    /// The governance blueprint ID used to identify Control transactions
    /// </summary>
    public const string GovernanceBlueprintId = GovernanceBlueprint.BlueprintId;

    public RightsEnforcementService(
        IGovernanceRosterService rosterService,
        ICryptoModule cryptoModule,
        ILogger<RightsEnforcementService> logger,
        GovernanceMetrics? metrics = null)
    {
        _rosterService = rosterService ?? throw new ArgumentNullException(nameof(rosterService));
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
    }

    /// <inheritdoc/>
    public async Task<ValidationEngineResult> ValidateGovernanceRightsAsync(
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using var _section = RuleTelemetry.TimeSection("GovernanceRights");
        var sw = Stopwatch.StartNew();
        var errors = new List<ValidationEngineError>();
        var authorisedSignerCount = 0;

        // Only enforce governance on Control transactions (identified by governance blueprint)
        if (!IsGovernanceTransaction(transaction))
        {
            return ValidationEngineResult.Success(
                transaction.TransactionId,
                transaction.RegisterId,
                sw.Elapsed);
        }

        _logger.LogDebug(
            "Validating governance rights for transaction {TransactionId} on register {RegisterId}",
            transaction.TransactionId, transaction.RegisterId);

        try
        {
            // Get the current admin roster for this register
            var roster = await _rosterService.GetCurrentRosterAsync(
                transaction.RegisterId, ct);

            if (roster == null)
            {
                // Feature 189 (R-002): a register genuinely has no roster until its genesis creates
                // one, so the genesis transaction must be admitted here. But this allowance used to
                // apply to ANY control transaction, which meant every governance operation was
                // admitted unchecked during the window between register creation and the genesis
                // docket sealing. That is not theoretical: a live DevMode promotion "passed" through
                // exactly this window and was mistaken for the feature working.
                //
                // Narrowed to the transaction that CREATES the roster. Anything else with no roster
                // fails closed — there is no authority to check it against.
                if (TransactionTypeClassifier.IsGenesisTransaction(transaction))
                {
                    _logger.LogInformation(
                        "No existing roster for register {RegisterId} — allowing genesis Control TX {TransactionId}",
                        transaction.RegisterId, transaction.TransactionId);
                    return ValidationEngineResult.Success(
                        transaction.TransactionId,
                        transaction.RegisterId,
                        sw.Elapsed);
                }

                _logger.LogWarning(
                    "Transaction {TransactionId} on register {RegisterId}: no governance roster exists and this is not the genesis transaction — refusing",
                    transaction.TransactionId, transaction.RegisterId);
                _metrics?.RecordRefused(transaction.RegisterId, "no-roster");
                errors.Add(CreateError("VAL_PERM_007",
                    "The register has no governance roster and this is not its genesis transaction, so there is no authority to authorise it against.",
                    "RegisterId", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Extract the submitter's public key from the first signature
            if (transaction.Signatures.Count == 0)
            {
                _metrics?.RecordRefused(transaction.RegisterId, "no-signature");
                errors.Add(CreateError("VAL_PERM_001",
                    "Control transaction must have at least one signature",
                    "Signatures", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Feature 189 (R-003 + FR-005): match EVERY signature against the roster, by decoded key
            // BYTES.
            //
            // Two defects are corrected here at once, and they matter independently:
            //
            //  * Only Signatures[0] was examined, so a multi-signature governance transaction could
            //    never be authorised by anyone but its first signer — multi-party governance was
            //    unenforceable even though the wire carries a signature list.
            //  * The comparison was a STRING equality between the roster's standard base64
            //    (padded, "+/" alphabet) and Base64Url.EncodeToString (unpadded, "-_"). Those cannot
            //    be equal for any key requiring padding or containing '+' or '/', so a correct key
            //    failed to match. Observed live: a roster key of
            //    fFE+9QNpjWLk9+hPDXbfIFctbmex6ONxaOnMVUAkjWA= contains '+'.
            //
            // Both surfaced identically as "submitter not found in roster", so fixing either alone
            // leaves the same symptom and reads as an unfixed bug.
            var matchedAttestations = new List<RegisterAttestation>();
            foreach (var signature in transaction.Signatures)
            {
                var match = roster.ControlRecord.Attestations.FirstOrDefault(
                    a => GovernanceKeyMatcher.Matches(a.PublicKey, signature.PublicKey));

                // Distinct roster members only — one member signing twice is one authority, not two.
                if (match is not null && !matchedAttestations.Any(
                        m => string.Equals(m.Subject, match.Subject, StringComparison.Ordinal)))
                {
                    matchedAttestations.Add(match);
                }
            }

            if (matchedAttestations.Count == 0)
            {
                _logger.LogWarning(
                    "Transaction {TransactionId} on register {RegisterId}: none of {SignatureCount} signature(s) match a roster member",
                    transaction.TransactionId, transaction.RegisterId, transaction.Signatures.Count);
                _metrics?.RecordRefused(transaction.RegisterId, "no-roster-match");
                errors.Add(CreateError("VAL_PERM_002",
                    "No signature on this transaction matches a member of the register's admin roster",
                    "Signatures"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // At least one matched member must hold a governance role. A matched member without one
            // is not an authorisation — it is a bystander's signature.
            var governanceSigners = matchedAttestations
                .Where(a => a.Role is RegisterRole.Owner or RegisterRole.Admin)
                .ToList();

            if (governanceSigners.Count == 0)
            {
                var roles = string.Join(", ", matchedAttestations.Select(a => a.Role));
                _logger.LogWarning(
                    "Transaction {TransactionId} on register {RegisterId}: matched roster member(s) hold role(s) {Roles}, requires Owner or Admin",
                    transaction.TransactionId, transaction.RegisterId, roles);
                _metrics?.RecordRefused(transaction.RegisterId, "no-governance-role");
                errors.Add(CreateError("VAL_PERM_003",
                    $"Signing roster member(s) hold role(s) '{roles}', which cannot execute governance operations. Requires Owner or Admin.",
                    "Signatures"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Prefer an Owner as the nominal submitter so downstream owner-override logic behaves as
            // it did when only the first signature was considered.
            var submitterAttestation =
                governanceSigners.FirstOrDefault(a => a.Role == RegisterRole.Owner)
                ?? governanceSigners[0];

            // Feature 189 (T027 / FR-005): how many DISTINCT roster organisations must have signed.
            // For everything that takes the Owner override this is 1, so single-organisation
            // registers behave exactly as they do today. It rises only where the override does not
            // apply — notably Transfer, which must never be completed on the proposing owner's
            // authority alone (FR-010).
            authorisedSignerCount = governanceSigners.Count;

            // Parsed once. The whole payload is needed, not just the operation: whether this
            // transaction ENACTS anything is decided by the presence of a roster, and that governs
            // both the signer threshold below and the quorum check further down.
            var controlPayload = TryParseControlPayload(transaction);

            // Feature 189: a transaction carrying NO roster changes no roster, so it is a proposal
            // being RAISED, not a change being made. It cannot carry the approvals it exists to
            // collect, and demanding them refuses every multi-party proposal at birth.
            //
            // The discriminator is the absence of a roster, NOT Operation.Status. Status was the
            // obvious choice and is wrong: ProposalStatus.Pending is 0, so a payload that simply
            // omits the field reads as Pending and would skip quorum on a transaction that DOES
            // carry a roster. Caught by ValidateGovernanceRightsAsync_NonOwnerWithoutQuorum_Rejected,
            // whose operation never sets Status. Absence of a roster cannot be faked into a change:
            // carry no roster, move no roster.
            var enactsNothing = controlPayload is not null && controlPayload.Roster is null;

            var requiredSigners = enactsNothing
                ? 1
                : ResolveRequiredDistinctSigners(roster, controlPayload?.Operation);

            if (governanceSigners.Count < requiredSigners)
            {
                _logger.LogWarning(
                    "Transaction {TransactionId} on register {RegisterId}: {Actual} distinct roster signature(s), requires {Required}",
                    transaction.TransactionId, transaction.RegisterId, governanceSigners.Count, requiredSigners);
                _metrics?.RecordRefused(transaction.RegisterId, "insufficient-signers");
                errors.Add(CreateError("VAL_PERM_008",
                    $"This governance operation requires {requiredSigners} distinct roster organisation(s) to sign, " +
                    $"but only {governanceSigners.Count} did. Collect the remaining approval(s) and resubmit.",
                    "Signatures"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Deeper validation against the parsed operation.
            var operation = controlPayload?.Operation;
            if (operation != null)
            {
                // Feature 189 (FR-011b): a proposal is judged against the roster it was RAISED
                // against. If the register's roster-establishing transaction has moved on, the
                // proposal is invalidated rather than re-counted.
                //
                // Re-counting against the current roster is the dangerous alternative: under
                // unanimity, removing the one organisation yet to approve would take the requirement
                // below the approvals already collected and ENACT the very change it was blocking.
                // Roster removal would become an attack on every open proposal. Invalidation makes
                // that impossible, and it is a comparison — no timer, no sweeper, and identical on
                // every node, which the Feature 145 projection requires.
                if (!string.IsNullOrEmpty(operation.RosterSnapshotId) &&
                    !string.Equals(operation.RosterSnapshotId, roster.LastControlTxId, StringComparison.Ordinal))
                {
                    _logger.LogWarning(
                        "Transaction {TransactionId} on register {RegisterId}: proposal was raised against roster {Raised} but the register is now on {Current} — invalidated",
                        transaction.TransactionId, transaction.RegisterId,
                        operation.RosterSnapshotId, roster.LastControlTxId);
                    _metrics?.RecordRefused(transaction.RegisterId, "roster-changed");
                    errors.Add(CreateError("VAL_PERM_009",
                        "The register's governance roster changed after this proposal was raised, so the " +
                        "proposal is no longer valid. Raise it again against the current roster.",
                        "Payload.Operation.RosterSnapshotId"));
                    return CreateFailureResult(transaction, sw.Elapsed, errors);
                }

                // Feature 189 (FR-024): never let a governance change strand a register with nobody
                // able to govern it. A register with no Owner or Admin cannot be recovered by any
                // later governance operation, because every such operation needs one to authorise it.
                if (operation.OperationType == GovernanceOperationType.Remove &&
                    WouldLeaveRegisterUngovernable(roster, operation.TargetDid))
                {
                    _logger.LogWarning(
                        "Transaction {TransactionId} on register {RegisterId}: removing {Target} would leave no governing member",
                        transaction.TransactionId, transaction.RegisterId, operation.TargetDid);
                    _metrics?.RecordRefused(transaction.RegisterId, "would-strand-register");
                    errors.Add(CreateError("VAL_PERM_010",
                        "This removal would leave the register with no Owner or Admin, making it permanently " +
                        "ungovernable. Add a replacement governing member first.",
                        "Payload.Operation.TargetDid"));
                    return CreateFailureResult(transaction, sw.Elapsed, errors);
                }

                // Validate proposal rules using the roster service
                var proposalResult = _rosterService.ValidateProposal(roster, operation);
                if (!proposalResult.IsValid)
                {
                    foreach (var error in proposalResult.Errors)
                    {
                        errors.Add(CreateError("VAL_PERM_004", error, "Payload"));
                    }
                }

                // For non-Owner Add/Remove: verify quorum is included.
                //
                // Feature 189: a transaction that enacts nothing is exempt, and must be. A proposal
                // is raised so approvals have something to attach to; it carries no roster and
                // changes nothing, so there is nothing for quorum to authorise. Applying this check
                // to one refuses every multi-party proposal at birth — the same dead end that was in
                // the endpoint, moved into the validator.
                //
                // See `enactsNothing` above for why this keys on the absence of a roster rather than
                // on Operation.Status.
                if (!enactsNothing &&
                    submitterAttestation.Role != RegisterRole.Owner &&
                    operation.OperationType is GovernanceOperationType.Add or GovernanceOperationType.Remove)
                {
                    if (operation.ApprovalSignatures == null || operation.ApprovalSignatures.Count == 0)
                    {
                        errors.Add(CreateError("VAL_PERM_005",
                            "Non-owner governance operations require quorum approval signatures",
                            "Payload.ApprovalSignatures"));
                    }
                    else
                    {
                        using var _quorumScope = RuleTelemetry.TimeRule("VAL_PERM_006");

                        // Feature 189 US2-A: count only CRYPTOGRAPHICALLY VERIFIED approvals.
                        //
                        // ApprovalSignature has always carried a Signature field, and nothing ever
                        // checked it: ValidateQuorumAsync counted an approval whose ApproverDid
                        // merely appeared in the roster. Quorum was therefore satisfied by ASSERTING
                        // approvals — whoever composed the payload could claim every other member's
                        // vote, with no cryptography involved anywhere. Verification happens here
                        // rather than in the roster service because Sorcha.Register.Core is
                        // deliberately crypto-free; only verified approvals are passed down.
                        var verifiedApprovals = await VerifyApprovalsAsync(
                            transaction.RegisterId, roster, operation, ct);

                        var forged = operation.ApprovalSignatures.Count - verifiedApprovals.Count;
                        if (forged > 0)
                        {
                            _logger.LogWarning(
                                "Transaction {TransactionId} on register {RegisterId}: {Forged} of {Total} approval signature(s) failed verification and were discarded",
                                transaction.TransactionId, transaction.RegisterId,
                                forged, operation.ApprovalSignatures.Count);
                            _metrics?.RecordRefused(transaction.RegisterId, "approval-signature-invalid");
                        }

                        var quorumResult = await _rosterService.ValidateQuorumAsync(
                            transaction.RegisterId, operation, verifiedApprovals, ct);

                        if (!quorumResult.IsQuorumMet)
                        {
                            errors.Add(CreateError("VAL_PERM_006",
                                $"Quorum not met: {quorumResult.VotesReceived}/{quorumResult.VotesRequired} votes received (pool: {quorumResult.VotingPool})",
                                "Payload.ApprovalSignatures"));
                        }
                    }
                }
            }

            _logger.LogDebug(
                "Governance rights check for {TransactionId}: submitter={Subject}, role={Role}, errors={ErrorCount}",
                transaction.TransactionId, submitterAttestation.Subject, submitterAttestation.Role, errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error validating governance rights for transaction {TransactionId} on register {RegisterId}",
                transaction.TransactionId, transaction.RegisterId);
            errors.Add(CreateError("VAL_PERM_ERR",
                $"Governance validation error: {ex.Message}",
                isFatal: true));
        }

        if (errors.Count > 0)
        {
            // Late failures (invalid proposal / quorum) land here rather than returning early.
            _metrics?.RecordRefused(transaction.RegisterId, "proposal-invalid");
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        _metrics?.RecordAuthorised(transaction.RegisterId, authorisedSignerCount);

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    /// <summary>
    /// Determines if a transaction is a governance (Control) transaction by checking
    /// the blueprint ID or transaction metadata.
    /// </summary>
    /// <summary>
    /// True when removing <paramref name="targetDid"/> would leave the register with no member
    /// holding a governing role (FR-024).
    /// </summary>
    /// <remarks>
    /// This is unrecoverable rather than merely inconvenient: every governance operation requires an
    /// Owner or Admin to authorise it, so a register with none can never be given one back. It is
    /// the one governance outcome with no remedy, which is why it is refused rather than warned about.
    /// </remarks>
    private static bool WouldLeaveRegisterUngovernable(AdminRoster roster, string? targetDid)
    {
        if (string.IsNullOrWhiteSpace(targetDid))
        {
            return false;
        }

        var remaining = roster.ControlRecord.Attestations
            .Where(a => a.Role is RegisterRole.Owner or RegisterRole.Admin)
            .Count(a => !string.Equals(a.Subject, targetDid, StringComparison.Ordinal));

        return remaining == 0;
    }

    /// <summary>
    /// Returns only those approvals whose signature verifies against the approving organisation's
    /// roster key, over the canonical approval statement.
    /// </summary>
    /// <remarks>
    /// Fails closed on every uncertainty — an unknown approver, an unparseable algorithm, an
    /// undecodable key or signature, or a verification error all discard the approval rather than
    /// counting it. A vote that cannot be proven is not a vote.
    /// </remarks>
    private async Task<List<ApprovalSignature>> VerifyApprovalsAsync(
        string registerId,
        AdminRoster roster,
        GovernanceOperation operation,
        CancellationToken ct)
    {
        var verified = new List<ApprovalSignature>();
        if (operation.ApprovalSignatures is null)
        {
            return verified;
        }

        foreach (var approval in operation.ApprovalSignatures)
        {
            // The approver must be on the roster, and we verify against THAT member's key — never
            // against a key supplied alongside the approval, which would let an approval carry its
            // own forged authority.
            var attestation = roster.ControlRecord.Attestations.FirstOrDefault(
                a => string.Equals(a.Subject, approval.ApproverDid, StringComparison.Ordinal));

            if (attestation is null)
            {
                continue;
            }

            if (!GovernanceKeyMatcher.TryDecode(attestation.PublicKey, out var publicKey) ||
                !GovernanceKeyMatcher.TryDecode(approval.Signature, out var signatureBytes))
            {
                continue;
            }

            if (!Enum.TryParse<WalletNetworks>(attestation.Algorithm.ToString(), ignoreCase: true, out var network))
            {
                continue;
            }

            var digest = GovernanceApprovalStatement.ComputeDigest(
                registerId, operation, approval.ApproverDid, approval.IsApproval);

            try
            {
                var status = await _cryptoModule.VerifyAsync(
                    signatureBytes, digest, (byte)network, publicKey, ct);

                if (status == CryptoStatus.Success)
                {
                    verified.Add(approval);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Approval from {ApproverDid} on register {RegisterId} could not be verified — discarding",
                    approval.ApproverDid, registerId);
            }
        }

        return verified;
    }

    /// <summary>
    /// Resolves how many distinct roster organisations must sign a governance transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <c>1</c> for every operation that takes the Owner override, which is the
    /// overwhelming majority and covers every single-organisation register — so this cannot change
    /// the behaviour User Story 1 depends on.
    /// </para>
    /// <para>
    /// It rises above 1 only for <see cref="GovernanceOperationType.Transfer"/>, which
    /// <c>GovernanceRosterService.ValidateQuorumAsync</c> deliberately excludes from the override:
    /// ownership must never move on the proposing owner's say-so alone (FR-010). The threshold then
    /// comes from the register's own configured rule, so a consortium demanding unanimity gets
    /// unanimity.
    /// </para>
    /// </remarks>
    private static int ResolveRequiredDistinctSigners(
        AdminRoster roster,
        GovernanceOperation? operation)
    {
        // Scoped deliberately to Transfer, and to nothing else.
        //
        // Add/Remove already have their own quorum path, where approvals ride in the PAYLOAD as
        // operation.ApprovalSignatures and are checked against VAL_PERM_005 / VAL_PERM_006. Applying
        // a transaction-level signature threshold to those would pre-empt that check, change a
        // long-established refusal code out from under any operator tooling keyed on it, and impose
        // a multi-signature model the platform does not yet use. Approvals become ledger
        // transactions in User Story 2; the threshold generalises there, not here.
        //
        // Transfer is the one case that needs it now: ValidateQuorumAsync explicitly excludes it
        // from the Owner override, because ownership must never move on the proposing owner's own
        // say-so (FR-010).
        if (operation is null || operation.OperationType != GovernanceOperationType.Transfer)
        {
            return 1;
        }

        // Fall back to the platform default rather than inventing a stricter one when a register
        // carries no explicit policy — tightening silently would break existing registers.
        var formula = roster.ControlRecord.RegisterPolicy?.Governance?.QuorumFormula
                      ?? QuorumFormula.StrictMajority;

        var threshold = roster.ControlRecord.GetQuorumThreshold(formula: formula);
        return Math.Max(1, threshold);
    }

    private static bool IsGovernanceTransaction(Transaction transaction)
    {
        // A blueprint PUBLISH (transactionType "BlueprintPublish") is a system seed, never a
        // governance roster-operation — even when the blueprint being published IS
        // register-governance-v1. Genuine governance operations carry transactionType
        // "GovernanceOperation" with a ControlTransactionPayload, not a blueprint body, and
        // an empty BlueprintId. Without this guard the one-time bootstrap publish of the
        // governance blueprint matches the GovernanceBlueprintId check below and is rejected
        // VAL_PERM_002 (the system blueprint-publish key is not a roster member), silently
        // dropping register-governance-v1 from the system register. (#917 — real root cause;
        // the prior seal-wait fix targeted a stale-head fork that was never the actual cause.)
        if (transaction.Metadata.TryGetValue("transactionType", out var seedType) &&
            string.Equals(seedType, "BlueprintPublish", StringComparison.OrdinalIgnoreCase))
            return false;

        // Check if the blueprint ID matches the governance blueprint
        if (string.Equals(transaction.BlueprintId, GovernanceBlueprintId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Feature 189 (R-004): the discriminator carrying "Control" is Metadata["Type"] — the same key
        // DocketRegisterProjection.ResolveTransactionType reads. This previously keyed on
        // Metadata["transactionType"], which across the platform carries values like
        // "GovernanceOperation" / "CryptoPolicyUpdate" / "BlueprintPublish" and therefore NEVER equals
        // "Control". The practical effect was that a governance proposal (empty BlueprintId +
        // transactionType="GovernanceOperation") matched no arm at all and skipped roster enforcement
        // entirely — a bypass in the opposite direction to the rejection everyone was seeing. It was
        // masked only because the same transaction carried an empty BlueprintId and died earlier on
        // TX_003; fixing that alone would have opened the hole.
        if (transaction.Metadata.TryGetValue("Type", out var typeDiscriminator) &&
            string.Equals(typeDiscriminator, "Control", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Attempts to parse the control payload from the transaction.
    /// Returns null if the payload is not a control envelope (e.g., an approval or an action).
    /// </summary>
    private ControlTransactionPayload? TryParseControlPayload(Transaction transaction)
    {
        try
        {
            var payloadText = transaction.Payload.GetRawText();
            return JsonSerializer.Deserialize<ControlTransactionPayload>(payloadText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex,
                "Could not parse governance operation from transaction {TransactionId} payload",
                transaction.TransactionId);
            return null;
        }
    }

    private static ValidationEngineError CreateError(
        string code,
        string message,
        string? field = null,
        bool isFatal = false)
    {
        // Gated emission counter — see ValidationEngine.CreateError.
        RuleTelemetry.RuleEmitted(code);

        return new ValidationEngineError
        {
            Code = code,
            Message = message,
            Category = ValidationErrorCategory.Permission,
            Field = field,
            IsFatal = isFatal,
        };
    }

    private static ValidationEngineResult CreateFailureResult(
        Transaction transaction,
        TimeSpan duration,
        List<ValidationEngineError> errors) =>
        ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            duration,
            errors.ToArray());
}
