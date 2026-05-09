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
                // No roster means this could be a genesis Control TX (first roster creation)
                // Allow genesis if this is the register's first Control transaction
                _logger.LogInformation(
                    "No existing roster for register {RegisterId} — allowing genesis Control TX {TransactionId}",
                    transaction.RegisterId, transaction.TransactionId);
                return ValidationEngineResult.Success(
                    transaction.TransactionId,
                    transaction.RegisterId,
                    sw.Elapsed);
            }

            // Extract the submitter's public key from the first signature
            if (transaction.Signatures.Count == 0)
            {
                errors.Add(CreateError("VAL_PERM_001",
                    "Control transaction must have at least one signature",
                    "Signatures", true));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            var submitterPublicKey = Base64Url.EncodeToString(transaction.Signatures[0].PublicKey);

            // Find the submitter in the roster by matching public key
            var submitterAttestation = roster.ControlRecord.Attestations
                .FirstOrDefault(a => a.PublicKey == submitterPublicKey);

            if (submitterAttestation == null)
            {
                _logger.LogWarning(
                    "Transaction {TransactionId} on register {RegisterId}: submitter not found in roster",
                    transaction.TransactionId, transaction.RegisterId);
                errors.Add(CreateError("VAL_PERM_002",
                    "Submitter's public key does not match any member in the register's admin roster",
                    "Signatures[0].PublicKey"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

            // Verify submitter has a governance role (Owner or Admin)
            if (submitterAttestation.Role is not (RegisterRole.Owner or RegisterRole.Admin))
            {
                _logger.LogWarning(
                    "Transaction {TransactionId} on register {RegisterId}: submitter has role {Role}, requires Owner or Admin",
                    transaction.TransactionId, transaction.RegisterId, submitterAttestation.Role);
                errors.Add(CreateError("VAL_PERM_003",
                    $"Submitter has role '{submitterAttestation.Role}' which cannot execute governance operations. Requires Owner or Admin.",
                    "Signatures[0].PublicKey"));
                return CreateFailureResult(transaction, sw.Elapsed, errors);
            }

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
        // Check if the blueprint ID matches the governance blueprint
        if (string.Equals(transaction.BlueprintId, GovernanceBlueprintId, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check metadata for transaction type
        if (transaction.Metadata.TryGetValue("transactionType", out var txType) &&
            string.Equals(txType, "Control", StringComparison.OrdinalIgnoreCase))
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
