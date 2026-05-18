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
    /// Credential ID if a verifiable credential was issued by this action.
    /// </summary>
    /// <remarks>
    /// Retained for backwards-compatible clients. New consumers should prefer
    /// the richer <see cref="IssuedCredential"/> object when present.
    /// </remarks>
    public string? IssuedCredentialId { get; init; }

    /// <summary>
    /// Rich summary of the credential issued by this action. Present whenever
    /// the action has a credentialIssuanceConfig and the issuance succeeded.
    /// Powers the post-action IssuanceSummaryPanel dialog on the UI side.
    /// </summary>
    public IssuedCredentialResponse? IssuedCredential { get; init; }

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

    /// <summary>
    /// Feature 111 — set when the action is awaiting an external presentation outcome.
    /// When true, <see cref="TransactionId"/> carries the PresentationInitiated (attempt)
    /// transaction hash and the action has NOT yet completed. The verifier callback
    /// writes the PresentationOutcome that advances the action on success.
    /// </summary>
    public bool AwaitingPresentation { get; init; }
}

/// <summary>
/// Compact, human-readable summary of a credential just issued by an action
/// execution. Populated alongside the legacy <see cref="ActionSubmissionResponse.IssuedCredentialId"/>
/// when the action's credentialIssuanceConfig fires successfully.
/// </summary>
public record IssuedCredentialResponse
{
    /// <summary>Unique credential identifier (urn:uuid:... shape).</summary>
    public required string CredentialId { get; init; }

    /// <summary>Credential type (e.g. "AssuredIdentityCredential").</summary>
    public required string CredentialType { get; init; }

    /// <summary>Recipient DID (typically a Sorcha wallet address; falls back to whatever the engine produced).</summary>
    public required string IssuedToDid { get; init; }

    /// <summary>Best-effort display name for the recipient. Falls back to a truncated DID when no participant lookup matched.</summary>
    public required string IssuedToName { get; init; }

    /// <summary>Display name of the signing organisation (from the issuance config, or a truncated issuer DID fallback).</summary>
    public required string SignedByOrg { get; init; }

    /// <summary>Display name of the user who processed the action (caller of the submit endpoint).</summary>
    public required string ProcessedByName { get; init; }

    /// <summary>Role of the user who processed the action (first role claim, or "Member" as a generic fallback).</summary>
    public required string ProcessedByRole { get; init; }

    /// <summary>Total number of claims in the issued credential.</summary>
    public required int TotalClaims { get; init; }

    /// <summary>Number of claims marked selectively disclosable in the blueprint's credentialIssuanceConfig.</summary>
    public required int DisclosableClaims { get; init; }

    /// <summary>Human-readable usage policy (e.g. "Reusable", "Single-use", "Up to N presentations").</summary>
    public required string UsagePolicy { get; init; }

    /// <summary>Expiry timestamp, if any.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Blueprint title that drove the issuance.</summary>
    public required string BlueprintName { get; init; }

    /// <summary>Title of the specific action within the blueprint.</summary>
    public required string ActionName { get; init; }
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
