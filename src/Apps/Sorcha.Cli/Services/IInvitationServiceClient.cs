// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for register invitation endpoints on the Tenant Service.
/// </summary>
public interface IInvitationServiceClient
{
    /// <summary>
    /// Creates a new register invitation.
    /// </summary>
    [Post("/api/organizations/{orgId}/register-invitations")]
    Task<InvitationResponse> CreateInvitationAsync(
        string orgId,
        [Body] CreateInvitationRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Lists register invitations for an organization.
    /// </summary>
    [Get("/api/organizations/{orgId}/register-invitations")]
    Task<List<InvitationResponse>> ListInvitationsAsync(
        string orgId,
        [Query] string? direction,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Accepts a register invitation using the encrypted token.
    /// </summary>
    [Post("/api/organizations/{orgId}/register-invitations/accept")]
    Task<AcceptInvitationResponse> AcceptInvitationAsync(
        string orgId,
        [Body] AcceptInvitationRequest request,
        [Header("Authorization")] string authorization);

    /// <summary>
    /// Revokes a pending register invitation.
    /// </summary>
    [Delete("/api/organizations/{orgId}/register-invitations/{invitationId}")]
    Task RevokeInvitationAsync(
        string orgId,
        string invitationId,
        [Header("Authorization")] string authorization);
}

// --- Request/Response DTOs ---

/// <summary>
/// Request to create a register invitation.
/// </summary>
public class CreateInvitationRequest
{
    public string RegisterId { get; set; } = string.Empty;
    public string TargetOrgDid { get; set; } = string.Empty;
    public int? ExpiresInHours { get; set; }
}

/// <summary>
/// Response from creating or listing an invitation.
/// </summary>
public class InvitationResponse
{
    public string Id { get; set; } = string.Empty;
    public string RegisterId { get; set; } = string.Empty;
    public string SourceOrgId { get; set; } = string.Empty;
    public string TargetOrgDid { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Token { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>
/// Request to accept an invitation.
/// </summary>
public class AcceptInvitationRequest
{
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Response from accepting an invitation.
/// </summary>
public class AcceptInvitationResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
