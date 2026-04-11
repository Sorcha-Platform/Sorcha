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

    /// <summary>
    /// HAIP credential offer details when the action issues a credential to an external wallet.
    /// Present when the action has a credentialIssuanceConfig with targetAudience HaipExternalWallet.
    /// </summary>
    public HaipCredentialOfferResponse? CredentialOffer { get; init; }

    /// <summary>
    /// HAIP presentation request details when the action requires a credential from an external wallet.
    /// Present when the action has credentialRequirements with presentationSource HaipExternalWallet.
    /// </summary>
    public HaipPresentationRequestResponse? PresentationRequest { get; init; }
}

/// <summary>
/// HAIP credential offer details for QR code rendering and status polling.
/// Returned when a Blueprint action issues a credential to an external HAIP wallet
/// via the OpenID4VCI pre-authorized code flow.
/// </summary>
public record HaipCredentialOfferResponse
{
    /// <summary>The unique offer identifier, used for status polling.</summary>
    public required Guid OfferId { get; init; }

    /// <summary>The openid-credential-offer:// URI to render as a QR code.</summary>
    public required string CredentialOfferUri { get; init; }

    /// <summary>The type of credential being offered (e.g. VerifiedIdentityCredential).</summary>
    public required string CredentialType { get; init; }

    /// <summary>Display name of the issuing organisation (null if not available).</summary>
    public string? IssuerName { get; init; }

    /// <summary>When the credential offer expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// HAIP presentation request details for QR code rendering and verification polling.
/// Returned when a Blueprint action requires a credential presentation from an external
/// HAIP wallet via the OpenID4VP direct_post flow.
/// </summary>
public record HaipPresentationRequestResponse
{
    /// <summary>The unique request identifier, used for verification result polling.</summary>
    public required Guid RequestId { get; init; }

    /// <summary>The openid4vp://authorize URI to render as a QR code.</summary>
    public required string PresentationRequestUri { get; init; }

    /// <summary>The type of credential being requested (e.g. VerifiedIdentityCredential).</summary>
    public required string CredentialType { get; init; }

    /// <summary>The list of claims requested for selective disclosure.</summary>
    public List<string>? RequestedClaims { get; init; }

    /// <summary>When the presentation request expires.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }
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
