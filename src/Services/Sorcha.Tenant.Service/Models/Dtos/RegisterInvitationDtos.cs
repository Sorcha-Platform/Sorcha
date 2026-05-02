// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Request to create a register invitation for a target organization.
/// </summary>
public record CreateRegisterInvitationRequest
{
    /// <summary>
    /// Register identifier (32-character hex string matching ^[a-f0-9]{32}$).
    /// </summary>
    [JsonPropertyName("register_id")]
    public required string RegisterId { get; init; }

    /// <summary>
    /// Target organization DID (did:sorcha:org:{walletAddress}).
    /// </summary>
    [JsonPropertyName("target_org_did")]
    public required string TargetOrgDid { get; init; }

    /// <summary>
    /// Number of days until invitation expires. Default: 7, Range: 1-90.
    /// </summary>
    [JsonPropertyName("expires_in_days")]
    public int ExpiresInDays { get; init; } = 7;
}

/// <summary>
/// Response returned after creating a register invitation.
/// </summary>
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

/// <summary>
/// Request to accept a register invitation.
/// </summary>
public record AcceptInvitationRequest
{
    /// <summary>
    /// The base64url-encoded invitation token received out-of-band.
    /// </summary>
    [JsonPropertyName("invitation_token")]
    public required string InvitationToken { get; init; }
}

/// <summary>
/// Response returned after accepting a register invitation.
/// </summary>
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

/// <summary>
/// Summary of a register invitation (for listing).
/// </summary>
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

/// <summary>
/// Response containing a list of invitations.
/// </summary>
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
/// The decrypted payload inside an invitation token.
/// </summary>
public record InvitationPayload
{
    /// <summary>Identifier of the register.</summary>
    [JsonPropertyName("register_id")]
    public required string RegisterId { get; init; }

    /// <summary>Identifier of the source org did.</summary>
    [JsonPropertyName("source_org_did")]
    public required string SourceOrgDid { get; init; }

    /// <summary>Identifier of the target org did.</summary>
    [JsonPropertyName("target_org_did")]
    public required string TargetOrgDid { get; init; }

    /// <summary>Identifier of the invitation.</summary>
    [JsonPropertyName("invitation_id")]
    public required string InvitationId { get; init; }

    /// <summary>Timestamp at which the record expires (UTC).</summary>
    [JsonPropertyName("expires_at")]
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The nonce.</summary>
    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }

    /// <summary>The register name.</summary>
    [JsonPropertyName("register_name")]
    public string? RegisterName { get; init; }

    /// <summary>The source org name.</summary>
    [JsonPropertyName("source_org_name")]
    public string? SourceOrgName { get; init; }
}

/// <summary>
/// Wire format for the invitation token (base64url JSON envelope).
/// </summary>
public record InvitationTokenEnvelope
{
    /// <summary>Token format version.</summary>
    [JsonPropertyName("v")]
    public int Version { get; init; } = 1;

    /// <summary>ED25519 signature over the encrypted payload (base64url).</summary>
    [JsonPropertyName("sig")]
    public required string Signature { get; init; }

    /// <summary>Encrypted payload (base64url).</summary>
    [JsonPropertyName("enc")]
    public required string EncryptedPayload { get; init; }

    /// <summary>Source org DID for signature verification.</summary>
    [JsonPropertyName("sender")]
    public required string Sender { get; init; }
}
