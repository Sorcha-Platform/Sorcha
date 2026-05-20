// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Validator service status.
/// </summary>
public class ValidatorStatus
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("isRunning")]
    public bool IsRunning { get; set; }

    [JsonPropertyName("registersMonitored")]
    public int RegistersMonitored { get; set; }

    [JsonPropertyName("totalValidations")]
    public long TotalValidations { get; set; }

    [JsonPropertyName("failedValidations")]
    public long FailedValidations { get; set; }

    [JsonPropertyName("lastValidationAt")]
    public DateTimeOffset? LastValidationAt { get; set; }

    [JsonPropertyName("uptime")]
    public string Uptime { get; set; } = string.Empty;

    [JsonPropertyName("consensusProtocol")]
    public string ConsensusProtocol { get; set; } = string.Empty;
}

/// <summary>
/// Validator processing result.
/// </summary>
public class ValidatorProcessResult
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("transactionsProcessed")]
    public int TransactionsProcessed { get; set; }

    [JsonPropertyName("transactionsValidated")]
    public int TransactionsValidated { get; set; }

    [JsonPropertyName("transactionsRejected")]
    public int TransactionsRejected { get; set; }

    [JsonPropertyName("processedAt")]
    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>
/// Integrity check result.
/// </summary>
public class IntegrityCheckResult
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("isValid")]
    public bool IsValid { get; set; }

    [JsonPropertyName("chainLength")]
    public long ChainLength { get; set; }

    [JsonPropertyName("checkedAt")]
    public DateTimeOffset CheckedAt { get; set; }

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Validator start/stop response.
/// </summary>
public class ValidatorActionResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

// --- Roster Governance (Feature 086) ---

/// <summary>
/// Request to self-register a validator for a register.
/// </summary>
public class RegisterValidatorRequest
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("validatorId")]
    public string ValidatorId { get; set; } = string.Empty;

    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    [JsonPropertyName("grpcEndpoint")]
    public string GrpcEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Response from registering a validator.
/// </summary>
public class RegisterValidatorResponse
{
    [JsonPropertyName("validatorId")]
    public string ValidatorId { get; set; } = string.Empty;

    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("orderIndex")]
    public int OrderIndex { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Active validator count for a register.
/// </summary>
public class ValidatorCountResponse
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("activeCount")]
    public int ActiveCount { get; set; }

    [JsonPropertyName("minValidators")]
    public int MinValidators { get; set; }

    [JsonPropertyName("maxValidators")]
    public int MaxValidators { get; set; }

    [JsonPropertyName("hasQuorum")]
    public bool HasQuorum { get; set; }
}

/// <summary>
/// Validator roster audit trail for a register.
/// </summary>
public class ValidatorAuditResponse
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("entries")]
    public List<ValidatorAuditEntry> Entries { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>
/// A single validator roster lifecycle transition.
/// </summary>
public class ValidatorAuditEntry
{
    [JsonPropertyName("validatorId")]
    public string ValidatorId { get; set; } = string.Empty;

    [JsonPropertyName("previousStatus")]
    public string PreviousStatus { get; set; } = string.Empty;

    [JsonPropertyName("newStatus")]
    public string NewStatus { get; set; } = string.Empty;

    [JsonPropertyName("performedBy")]
    public string PerformedBy { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }
}

/// <summary>
/// Request to suspend a validator.
/// </summary>
public class SuspendValidatorRequest
{
    [JsonPropertyName("suspendedBy")]
    public string SuspendedBy { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Request to reactivate a suspended validator.
/// </summary>
public class ReactivateValidatorRequest
{
    [JsonPropertyName("reactivatedBy")]
    public string ReactivatedBy { get; set; } = string.Empty;

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }
}

/// <summary>
/// Request to permanently revoke a validator.
/// </summary>
public class RevokeValidatorRequest
{
    [JsonPropertyName("revokedBy")]
    public string RevokedBy { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Result of a validator lifecycle transition (suspend / reactivate / revoke).
/// Carries whichever timestamp/actor fields the specific operation returns.
/// </summary>
public class ValidatorLifecycleResponse
{
    [JsonPropertyName("validatorId")]
    public string ValidatorId { get; set; } = string.Empty;

    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("suspendedAt")]
    public DateTimeOffset? SuspendedAt { get; set; }

    [JsonPropertyName("suspendedBy")]
    public string? SuspendedBy { get; set; }

    [JsonPropertyName("reactivatedAt")]
    public DateTimeOffset? ReactivatedAt { get; set; }

    [JsonPropertyName("revokedAt")]
    public DateTimeOffset? RevokedAt { get; set; }

    [JsonPropertyName("revokedBy")]
    public string? RevokedBy { get; set; }
}

/// <summary>
/// A wallet's sequence numbers for a register.
/// </summary>
public class ValidatorSequenceResponse
{
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    [JsonPropertyName("walletAddress")]
    public string WalletAddress { get; set; } = string.Empty;

    [JsonPropertyName("lastSequenceNumber")]
    public long LastSequenceNumber { get; set; }

    [JsonPropertyName("nextSequenceNumber")]
    public long NextSequenceNumber { get; set; }
}
