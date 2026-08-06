// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Diagnostics;
using System.Text.Json;
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
    private readonly ILogger<RightsEnforcementService> _logger;

    /// <summary>
    /// The governance blueprint ID used to identify Control transactions
    /// </summary>
    public const string GovernanceBlueprintId = "register-governance-v1";

    public RightsEnforcementService(
        IGovernanceRosterService rosterService,
        ILogger<RightsEnforcementService> logger)
    {
        _rosterService = rosterService ?? throw new ArgumentNullException(nameof(rosterService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                errors.Add(CreateError("VAL_PERM_007",
                    "The register has no governance roster and this is not its genesis transaction, so there is no authority to authorise it against.",
                    "RegisterId", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Extract the submitter's public key from the first signature
            if (transaction.Signatures.Count == 0)
            {
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

            // Try to parse the governance operation from the payload for deeper validation
            var operation = TryParseGovernanceOperation(transaction);
            if (operation != null)
            {
                // Validate proposal rules using the roster service
                var proposalResult = _rosterService.ValidateProposal(roster, operation);
                if (!proposalResult.IsValid)
                {
                    foreach (var error in proposalResult.Errors)
                    {
                        errors.Add(CreateError("VAL_PERM_004", error, "Payload"));
                    }
                }

                // For non-Owner Add/Remove: verify quorum is included
                if (submitterAttestation.Role != RegisterRole.Owner &&
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
                        var quorumResult = await _rosterService.ValidateQuorumAsync(
                            transaction.RegisterId, operation, operation.ApprovalSignatures, ct);

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
            return CreateFailureResult(transaction, sw.Elapsed, errors);
        }

        return ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            sw.Elapsed);
    }

    /// <summary>
    /// Determines if a transaction is a governance (Control) transaction by checking
    /// the blueprint ID or transaction metadata.
    /// </summary>
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
    /// Attempts to parse a GovernanceOperation from the transaction payload.
    /// Returns null if the payload is not a governance operation (e.g., genesis).
    /// </summary>
    private GovernanceOperation? TryParseGovernanceOperation(Transaction transaction)
    {
        try
        {
            var payloadText = transaction.Payload.GetRawText();
            var payload = JsonSerializer.Deserialize<ControlTransactionPayload>(payloadText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return payload?.Operation;
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
