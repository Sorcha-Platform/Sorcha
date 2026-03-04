// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Services.Interfaces;

/// <summary>
/// Manages the registry of validators for a register.
/// Tracks active validators, their status, and provides ordered lists for leader election.
/// </summary>
public interface IValidatorRegistry
{
    /// <summary>
    /// Get all active validators for a register
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of active validators</returns>
    Task<IReadOnlyList<ValidatorInfo>> GetActiveValidatorsAsync(
        string registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a validator is registered and active
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="validatorId">Validator ID to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if registered and active</returns>
    Task<bool> IsRegisteredAsync(
        string registerId,
        string validatorId,
        CancellationToken ct = default);

    /// <summary>
    /// Get validator information by ID
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="validatorId">Validator ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Validator info or null if not found</returns>
    Task<ValidatorInfo?> GetValidatorAsync(
        string registerId,
        string validatorId,
        CancellationToken ct = default);

    /// <summary>
    /// Get validators in order for rotating election
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Ordered list of validator IDs</returns>
    Task<IReadOnlyList<string>> GetValidatorOrderAsync(
        string registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Register this validator for a register (public mode)
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="registration">Registration details</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Registration result</returns>
    Task<ValidatorRegistrationResult> RegisterAsync(
        string registerId,
        ValidatorRegistration registration,
        CancellationToken ct = default);

    /// <summary>
    /// Refresh validator list from chain
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="ct">Cancellation token</param>
    Task RefreshAsync(string registerId, CancellationToken ct = default);

    /// <summary>
    /// Get count of active validators
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of active validators</returns>
    Task<int> GetActiveCountAsync(string registerId, CancellationToken ct = default);

    /// <summary>
    /// Get pending validators awaiting approval (consent mode)
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of pending validators</returns>
    Task<IReadOnlyList<ValidatorInfo>> GetPendingValidatorsAsync(
        string registerId,
        CancellationToken ct = default);

    /// <summary>
    /// Approve a pending validator (consent mode)
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="request">Approval request</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Approval result</returns>
    Task<ValidatorApprovalResult> ApproveValidatorAsync(
        string registerId,
        ValidatorApprovalRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Reject a pending validator (consent mode)
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="validatorId">Validator to reject</param>
    /// <param name="reason">Rejection reason</param>
    /// <param name="rejectedBy">Who rejected (wallet address)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if rejected successfully</returns>
    Task<bool> RejectValidatorAsync(
        string registerId,
        string validatorId,
        string reason,
        string rejectedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Enforces a registration mode transition from public to consent.
    /// In Immediate mode, unapproved validators are removed at once.
    /// In GracePeriod mode, unapproved validators keep their current TTL but are not refreshed.
    /// </summary>
    /// <param name="registerId">Register ID</param>
    /// <param name="approvedValidators">List of approved validators from the new policy</param>
    /// <param name="mode">Transition mode (Immediate or GracePeriod)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of validators affected by the transition</returns>
    Task<int> EnforceRegistrationModeTransitionAsync(
        string registerId,
        IReadOnlyList<Sorcha.Register.Models.ApprovedValidator> approvedValidators,
        Sorcha.Register.Models.TransitionMode mode,
        CancellationToken ct = default);

    /// <summary>
    /// Event raised when validator list changes
    /// </summary>
    event EventHandler<ValidatorListChangedEventArgs>? ValidatorListChanged;
}

/// <summary>
/// Information about a registered validator
/// </summary>
public record ValidatorInfo
{
    /// <summary>Validator's unique identifier (wallet address)</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Validator's public key for signature verification</summary>
    public required string PublicKey { get; init; }

    /// <summary>gRPC endpoint for peer communication</summary>
    public required string GrpcEndpoint { get; init; }

    /// <summary>Current status</summary>
    public required ValidatorStatus Status { get; init; }

    /// <summary>When the validator registered</summary>
    public required DateTimeOffset RegisteredAt { get; init; }

    /// <summary>Order index for rotating leader election</summary>
    public int? OrderIndex { get; init; }

    /// <summary>Transaction ID of the registration</summary>
    public string? RegistrationTxId { get; init; }

    /// <summary>Optional metadata</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Validator registration request
/// </summary>
public record ValidatorRegistration
{
    /// <summary>Validator's unique identifier</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Validator's public key</summary>
    public required string PublicKey { get; init; }

    /// <summary>gRPC endpoint</summary>
    public required string GrpcEndpoint { get; init; }

    /// <summary>Optional metadata</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Result of validator registration attempt
/// </summary>
public record ValidatorRegistrationResult
{
    /// <summary>Whether registration succeeded</summary>
    public required bool Success { get; init; }

    /// <summary>Registration transaction ID if successful</summary>
    public string? TransactionId { get; init; }

    /// <summary>Error message if failed</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Assigned order index for election</summary>
    public int? OrderIndex { get; init; }

    /// <summary>Creates a successful result</summary>
    public static ValidatorRegistrationResult Succeeded(string txId, int orderIndex) => new()
    {
        Success = true,
        TransactionId = txId,
        OrderIndex = orderIndex
    };

    /// <summary>Creates a failed result</summary>
    public static ValidatorRegistrationResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}

/// <summary>
/// Validator status values
/// </summary>
public enum ValidatorStatus
{
    /// <summary>Registration pending approval (consent mode)</summary>
    Pending,

    /// <summary>Active and participating in consensus</summary>
    Active,

    /// <summary>Temporarily suspended</summary>
    Suspended,

    /// <summary>Permanently removed</summary>
    Removed
}

/// <summary>
/// Event arguments for validator list changes
/// </summary>
public class ValidatorListChangedEventArgs : EventArgs
{
    /// <summary>Register ID</summary>
    public required string RegisterId { get; init; }

    /// <summary>Type of change</summary>
    public required ValidatorListChangeType ChangeType { get; init; }

    /// <summary>Validator ID affected</summary>
    public required string ValidatorId { get; init; }

    /// <summary>New validator count</summary>
    public required int NewValidatorCount { get; init; }
}

/// <summary>
/// Types of validator list changes
/// </summary>
public enum ValidatorListChangeType
{
    /// <summary>New validator added</summary>
    ValidatorAdded,

    /// <summary>Validator removed</summary>
    ValidatorRemoved,

    /// <summary>Validator suspended</summary>
    ValidatorSuspended,

    /// <summary>Validator reactivated</summary>
    ValidatorReactivated,

    /// <summary>Validator approved (consent mode)</summary>
    ValidatorApproved,

    /// <summary>Validator rejected (consent mode)</summary>
    ValidatorRejected
}

/// <summary>
/// Request to approve a pending validator
/// </summary>
public record ValidatorApprovalRequest
{
    /// <summary>Validator ID to approve</summary>
    public required string ValidatorId { get; init; }

    /// <summary>Who approved (wallet address)</summary>
    public required string ApprovedBy { get; init; }

    /// <summary>Optional approval notes</summary>
    public string? ApprovalNotes { get; init; }
}

/// <summary>
/// Result of validator approval attempt
/// </summary>
public record ValidatorApprovalResult
{
    /// <summary>Whether approval succeeded</summary>
    public required bool Success { get; init; }

    /// <summary>Approval transaction ID if successful</summary>
    public string? TransactionId { get; init; }

    /// <summary>Error message if failed</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Validator's order index after approval</summary>
    public int? OrderIndex { get; init; }

    /// <summary>When the validator was approved</summary>
    public DateTimeOffset? ApprovedAt { get; init; }

    /// <summary>Creates a successful result</summary>
    public static ValidatorApprovalResult Succeeded(string txId, int orderIndex, DateTimeOffset approvedAt) => new()
    {
        Success = true,
        TransactionId = txId,
        OrderIndex = orderIndex,
        ApprovedAt = approvedAt
    };

    /// <summary>Creates a failed result</summary>
    public static ValidatorApprovalResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}
