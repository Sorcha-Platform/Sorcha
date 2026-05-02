// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Represents a Web Push API subscription for browser notifications.
/// </summary>
public class PushSubscription
{
    /// <summary>Unique identifier for the resource.</summary>
    public Guid Id { get; set; }
    /// <summary>Identifier of the user.</summary>
    public Guid UserId { get; set; }
    /// <summary>Endpoint URL.</summary>
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>The p256dh key.</summary>
    public string P256dhKey { get; set; } = string.Empty;
    /// <summary>The auth key.</summary>
    public string AuthKey { get; set; } = string.Empty;
    /// <summary>Server timestamp when the record was created (UTC).</summary>
    public DateTime CreatedAt { get; set; }
    /// <summary>Server timestamp when the record was last updated (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
