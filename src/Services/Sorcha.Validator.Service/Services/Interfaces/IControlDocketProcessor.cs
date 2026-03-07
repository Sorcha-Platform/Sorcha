// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Services.Interfaces;

/// <summary>
/// Processes control dockets containing governance transactions.
/// Control dockets contain special transactions that modify register configuration,
/// validator membership, and blueprint publications.
/// </summary>
public interface IControlDocketProcessor
{
    /// <summary>
    /// Identifies and extracts control transactions from a docket.
    /// </summary>
    /// <param name="docket">The docket to scan</param>
    /// <returns>List of control transactions with their action types</returns>
    IReadOnlyList<ControlTransaction> ExtractControlTransactions(Docket docket);

    /// <summary>
    /// Determines if a docket contains any control transactions.
    /// </summary>
    /// <param name="docket">The docket to check</param>
    /// <returns>True if the docket contains control transactions</returns>
    bool IsControlDocket(Docket docket);

    /// <summary>
    /// Validates control transactions against the current control blueprint.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="controlTransactions">Control transactions to validate</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Validation result with any errors</returns>
    Task<ControlValidationResult> ValidateControlTransactionsAsync(
        string registerId,
        IReadOnlyList<ControlTransaction> controlTransactions,
        CancellationToken ct = default);

    /// <summary>
    /// Processes a committed control docket, applying state changes.
    /// This is called after consensus is achieved and the docket is committed.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="docket">The committed docket</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result of processing with applied changes</returns>
    Task<ControlProcessingResult> ProcessCommittedDocketAsync(
        string registerId,
        Docket docket,
        CancellationToken ct = default);

    /// <summary>
    /// Applies a single control action to the register state.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="controlTransaction">Control transaction to apply</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result of applying the action</returns>
    Task<ControlActionResult> ApplyControlActionAsync(
        string registerId,
        ControlTransaction controlTransaction,
        CancellationToken ct = default);

    /// <summary>
    /// Event raised when a control action is successfully applied.
    /// </summary>
    event EventHandler<ControlActionAppliedEventArgs>? ControlActionApplied;
}

/// <summary>
/// A control transaction extracted from a docket with its action type
/// </summary>
public record ControlTransaction
{
    /// <summary>The underlying transaction</summary>
    public required Transaction Transaction { get; init; }

    /// <summary>The control action type</summary>
    public required ControlActionType ActionType { get; init; }

    /// <summary>Parsed action ID (e.g., "control.validator.register")</summary>
    public required string ActionId { get; init; }

    /// <summary>Parsed payload data (specific to action type)</summary>
    public required ControlPayload Payload { get; init; }
}

/// <summary>
/// Control action types
/// </summary>
public enum ControlActionType
{
    /// <summary>Unknown or invalid control action</summary>
    Unknown = 0,

    /// <summary>Register a new validator</summary>
    ValidatorRegister,

    /// <summary>Approve a pending validator (consent mode)</summary>
    ValidatorApprove,

    /// <summary>Suspend a validator temporarily</summary>
    ValidatorSuspend,

    /// <summary>Remove a validator permanently</summary>
    ValidatorRemove,

    /// <summary>Update register configuration</summary>
    ConfigUpdate,

    /// <summary>Publish a blueprint to the register</summary>
    BlueprintPublish,

    /// <summary>Update register metadata</summary>
    RegisterUpdateMetadata,

    /// <summary>Update the register's cryptographic policy</summary>
    CryptoPolicyUpdate,

    /// <summary>Update the register's operational policy (governance, validators, consensus, leader election)</summary>
    PolicyUpdate
}

/// <summary>
/// Base class for control action payloads
/// </summary>
public abstract record ControlPayload
{
    /// <summary>Reason for the action (optional)</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Payload for validator registration
/// </summary>
public record ValidatorRegisterPayload : ControlPayload
{
    /// <summary>Validator identifier</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Validator public key</summary>
    public required string PublicKey { get; init; }

    /// <summary>Validator endpoint URL</summary>
    public required string Endpoint { get; init; }

    /// <summary>Additional metadata</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Payload for validator approval
/// </summary>
public record ValidatorApprovePayload : ControlPayload
{
    /// <summary>Validator ID to approve</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Who approved the validator</summary>
    public required string ApprovedBy { get; init; }

    /// <summary>Approval notes</summary>
    public string? ApprovalNotes { get; init; }
}

/// <summary>
/// Payload for validator suspension
/// </summary>
public record ValidatorSuspendPayload : ControlPayload
{
    /// <summary>Validator ID to suspend</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Who suspended the validator</summary>
    public required string SuspendedBy { get; init; }

    /// <summary>When the suspension ends (null = indefinite)</summary>
    public DateTimeOffset? SuspendedUntil { get; init; }
}

/// <summary>
/// Payload for validator removal
/// </summary>
public record ValidatorRemovePayload : ControlPayload
{
    /// <summary>Validator ID to remove</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Who removed the validator</summary>
    public required string RemovedBy { get; init; }
}

/// <summary>
/// Payload for configuration update
/// </summary>
public record ConfigUpdatePayload : ControlPayload
{
    /// <summary>JSON path to the config property</summary>
    public required string Path { get; init; }

    /// <summary>Previous value (for verification)</summary>
    public object? OldValue { get; init; }

    /// <summary>New value to set</summary>
    public required object NewValue { get; init; }
}

/// <summary>
/// Payload for blueprint publication
/// </summary>
public record BlueprintPublishPayload : ControlPayload
{
    /// <summary>Blueprint identifier</summary>
    public required string BlueprintId { get; init; }

    /// <summary>Blueprint content (JSON)</summary>
    public required string BlueprintJson { get; init; }

    /// <summary>Previous version transaction ID (if updating)</summary>
    public string? PreviousVersionId { get; init; }

    /// <summary>Who published the blueprint</summary>
    public required string PublishedBy { get; init; }
}

/// <summary>
/// Payload for register metadata update
/// </summary>
public record RegisterMetadataUpdatePayload : ControlPayload
{
    /// <summary>Field to update (name, description, tags)</summary>
    public required string Field { get; init; }

    /// <summary>Previous value</summary>
    public string? OldValue { get; init; }

    /// <summary>New value</summary>
    public required string NewValue { get; init; }
}

/// <summary>
/// Payload for register operational policy update (governance, validators, consensus, leader election)
/// </summary>
public record PolicyUpdatePayload : ControlPayload
{
    /// <summary>Full policy snapshot with version incremented</summary>
    public Sorcha.Register.Models.RegisterPolicy? Policy { get; init; }

    /// <summary>Transition mode when changing registrationMode from public to consent</summary>
    public Sorcha.Register.Models.TransitionMode? TransitionMode { get; init; }

    /// <summary>DID of the proposer</summary>
    public string? UpdatedBy { get; init; }
}

/// <summary>
/// Payload for crypto policy update
/// </summary>
public record CryptoPolicyUpdatePayload : ControlPayload
{
    /// <summary>New policy version number</summary>
    public required int Version { get; init; }

    /// <summary>Accepted signature algorithms</summary>
    public required string[] AcceptedSignatureAlgorithms { get; init; }

    /// <summary>Required signature algorithms (must be subset of accepted)</summary>
    public string[] RequiredSignatureAlgorithms { get; init; } = [];

    /// <summary>Enforcement mode: Permissive or Strict</summary>
    public string EnforcementMode { get; init; } = "Permissive";

    /// <summary>When the policy takes effect</summary>
    public DateTimeOffset? EffectiveFrom { get; init; }

    /// <summary>Who submitted the policy update</summary>
    public string? UpdatedBy { get; init; }
}

/// <summary>
/// Result of control transaction validation
/// </summary>
public record ControlValidationResult
{
    /// <summary>Whether all transactions are valid</summary>
    public required bool IsValid { get; init; }

    /// <summary>Validation errors by transaction ID</summary>
    public required IReadOnlyDictionary<string, string[]> Errors { get; init; }

    /// <summary>Transactions that passed validation</summary>
    public required IReadOnlyList<ControlTransaction> ValidTransactions { get; init; }

    /// <summary>Creates a successful validation result</summary>
    public static ControlValidationResult Success(IReadOnlyList<ControlTransaction> transactions) => new()
    {
        IsValid = true,
        Errors = new Dictionary<string, string[]>(),
        ValidTransactions = transactions
    };

    /// <summary>Creates a failed validation result</summary>
    public static ControlValidationResult Failure(IReadOnlyDictionary<string, string[]> errors) => new()
    {
        IsValid = false,
        Errors = errors,
        ValidTransactions = Array.Empty<ControlTransaction>()
    };
}

/// <summary>
/// Result of processing a control docket
/// </summary>
public record ControlProcessingResult
{
    /// <summary>Whether processing succeeded</summary>
    public required bool Success { get; init; }

    /// <summary>Number of control actions applied</summary>
    public required int ActionsApplied { get; init; }

    /// <summary>Individual action results</summary>
    public required IReadOnlyList<ControlActionResult> ActionResults { get; init; }

    /// <summary>Whether configuration was updated</summary>
    public bool ConfigurationUpdated { get; init; }

    /// <summary>Whether validators were modified</summary>
    public bool ValidatorsModified { get; init; }

    /// <summary>Error message if processing failed</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of applying a single control action
/// </summary>
public record ControlActionResult
{
    /// <summary>Transaction ID of the control action</summary>
    public required string TransactionId { get; init; }

    /// <summary>Action type that was applied</summary>
    public required ControlActionType ActionType { get; init; }

    /// <summary>Whether the action was applied successfully</summary>
    public required bool Success { get; init; }

    /// <summary>Description of what changed</summary>
    public string? ChangeDescription { get; init; }

    /// <summary>Error message if action failed</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Event args for when a control action is applied
/// </summary>
public class ControlActionAppliedEventArgs : EventArgs
{
    /// <summary>Register ID</summary>
    public required string RegisterId { get; init; }

    /// <summary>Transaction ID</summary>
    public required string TransactionId { get; init; }

    /// <summary>Action type</summary>
    public required ControlActionType ActionType { get; init; }

    /// <summary>When the action was applied</summary>
    public required DateTimeOffset AppliedAt { get; init; }

    /// <summary>Description of the change</summary>
    public string? ChangeDescription { get; init; }
}
