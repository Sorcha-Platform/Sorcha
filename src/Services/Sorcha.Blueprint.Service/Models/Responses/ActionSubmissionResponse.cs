// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models.Responses;

/// <summary>
/// Response from submitting an action
/// </summary>
public record ActionSubmissionResponse
{
    /// <summary>
    /// The transaction ID (hash)
    /// </summary>
    public required string TransactionId { get; init; }

    /// <summary>
    /// Legacy alias for TransactionId for backwards compatibility
    /// </summary>
    public string TransactionHash => TransactionId;

    /// <summary>
    /// The serialized transaction (for signing by wallet) - legacy support
    /// </summary>
    public string? SerializedTransaction { get; init; }

    /// <summary>
    /// The workflow instance ID
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// Next action(s) in the workflow.
    /// Multiple actions indicate parallel branches.
    /// Empty list indicates workflow completion.
    /// </summary>
    public List<NextActionResponse> NextActions { get; init; } = [];

    /// <summary>
    /// Calculated values from JSON Logic expressions
    /// </summary>
    public Dictionary<string, object>? Calculations { get; init; }

    /// <summary>
    /// Whether the workflow is complete
    /// </summary>
    public bool IsComplete { get; init; }

    /// <summary>
    /// Validation warnings (non-blocking)
    /// </summary>
    public List<string>? Warnings { get; init; }

    /// <summary>
    /// Credential ID if a verifiable credential was issued by this action
    /// </summary>
    public string? IssuedCredentialId { get; init; }

    /// <summary>
    /// Operation ID for async encryption tracking (non-null when IsAsync is true).
    /// Use this with the /api/operations/{operationId} endpoint or SignalR EncryptionProgress events.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// True when encryption is being processed asynchronously.
    /// TransactionId will be empty; monitor via OperationId.
    /// </summary>
    public bool IsAsync { get; init; }

    /// <summary>
    /// File transaction hashes (if files were attached)
    /// </summary>
    public List<string>? FileTransactionHashes { get; init; }

    /// <summary>
    /// Timestamp when the transaction was created
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Information about the next action to be executed
/// </summary>
public record NextActionResponse
{
    /// <summary>
    /// The action ID within the blueprint
    /// </summary>
    public required int ActionId { get; init; }

    /// <summary>
    /// Display title of the action
    /// </summary>
    public required string ActionTitle { get; init; }

    /// <summary>
    /// The participant ID who should execute this action
    /// </summary>
    public required string ParticipantId { get; init; }

    /// <summary>
    /// Branch ID for parallel workflows
    /// </summary>
    public string? BranchId { get; init; }
}
