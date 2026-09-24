// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Tenant.Models.Identity;
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
    /// The submitting caller's <c>PlatformUser.Id</c> (from the JWT <c>platform_user_id</c> claim —
    /// NOT the <c>sub</c> claim, which carries the org-scoped <c>UserIdentity.Id</c>, a different id
    /// in the same Guid value space; see #1703). Used by
    /// <see cref="Sorcha.Blueprint.Service.Services.Implementation.EncryptionBackgroundService"/> to
    /// address the encryption-complete/-failed inbox notification. Typed (#1709) so a <c>sub</c>
    /// value can no longer be assigned here — the #1703 defect is now a compile error.
    /// </summary>
    public PlatformUserId? UserId { get; init; }

    /// <summary>
    /// Delegation token for downstream service calls.
    /// </summary>
    public required string DelegationToken { get; init; }

    /// <summary>
    /// Pre-computed routing result (next actions). Required for instance advancement
    /// after the encrypted transaction is confirmed on the ledger.
    /// </summary>
    public required RoutingResult RoutingResult { get; init; }

    /// <summary>
    /// Merged data (accumulated state + current payload + calculations) for persisting
    /// on the instance as AccumulatedData after confirmation.
    /// </summary>
    public required Dictionary<string, object> MergedData { get; init; }

    /// <summary>
    /// Keys from the original payload and calculations. Only these fields are stored
    /// in AccumulatedData (not the full merged state).
    /// </summary>
    public required HashSet<string> AllowedAccumulatedFields { get; init; }

    /// <summary>
    /// Timestamp when the work item was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
