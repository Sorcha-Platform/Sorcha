// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Models;

/// <summary>One row in the transaction history feed.</summary>
public sealed record HistoryFeedEntry(
    string Id,
    HistoryFeedKind Kind,
    string Title,
    string? Subtitle,
    DateTimeOffset At);

/// <summary>Logical event kind for <see cref="HistoryFeedEntry"/>; drives the icon.</summary>
public enum HistoryFeedKind
{
    Issuance,
    Presentation,
    Verification,
    Submission,
    Revocation
}

/// <summary>One row in the recent activity feed.</summary>
public sealed record RecentActivityEntry(
    string Id,
    RecentActivityKind Kind,
    string Summary,
    DateTimeOffset At);

/// <summary>Logical kind for <see cref="RecentActivityEntry"/>; drives the icon.</summary>
public enum RecentActivityKind
{
    Issuance,
    Presentation,
    Verification,
    Submission,
    Revocation
}
