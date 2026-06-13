// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Pwa.Services.Actions.Models;

namespace Sorcha.Wallet.Pwa.Services.Actions;

/// <summary>
/// Feature 151 (citizen workflow inbox) — PWA-side client for the citizen's "actions waiting on me"
/// surface. Reads the Blueprint Service's <c>GET /api/actions/pending</c> (list) and
/// <c>GET /api/actions/pending/count</c> (badge) endpoints, which already resolve the citizen's
/// wallet(s) from a consumer-tier token via <c>platform_user_id</c> and return only actions where
/// the citizen is the designated actor (their turn). The shared <see cref="HttpClient"/> carries
/// the bearer token chain; callers pass no token.
/// </summary>
public interface IMyActionsClient
{
    /// <summary>
    /// Returns the actions currently awaiting the citizen's input ("their turn"), most-pressing
    /// ordering is applied by the caller. Returns an empty list on a transient failure (the caller
    /// retains its last-known list and surfaces a non-blocking notice).
    /// </summary>
    Task<IReadOnlyList<PendingActionItem>> GetPendingAsync(
        int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of outstanding actions for the navigation badge. Returns
    /// <see cref="PendingActionsCount.Empty"/> on a transient failure.
    /// </summary>
    Task<PendingActionsCount> GetCountAsync(CancellationToken ct = default);
}
