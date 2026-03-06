// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Refit;
using Sorcha.Cli.Models;

namespace Sorcha.Cli.Services;

/// <summary>
/// Refit client interface for the Participant Identity API (via Tenant Service).
/// </summary>
public interface IParticipantServiceClient
{
    /// <summary>
    /// Registers a new participant in an organization.
    /// </summary>
    [Post("/api/organizations/{orgId}/participants")]
    Task<ParticipantIdentity> RegisterParticipantAsync(string orgId, [Body] RegisterParticipantRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Lists participants in an organization.
    /// </summary>
    [Get("/api/organizations/{orgId}/participants")]
    Task<List<ParticipantIdentity>> ListParticipantsAsync(string orgId, [Header("Authorization")] string authorization);

    /// <summary>
    /// Gets a participant by ID.
    /// </summary>
    [Get("/api/organizations/{orgId}/participants/{id}")]
    Task<ParticipantIdentity> GetParticipantAsync(string orgId, string id, [Header("Authorization")] string authorization);

    /// <summary>
    /// Updates a participant.
    /// </summary>
    [Put("/api/organizations/{orgId}/participants/{id}")]
    Task<ParticipantIdentity> UpdateParticipantAsync(string orgId, string id, [Body] UpdateParticipantRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Searches for participants across accessible organizations.
    /// </summary>
    [Post("/api/participants/search")]
    Task<List<ParticipantIdentity>> SearchParticipantsAsync([Body] SearchParticipantsRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Initiates a wallet link challenge.
    /// </summary>
    [Post("/api/participants/{id}/wallet-links")]
    Task<WalletLinkChallengeResponse> InitiateWalletLinkAsync(string id, [Body] InitiateWalletLinkRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Verifies a wallet link challenge.
    /// </summary>
    [Post("/api/participants/{id}/wallet-links/{challengeId}/verify")]
    Task<LinkedWalletAddress> VerifyWalletLinkAsync(string id, string challengeId, [Body] VerifyWalletLinkRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Lists wallet links for a participant.
    /// </summary>
    [Get("/api/participants/{id}/wallet-links")]
    Task<List<LinkedWalletAddress>> ListWalletLinksAsync(string id, [Header("Authorization")] string authorization);

    /// <summary>
    /// Suspends an active participant.
    /// </summary>
    [Post("/api/organizations/{orgId}/participants/{id}/suspend")]
    Task SuspendParticipantAsync(string orgId, string id, [Header("Authorization")] string authorization);

    /// <summary>
    /// Reactivates a suspended participant.
    /// </summary>
    [Post("/api/organizations/{orgId}/participants/{id}/reactivate")]
    Task ReactivateParticipantAsync(string orgId, string id, [Header("Authorization")] string authorization);

    /// <summary>
    /// Publishes a participant identity record to a register.
    /// </summary>
    [Post("/api/organizations/{orgId}/participants/publish")]
    Task<PublishParticipantResult> PublishParticipantAsync(string orgId, [Body] PublishParticipantRequest request, [Header("Authorization")] string authorization);

    /// <summary>
    /// Revokes a published participant record from a register.
    /// </summary>
    [Delete("/api/organizations/{orgId}/participants/publish/{id}")]
    Task<PublishParticipantResult> UnpublishParticipantAsync(string orgId, string id, [Query] string registerId, [Query] string signerWalletAddress, [Header("Authorization")] string authorization);
}
