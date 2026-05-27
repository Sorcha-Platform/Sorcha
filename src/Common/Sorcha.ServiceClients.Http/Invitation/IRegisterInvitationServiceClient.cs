// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.ServiceClients.Invitation;

/// <summary>
/// HTTP client interface for the Tenant Service's private register invitation endpoints.
/// Used by the Blazor UI (and future callers) to create, accept, list, and revoke
/// cryptographic invitation envelopes that bring organisations onto a private register.
/// </summary>
/// <remarks>
/// The interface surface mirrors the Tenant Service endpoints one-for-one. Auth is
/// expected to be attached by the caller's <c>HttpClient</c> pipeline (for the Blazor
/// UI this is the logged-in user's access token). All 4xx responses surface as
/// <see cref="InvitationApiException"/> so dialogs can render the server message
/// without parsing the body themselves.
/// </remarks>
public interface IRegisterInvitationServiceClient
{
    /// <summary>Create a private register invitation for a target organisation.</summary>
    Task<InvitationCreatedResponse> CreateAsync(
        Guid sourceOrgId,
        CreateInvitationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Accept an invitation token as the target organisation.</summary>
    Task<InvitationAcceptedResponse> AcceptAsync(
        Guid targetOrgId,
        AcceptInvitationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>List invitations for an organisation (direction: sent / received / all).</summary>
    Task<InvitationListResponse> ListAsync(
        Guid orgId,
        string direction = "all",
        CancellationToken cancellationToken = default);

    /// <summary>Revoke a pending invitation created by this organisation.</summary>
    Task RevokeAsync(
        Guid sourceOrgId,
        string invitationId,
        CancellationToken cancellationToken = default);
}

// ---- Request / response DTOs (wire-compatible with Sorcha.Tenant.Service.Models.Dtos) ----

/// <summary>Request to create a register invitation.</summary>
public record CreateInvitationRequest
{
    /// <summary>Identifier of the register.</summary>
    [JsonPropertyName("register_id")]
    public required string RegisterId { get; init; }

    /// <summary>Identifier of the target org did.</summary>
    [JsonPropertyName("target_org_did")]
    public required string TargetOrgDid { get; init; }

    /// <summary>Numeric value for expires in days.</summary>
    [JsonPropertyName("expires_in_days")]
    public int ExpiresInDays { get; init; } = 7;
}

/// <summary>Response returned after creating a register invitation.</summary>
public record InvitationCreatedResponse
{
    /// <summary>Identifier of the invitation.</summary>
    [JsonPropertyName("invitation_id")]
    public required string InvitationId { get; init; }

    /// <summary>The invitation token.</summary>
    [JsonPropertyName("invitation_token")]
    public required string InvitationToken { get; init; }

    /// <summary>Identifier of the register.</summary>
    [JsonPropertyName("register_id")]
    public required string RegisterId { get; init; }

    /// <summary>Identifier of the target org did.</summary>
    [JsonPropertyName("target_org_did")]
    public required string TargetOrgDid { get; init; }

    /// <summary>Timestamp at which the record expires (UTC).</summary>
    [JsonPropertyName("expires_at")]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Server timestamp when the record was created (UTC).</summary>
    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Request to accept an invitation.</summary>
public record AcceptInvitationRequest
{
    /// <summary>The invitation token.</summary>
    [JsonPropertyName("invitation_token")]
    public required string InvitationToken { get; init; }
}

/// <summary>Response returned after accepting an invitation.</summary>
public record InvitationAcceptedResponse
{
    /// <summary>Identifier of the subscription.</summary>
    [JsonPropertyName("subscription_id")]
    public required Guid SubscriptionId { get; init; }

    /// <summary>Identifier of the register.</summary>
    [JsonPropertyName("register_id")]
    public required string RegisterId { get; init; }

    /// <summary>The register name.</summary>
    [JsonPropertyName("register_name")]
    public string? RegisterName { get; init; }

    /// <summary>Identifier of the source org did.</summary>
    [JsonPropertyName("source_org_did")]
    public required string SourceOrgDid { get; init; }

    /// <summary>The source org name.</summary>
    [JsonPropertyName("source_org_name")]
    public string? SourceOrgName { get; init; }

    /// <summary>The subscription status.</summary>
    [JsonPropertyName("subscription_status")]
    public required string SubscriptionStatus { get; init; }

    /// <summary>Timestamp at which accepted occurred (UTC).</summary>
    [JsonPropertyName("accepted_at")]
    public required DateTimeOffset AcceptedAt { get; init; }
}

/// <summary>Summary of an invitation for listing.</summary>
public record InvitationSummary
{
    /// <summary>Identifier of the invitation.</summary>
    [JsonPropertyName("invitation_id")]
    public required string InvitationId { get; init; }

    /// <summary>Identifier of the register.</summary>
    [JsonPropertyName("register_id")]
    public required string RegisterId { get; init; }

    /// <summary>The register name.</summary>
    [JsonPropertyName("register_name")]
    public string? RegisterName { get; init; }

    /// <summary>Identifier of the source org did.</summary>
    [JsonPropertyName("source_org_did")]
    public required string SourceOrgDid { get; init; }

    /// <summary>The source org name.</summary>
    [JsonPropertyName("source_org_name")]
    public string? SourceOrgName { get; init; }

    /// <summary>Identifier of the target org did.</summary>
    [JsonPropertyName("target_org_did")]
    public required string TargetOrgDid { get; init; }

    /// <summary>The target org name.</summary>
    [JsonPropertyName("target_org_name")]
    public string? TargetOrgName { get; init; }

    /// <summary>The direction.</summary>
    [JsonPropertyName("direction")]
    public required string Direction { get; init; }

    /// <summary>Current status of the resource.</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Timestamp at which the record expires (UTC).</summary>
    [JsonPropertyName("expires_at")]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Server timestamp when the record was created (UTC).</summary>
    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Response containing a list of invitations.</summary>
public record InvitationListResponse
{
    /// <summary>Collection of invitations associated with this resource.</summary>
    [JsonPropertyName("invitations")]
    public required IReadOnlyList<InvitationSummary> Invitations { get; init; }

    /// <summary>Total number of items available.</summary>
    [JsonPropertyName("total_count")]
    public required int TotalCount { get; init; }
}

/// <summary>
/// Raised when the Tenant Service returns a 4xx response for an invitation call.
/// Carries the server's message so the UI can surface it directly without reparsing.
/// </summary>
public sealed class InvitationApiException : Exception
{
    /// <summary>The status code.</summary>
    public System.Net.HttpStatusCode StatusCode { get; }

    public InvitationApiException(System.Net.HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
