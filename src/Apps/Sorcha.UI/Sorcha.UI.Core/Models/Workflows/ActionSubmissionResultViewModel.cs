// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Workflows;

/// <summary>
/// Result view model from submitting an action execution.
/// Maps to ActionSubmissionResponse from the Blueprint Service.
/// </summary>
public record ActionSubmissionResultViewModel
{
    public string TransactionId { get; init; } = string.Empty;
    public string InstanceId { get; init; } = string.Empty;
    public bool IsComplete { get; init; }
    public List<NextActionInfo>? NextActions { get; init; }
    public List<string>? Warnings { get; init; }

    /// <summary>
    /// Operation ID for async encryption tracking. Null for synchronous operations.
    /// </summary>
    public string? OperationId { get; init; }

    /// <summary>
    /// True when encryption is processed asynchronously (HTTP 202 response).
    /// </summary>
    public bool IsAsync { get; init; }

    /// <summary>
    /// Whether this result has an active async encryption operation to track.
    /// </summary>
    public bool HasAsyncOperation => IsAsync && !string.IsNullOrEmpty(OperationId);

    /// <summary>
    /// HAIP credential offer URI when the action issues a credential to an external wallet.
    /// Present when TargetAudience is HaipExternalWallet.
    /// </summary>
    public HaipCredentialOfferInfo? CredentialOffer { get; init; }

    /// <summary>
    /// HAIP presentation request URI when the action requires a credential from an external wallet.
    /// Present when PresentationSource is HaipExternalWallet.
    /// </summary>
    public HaipPresentationRequestInfo? PresentationRequest { get; init; }

    /// <summary>
    /// Whether this result includes a HAIP external wallet interaction.
    /// </summary>
    public bool HasHaipInteraction => CredentialOffer is not null || PresentationRequest is not null;
}

/// <summary>
/// HAIP credential offer details for QR rendering.
/// </summary>
public record HaipCredentialOfferInfo
{
    public Guid OfferId { get; init; }
    public string CredentialOfferUri { get; init; } = string.Empty;
    public string CredentialType { get; init; } = string.Empty;
    public string? IssuerName { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// HAIP presentation request details for QR rendering.
/// </summary>
public record HaipPresentationRequestInfo
{
    public Guid RequestId { get; init; }
    public string PresentationRequestUri { get; init; } = string.Empty;
    public string CredentialType { get; init; } = string.Empty;
    public IReadOnlyList<string>? RequestedClaims { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Information about the next action in the workflow after a submission.
/// Maps to NextActionResponse from the Blueprint Service.
/// </summary>
public record NextActionInfo
{
    public int ActionId { get; init; }
    public string ActionTitle { get; init; } = string.Empty;
    public string ParticipantId { get; init; } = string.Empty;
    public string? BranchId { get; init; }
}
