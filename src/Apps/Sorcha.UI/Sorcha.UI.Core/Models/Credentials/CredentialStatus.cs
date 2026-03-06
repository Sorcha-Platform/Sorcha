// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Credentials;

/// <summary>
/// Shared constants for credential status values.
/// Replaces inline magic strings across all credential UI components.
/// </summary>
public static class CredentialStatus
{
    public const string Active = "Active";
    public const string Suspended = "Suspended";
    public const string Revoked = "Revoked";
    public const string Expired = "Expired";
    public const string Consumed = "Consumed";

    /// <summary>
    /// All valid credential status values, for iteration in filters and UI chips.
    /// </summary>
    public static readonly IReadOnlyList<string> AllStatuses =
        [Active, Suspended, Revoked, Expired, Consumed];
}
