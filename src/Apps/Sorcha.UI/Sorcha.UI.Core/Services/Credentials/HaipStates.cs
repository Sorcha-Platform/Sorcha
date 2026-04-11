// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// Status values for HAIP credential offers.
/// Matches the OfferStatus enum in the HAIP Service.
/// </summary>
public static class HaipOfferStates
{
    public const string Pending = "Pending";
    public const string Exchanged = "Exchanged";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";
}

/// <summary>
/// State values for HAIP presentation requests.
/// Matches the PresentationRequestState enum in the HAIP Service.
/// </summary>
public static class HaipVerificationStates
{
    public const string Pending = "Pending";
    public const string Submitted = "Submitted";
    public const string Verified = "Verified";
    public const string Denied = "Denied";
    public const string Expired = "Expired";
    public const string Cancelled = "Cancelled";

    /// <summary>Whether the state is terminal (no further transitions expected).</summary>
    public static bool IsTerminal(string state) =>
        state is Verified or Denied or Expired or Cancelled;
}
