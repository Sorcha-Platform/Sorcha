// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Pwa.Services.Actions.Models;

namespace Sorcha.Wallet.Pwa.Services.Actions;

/// <summary>
/// Feature 151 — the single source of truth for "most pressing first" inbox ordering:
/// Urgent → Warning → Normal, then earliest <see cref="PendingActionItem.Deadline"/> first
/// (items with no deadline last), then earliest <see cref="PendingActionItem.ReceivedAt"/>.
/// </summary>
public static class PendingActionOrdering
{
    /// <summary>Returns a new ordered list; the input is not mutated.</summary>
    public static IReadOnlyList<PendingActionItem> Order(IEnumerable<PendingActionItem> items) =>
        items
            .OrderByDescending(i => (int)i.Urgency)
            .ThenBy(i => i.Deadline ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.ReceivedAt)
            .ToList();
}
