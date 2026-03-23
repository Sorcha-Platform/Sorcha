// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Service interface for managing organization register subscriptions.
/// Handles subscribing, unsubscribing, and querying register access for organizations.
/// </summary>
public interface IRegisterSubscriptionService
{
    /// <summary>
    /// Lists register subscriptions for an organization with pagination.
    /// </summary>
    /// <param name="orgId">Organization identifier.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paginated list of register subscriptions.</returns>
    Task<RegisterSubscriptionListResponse> ListAsync(Guid orgId, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Gets a specific register subscription for an organization.
    /// </summary>
    /// <param name="orgId">Organization identifier.</param>
    /// <param name="registerId">Register identifier (32-character hex string).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The subscription response, or null if not found.</returns>
    Task<RegisterSubscriptionResponse?> GetAsync(Guid orgId, string registerId, CancellationToken ct);

    /// <summary>
    /// Subscribes an organization to a public register.
    /// Creates a subscription with Status=Pending and SubscriptionType=Public.
    /// </summary>
    /// <param name="orgId">Organization identifier.</param>
    /// <param name="registerId">Register identifier (32-character hex string).</param>
    /// <param name="registerName">Optional cached display name for the register.</param>
    /// <param name="subscribedByUserId">User performing the subscription.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created subscription response.</returns>
    /// <exception cref="ArgumentException">Thrown when registerId is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a subscription already exists (409 Conflict).</exception>
    Task<RegisterSubscriptionResponse> SubscribeAsync(Guid orgId, string registerId, string? registerName, Guid subscribedByUserId, CancellationToken ct);

    /// <summary>
    /// Unsubscribes an organization from a register.
    /// Sets the subscription status to Revoked. Cannot unsubscribe from Owner subscriptions.
    /// </summary>
    /// <param name="orgId">Organization identifier.</param>
    /// <param name="registerId">Register identifier (32-character hex string).</param>
    /// <param name="revokedByUserId">User performing the revocation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="KeyNotFoundException">Thrown when subscription not found.</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to unsubscribe from an Owner subscription.</exception>
    Task UnsubscribeAsync(Guid orgId, string registerId, Guid revokedByUserId, CancellationToken ct);

    /// <summary>
    /// Creates an owner subscription when an organization creates a register.
    /// Owner subscriptions are immediately active and cannot be revoked via unsubscribe.
    /// </summary>
    /// <param name="orgId">Organization identifier.</param>
    /// <param name="registerId">Register identifier (32-character hex string).</param>
    /// <param name="registerName">Optional cached display name for the register.</param>
    /// <param name="subscribedByUserId">User who created the register.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created owner subscription response.</returns>
    /// <exception cref="ArgumentException">Thrown when registerId is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown when a subscription already exists.</exception>
    Task<RegisterSubscriptionResponse> CreateOwnerSubscriptionAsync(Guid orgId, string registerId, string? registerName, Guid subscribedByUserId, CancellationToken ct);

    /// <summary>
    /// Gets all active register subscriptions for an organization.
    /// </summary>
    /// <param name="orgId">Organization identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of active subscription responses.</returns>
    Task<IReadOnlyList<RegisterSubscriptionResponse>> GetSubscribedRegistersForOrgAsync(Guid orgId, CancellationToken ct);
}
