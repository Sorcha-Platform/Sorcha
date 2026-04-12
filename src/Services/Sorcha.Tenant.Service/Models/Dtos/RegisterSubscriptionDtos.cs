// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models.Dtos;

/// <summary>
/// Service-to-service request body for <c>POST /api/internal/register-owner-subscriptions</c>.
/// Sent by the Register Service immediately after finalising a register so that the
/// owning organisation is subscribed without the client having to make a second
/// admin-gated call. The Register Service has already verified owner attestations
/// cryptographically, so the admin role check on the public subscribe endpoint is
/// redundant here — trust is established by the service-to-service token instead.
/// </summary>
public record InternalOwnerSubscriptionRequest
{
    /// <summary>
    /// Owning organisation ID — resolved from the authenticated caller's JWT claims
    /// by the Register Service finalize endpoint, never accepted from an untrusted
    /// request body.
    /// </summary>
    [JsonPropertyName("organizationId")]
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// Register identifier (32-character hex string).
    /// </summary>
    [JsonPropertyName("registerId")]
    public required string RegisterId { get; init; }

    /// <summary>
    /// Human-readable register name (cached on the subscription row for display).
    /// </summary>
    [JsonPropertyName("registerName")]
    public string? RegisterName { get; init; }

    /// <summary>
    /// User ID recorded as the creator of the subscription. Resolved from the
    /// authenticated caller's <c>sub</c> claim by the Register Service.
    /// </summary>
    [JsonPropertyName("ownerUserId")]
    public required Guid OwnerUserId { get; init; }
}

/// <summary>
/// Request to subscribe an organization to a register.
/// </summary>
public record SubscribeRequest
{
    /// <summary>
    /// Register identifier (32-character hex string matching ^[a-f0-9]{32}$).
    /// </summary>
    [JsonPropertyName("register_id")]
    public required string RegisterId { get; init; }

    /// <summary>
    /// Optional human-readable register name (cached on the subscription).
    /// </summary>
    [JsonPropertyName("register_name")]
    public string? RegisterName { get; init; }

    /// <summary>
    /// Optional register description from peer advertisement (passed through to Register Service).
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Subscription type: "Owner" or "Public" (default). Owner subscriptions are
    /// immediately Active; Public subscriptions start as Pending.
    /// </summary>
    [JsonPropertyName("subscription_type")]
    public string? SubscriptionType { get; init; }
}

/// <summary>
/// Response representing a single register subscription.
/// </summary>
public record RegisterSubscriptionResponse
{
    /// <summary>
    /// Unique subscription identifier.
    /// </summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>
    /// Organization that holds this subscription.
    /// </summary>
    [JsonPropertyName("organization_id")]
    public required Guid OrganizationId { get; init; }

    /// <summary>
    /// Register identifier (32-character hex string).
    /// </summary>
    [JsonPropertyName("register_id")]
    public required string RegisterId { get; init; }

    /// <summary>
    /// Cached display name of the register.
    /// </summary>
    [JsonPropertyName("register_name")]
    public string? RegisterName { get; init; }

    /// <summary>
    /// How the organization gained access (Owner, Public, Invited).
    /// </summary>
    [JsonPropertyName("subscription_type")]
    public required string SubscriptionType { get; init; }

    /// <summary>
    /// Current lifecycle status (Pending, Active, Suspended, Revoked).
    /// </summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>
    /// Optional invitation ID for Invited subscriptions.
    /// </summary>
    [JsonPropertyName("invitation_id")]
    public string? InvitationId { get; init; }

    /// <summary>
    /// When the subscription was created (UTC).
    /// </summary>
    [JsonPropertyName("subscribed_at")]
    public required DateTimeOffset SubscribedAt { get; init; }

    /// <summary>
    /// User who created this subscription.
    /// </summary>
    [JsonPropertyName("subscribed_by_user_id")]
    public required Guid SubscribedByUserId { get; init; }

    /// <summary>
    /// When the subscription was revoked (UTC), if applicable.
    /// </summary>
    [JsonPropertyName("revoked_at")]
    public DateTimeOffset? RevokedAt { get; init; }

    /// <summary>
    /// User who revoked this subscription, if applicable.
    /// </summary>
    [JsonPropertyName("revoked_by_user_id")]
    public Guid? RevokedByUserId { get; init; }
}

/// <summary>
/// Paginated response for listing register subscriptions.
/// </summary>
public record RegisterSubscriptionListResponse
{
    /// <summary>
    /// Subscription items on the current page.
    /// </summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<RegisterSubscriptionResponse> Items { get; init; }

    /// <summary>
    /// Total number of subscriptions matching the criteria.
    /// </summary>
    [JsonPropertyName("total_count")]
    public required int TotalCount { get; init; }

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    [JsonPropertyName("page")]
    public required int Page { get; init; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    [JsonPropertyName("page_size")]
    public required int PageSize { get; init; }
}
