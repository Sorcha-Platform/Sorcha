// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// Represents the current push subscription status for the user.
/// </summary>
public record PushSubscriptionStatus(bool HasActiveSubscription);

/// <summary>
/// Request model for creating a push notification subscription.
/// </summary>
public record PushSubscriptionRequest
{
    /// <summary>Push service endpoint URL.</summary>
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>P-256 Diffie-Hellman public key for message encryption.</summary>
    public string P256dh { get; init; } = string.Empty;

    /// <summary>Authentication secret for the push subscription.</summary>
    public string Auth { get; init; } = string.Empty;
}

/// <summary>
/// Response model for a push subscription operation.
/// </summary>
public record PushSubscriptionResponse(bool Subscribed);
