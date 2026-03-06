// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Work item queued for asynchronous encryption processing.
/// Contains all data needed to encrypt, build, sign, and submit a transaction.
/// </summary>
public sealed record EncryptionWorkItem
{
    /// <summary>
    /// Unique operation identifier returned to caller for tracking.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Workflow instance ID.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// Blueprint ID for the workflow.
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// Action ID within the blueprint.
    /// </summary>
    public required int ActionId { get; init; }

    /// <summary>
    /// Submitting participant's wallet address.
    /// </summary>
    public required string SenderWallet { get; init; }

    /// <summary>
    /// Target register ID.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Disclosure groups to encrypt.
    /// </summary>
    public required DisclosureGroup[] DisclosureGroups { get; init; }

    /// <summary>
    /// Payload data including calculated values.
    /// </summary>
    public required Dictionary<string, object> PayloadWithCalculations { get; init; }

    /// <summary>
    /// Disclosed payloads per recipient wallet.
    /// </summary>
    public required Dictionary<string, Dictionary<string, object>> DisclosedPayloads { get; init; }

    /// <summary>
    /// Previous transaction ID in the chain for building the next transaction.
    /// </summary>
    public string? PreviousTransactionId { get; init; }

    /// <summary>
    /// Pre-computed encrypted payload groups (if encryption already started).
    /// </summary>
    public EncryptedPayloadGroup[]? PreComputedGroups { get; init; }

    /// <summary>
    /// Authenticated user ID (from JWT sub claim) for EventsHub notifications.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Delegation token for downstream service calls.
    /// </summary>
    public required string DelegationToken { get; init; }

    /// <summary>
    /// Timestamp when the work item was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
