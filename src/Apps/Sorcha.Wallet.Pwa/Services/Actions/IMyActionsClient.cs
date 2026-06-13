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
    /// Returns the actions currently awaiting the citizen's input ("their turn"); most-pressing
    /// ordering is applied by the caller. An empty list means "nothing waiting on you". A transient
    /// failure (network / non-success / malformed body) <b>throws</b> so the caller can retain its
    /// last-known list and surface a non-blocking notice (FR-010) — failure is never conflated with
    /// an empty inbox.
    /// </summary>
    Task<IReadOnlyList<PendingActionItem>> GetPendingAsync(
        int page = 1, int pageSize = 20, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of outstanding actions for the navigation badge. A transient failure
    /// throws; the badge owner retains its last-known count.
    /// </summary>
    Task<PendingActionsCount> GetCountAsync(CancellationToken ct = default);
}
