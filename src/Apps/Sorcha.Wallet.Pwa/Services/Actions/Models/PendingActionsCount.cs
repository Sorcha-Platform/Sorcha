// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Pwa.Services.Actions.Models;

/// <summary>
/// Feature 151 — the count of the citizen's outstanding actions, for the navigation badge.
/// <see cref="UrgentCount"/> mirrors the server field but is always 0 today (urgency-aware
/// counting is a future server iteration) and is not relied upon by the badge.
/// </summary>
/// <param name="Count">Total outstanding actions awaiting the citizen.</param>
/// <param name="UrgentCount">Outstanding actions flagged urgent (currently always 0).</param>
public sealed record PendingActionsCount(int Count, int UrgentCount)
{
    /// <summary>An empty count (nothing outstanding).</summary>
    public static readonly PendingActionsCount Empty = new(0, 0);
}
