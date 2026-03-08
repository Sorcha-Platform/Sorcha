// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Lifecycle status of an organization invitation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InvitationStatus
{
    /// <summary>
    /// Invitation sent and awaiting acceptance.
    /// </summary>
    Pending,

    /// <summary>
    /// Invitation accepted — user has joined the organization.
    /// </summary>
    Accepted,

    /// <summary>
    /// Invitation expired (past ExpiresAt timestamp).
    /// </summary>
    Expired,

    /// <summary>
    /// Invitation revoked by an administrator.
    /// </summary>
    Revoked
}
