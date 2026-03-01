// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Trackable background encryption operation.
/// </summary>
public sealed class EncryptionOperation
{
    /// <summary>Unique tracking identifier.</summary>
    public required string OperationId { get; init; }

    /// <summary>Current state of the operation.</summary>
    public EncryptionOperationStatus Status { get; set; } = EncryptionOperationStatus.Pending;

    /// <summary>Source blueprint ID.</summary>
    public required string BlueprintId { get; init; }

    /// <summary>Source action ID.</summary>
    public required string ActionId { get; init; }

    /// <summary>Workflow instance ID.</summary>
    public required string InstanceId { get; init; }

    /// <summary>Wallet that initiated the action.</summary>
    public required string SubmittingWalletAddress { get; init; }

    /// <summary>Number of recipients to encrypt for.</summary>
    public int TotalRecipients { get; set; }

    /// <summary>Number of disclosure groups.</summary>
    public int TotalGroups { get; set; }

    /// <summary>Current pipeline step (1-based).</summary>
    public int CurrentStep { get; set; }

    /// <summary>Total pipeline steps.</summary>
    public int TotalSteps { get; set; }

    /// <summary>Human-readable current step name.</summary>
    public string StepName { get; set; } = string.Empty;

    /// <summary>Progress percentage (0-100).</summary>
    public int PercentComplete { get; set; }

    /// <summary>Result transaction hash on success.</summary>
    public string? TransactionHash { get; set; }

    /// <summary>Error message on failure.</summary>
    public string? Error { get; set; }

    /// <summary>Wallet address of failed recipient (if applicable).</summary>
    public string? FailedRecipient { get; set; }

    /// <summary>Operation creation time (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Operation completion time (UTC).</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Status of an encryption operation.
/// </summary>
public enum EncryptionOperationStatus
{
    /// <summary>Queued, awaiting processing.</summary>
    Pending,

    /// <summary>Resolving recipient public keys.</summary>
    ResolvingKeys,

    /// <summary>Encrypting payload groups.</summary>
    Encrypting,

    /// <summary>Assembling encrypted transaction.</summary>
    BuildingTransaction,

    /// <summary>Submitting to validator.</summary>
    Submitting,

    /// <summary>Successfully submitted.</summary>
    Complete,

    /// <summary>Failed with error.</summary>
    Failed
}
