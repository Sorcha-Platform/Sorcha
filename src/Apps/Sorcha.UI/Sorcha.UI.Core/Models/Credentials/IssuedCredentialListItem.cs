// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Lightweight summary of an issued credential for list / table views.
/// </summary>
public class IssuedCredentialListItem
{
    /// <summary>Unique identifier of the credential.</summary>
    public required string CredentialId { get; init; }

    /// <summary>Credential type identifier.</summary>
    public required string Type { get; init; }

    /// <summary>Display name of the credential's subject.</summary>
    public required string IssuedToName { get; init; }

    /// <summary>DID of the credential's subject.</summary>
    public required string IssuedToDid { get; init; }

    /// <summary>Name of the blueprint that produced this credential.</summary>
    public required string BlueprintName { get; init; }

    /// <summary>When the credential was issued.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>When the credential expires, if applicable.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Current lifecycle status (e.g. Active, Revoked, Suspended).</summary>
    public required string Status { get; init; }

    /// <summary>Number of times this credential has been presented.</summary>
    public required int PresentationCount { get; init; }

    /// <summary>
    /// Whether the credential will expire within the next 30 days and has not yet expired.
    /// </summary>
    public bool IsExpiringSoon =>
        ExpiresAt.HasValue &&
        ExpiresAt.Value > DateTimeOffset.UtcNow &&
        ExpiresAt.Value <= DateTimeOffset.UtcNow.AddDays(30);

    /// <summary>
    /// Whether the credential has already passed its expiry date.
    /// </summary>
    public bool IsExpired =>
        ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow;
}
