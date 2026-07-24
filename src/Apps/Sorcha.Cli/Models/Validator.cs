// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Cli.Models;

/// <summary>
/// Validator service status.
/// </summary>
/// <remarks>
/// Mirrors <c>Sorcha.Validator.Service.ValidatorStatus</c> (returned by
/// <c>GET /api/admin/validators/{registerId}/status</c>); the pairing is asserted by
/// <c>CliWireContractTests</c>. This was previously an invented shape — <c>status</c>,
/// <c>isRunning</c>, <c>uptime</c>, <c>consensusProtocol</c>, <c>registersMonitored</c> — none of
/// which the server sends. So <c>sorcha validator status</c> printed an empty status, "Running: No"
/// and a blank uptime regardless of what the validator was actually doing.
/// </remarks>
public class ValidatorStatus
{
    /// <summary>The register this validator status is for.</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>Whether the validator is actively sealing for this register.</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; set; }

    /// <summary>Transactions currently waiting in this register's mempool.</summary>
    [JsonPropertyName("transactionsInMemPool")]
    public int TransactionsInMemPool { get; set; }

    /// <summary>Dockets this validator has proposed.</summary>
    [JsonPropertyName("docketsProposed")]
    public long DocketsProposed { get; set; }

    /// <summary>Dockets confirmed (sealed) for this register.</summary>
    [JsonPropertyName("docketsConfirmed")]
    public long DocketsConfirmed { get; set; }

    /// <summary>Dockets this validator proposed that were rejected.</summary>
    [JsonPropertyName("docketsRejected")]
    public long DocketsRejected { get; set; }

    /// <summary>When this validator started sealing for the register, if it has.</summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When this validator last built a docket, if ever.</summary>
    [JsonPropertyName("lastDocketBuildAt")]
    public DateTimeOffset? LastDocketBuildAt { get; set; }
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
    /// <summary>The audit entry's own id. Present on the wire; the CLI previously dropped it.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>The register this roster change applies to. Present on the wire; previously dropped.</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

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
