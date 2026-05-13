// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Well-known notification type strings for credential lifecycle events.
/// </summary>
public static class CredentialNotificationType
{
    public const string Issued = "Issued";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string Suspended = "Suspended";
    public const string Revoked = "Revoked";
    public const string PresentationRequested = "PresentationRequested";
}

/// <summary>
/// A real-time notification about a credential lifecycle event.
/// </summary>
public class CredentialNotification
{
    /// <summary>
    /// The notification type. Use <see cref="CredentialNotificationType"/> constants.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>The credential this notification relates to.</summary>
    public required string CredentialId { get; init; }

    /// <summary>Human-readable credential type label.</summary>
    public required string CredentialType { get; init; }

    /// <summary>Display name of the issuing organisation, if relevant.</summary>
    public string? IssuerName { get; init; }

    /// <summary>Display name of the credential recipient, if relevant.</summary>
    public string? RecipientName { get; init; }

    /// <summary>When the notification was raised.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Optional human-readable message to display alongside the notification.</summary>
    public string? Message { get; init; }
}
