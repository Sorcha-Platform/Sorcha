// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Represents an organization's subscription to a register on the Sorcha platform.
/// Tracks which registers an organization can access and its subscription status.
/// </summary>
public class OrganizationRegisterSubscription
{
    /// <summary>
    /// Unique subscription identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to the subscribing organization.
    /// </summary>
    public Guid OrganizationId { get; set; }

    /// <summary>
    /// Register identifier (32-character hex string).
    /// Must match pattern ^[a-f0-9]{32}$.
    /// </summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Cached display name of the register (max 38 characters).
    /// Updated on subscription creation and refresh.
    /// </summary>
    public string? RegisterName { get; set; }

    /// <summary>
    /// How the organization gained access to this register.
    /// </summary>
    public SubscriptionType SubscriptionType { get; set; }

    /// <summary>
    /// Current lifecycle status of the subscription.
    /// </summary>
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Pending;

    /// <summary>
    /// Optional invitation ID for Invited subscriptions (Phase 2).
    /// </summary>
    public string? InvitationId { get; set; }

    /// <summary>
    /// When the subscription was created (UTC).
    /// </summary>
    public DateTimeOffset SubscribedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// User who created this subscription.
    /// </summary>
    public Guid SubscribedByUserId { get; set; }

    /// <summary>
    /// When the subscription was revoked (UTC), if applicable.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// User who revoked this subscription, if applicable.
    /// </summary>
    public Guid? RevokedByUserId { get; set; }

    /// <summary>
    /// Navigation property to the subscribing organization.
    /// </summary>
    public Organization? Organization { get; set; }
}

/// <summary>
/// How the organization gained access to a register.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubscriptionType
{
    /// <summary>
    /// Organization owns/created the register.
    /// </summary>
    Owner,

    /// <summary>
    /// Register is publicly accessible.
    /// </summary>
    Public,

    /// <summary>
    /// Organization was invited to the register.
    /// </summary>
    Invited
}

/// <summary>
/// Lifecycle status of a register subscription.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubscriptionStatus
{
    /// <summary>
    /// Subscription is pending approval or activation.
    /// </summary>
    Pending,

    /// <summary>
    /// Subscription is active and the organization can access the register.
    /// </summary>
    Active,

    /// <summary>
    /// Subscription has been temporarily suspended.
    /// </summary>
    Suspended,

    /// <summary>
    /// Subscription has been permanently revoked.
    /// </summary>
    Revoked
}
