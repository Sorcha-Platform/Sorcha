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

    /// <summary>Whether the status is terminal (no further transitions expected).</summary>
    public static bool IsTerminal(string status) =>
        status is Exchanged or Expired or Cancelled;
}

/// <summary>
/// Shared polling constants for HAIP QR card components.
/// </summary>
public static class HaipPollingDefaults
{
    /// <summary>Interval between poll ticks.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>Maximum number of poll ticks before giving up (5 minutes at 2s intervals).</summary>
    public const int MaxPollTicks = 150;

    /// <summary>Delay (ms) before auto-closing a dialog after a successful exchange or collection.</summary>
    public const int SuccessCloseDelayMs = 1500;

    /// <summary>Delay (ms) before auto-closing a dialog after successful verification.</summary>
    public const int VerifiedCloseDelayMs = 2000;

    /// <summary>Delay (ms) before auto-closing a dialog after reaching an error state.</summary>
    public const int ErrorCloseDelayMs = 3000;
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
